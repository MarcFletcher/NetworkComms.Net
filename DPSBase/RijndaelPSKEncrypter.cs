//  Copyright 2011-2013 Marc Fletcher, Matthew Dean
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
//  A commercial license of this software can also be purchased. 
//  Please see <http://www.networkcomms.net/licensing/> for details.

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

#if NETFX_CORE
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;
using Windows.Security.Cryptography;
#else
using System.Security.Cryptography;
#endif

#if ANDROID
using PreserveAttribute = Android.Runtime.PreserveAttribute;
#elif iOS
using PreserveAttribute = MonoTouch.Foundation.PreserveAttribute;
#endif

namespace DPSBase
{
#if !FREETRIAL
    /// <summary>
    /// <see cref="DataProcessor"/> which encrypts/decrypts data using the Rijndael algorithm and a pre-shared password
    /// </summary>
    [DataSerializerProcessor(4)]
    public class RijndaelPSKEncrypter : DataProcessor, IDisposable
    {        
        private const string PasswordOption = "RijndaelPSKEncrypter_PASSWORD";
        private static readonly byte[] SALT = new byte[] { 118, 100, 123, 136, 20, 242, 170, 227, 97, 168, 101, 177, 214, 211, 118, 137 };

#if WINDOWS_PHONE
        SymmetricAlgorithm encrypter = new AesManaged();
#elif !NETFX_CORE
        SymmetricAlgorithm encrypter = new RijndaelManaged();
#endif

#if ANDROID || iOS
        [Preserve]
#endif
        private RijndaelPSKEncrypter() 
        {
#if !NETFX_CORE
            encrypter.BlockSize = 128;            
#endif
        }
        
        /// <inheritdoc />
        public override void ForwardProcessDataStream(System.IO.Stream inStream, System.IO.Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            if (options == null) throw new ArgumentNullException("options");
            else if (!options.ContainsKey(PasswordOption)) throw new ArgumentException("Options must contain encryption key", "options");

            if (outStream == null) throw new ArgumentNullException("outStream");

#if NETFX_CORE
            IBuffer pwBuffer = CryptographicBuffer.ConvertStringToBinary(options[PasswordOption], BinaryStringEncoding.Utf8);
            IBuffer saltBuffer = CryptographicBuffer.CreateFromByteArray(SALT);
            IBuffer plainBuffer = (new DataReader(inStream.AsInputStream())).ReadBuffer((uint)inStream.Length);

            // Derive key material for password size 32 bytes for AES256 algorithm
            KeyDerivationAlgorithmProvider keyDerivationProvider = Windows.Security.Cryptography.Core.KeyDerivationAlgorithmProvider.OpenAlgorithm("PBKDF2_SHA1");
            // using salt and 1000 iterations
            KeyDerivationParameters pbkdf2Parms = KeyDerivationParameters.BuildForPbkdf2(saltBuffer, 1000);

            // create a key based on original key and derivation parmaters
            CryptographicKey keyOriginal = keyDerivationProvider.CreateKey(pwBuffer);
            IBuffer keyMaterial = CryptographicEngine.DeriveKeyMaterial(keyOriginal, pbkdf2Parms, 32);
            CryptographicKey derivedPwKey = keyDerivationProvider.CreateKey(pwBuffer);

            // derive buffer to be used for encryption salt from derived password key 
            IBuffer saltMaterial = CryptographicEngine.DeriveKeyMaterial(derivedPwKey, pbkdf2Parms, 16);

            // display the buffers - because KeyDerivationProvider always gets cleared after each use, they are very similar unforunately
            string keyMaterialString = CryptographicBuffer.EncodeToBase64String(keyMaterial);
            string saltMaterialString = CryptographicBuffer.EncodeToBase64String(saltMaterial);

            SymmetricKeyAlgorithmProvider symProvider = SymmetricKeyAlgorithmProvider.OpenAlgorithm("AES_CBC_PKCS7");
            // create symmetric key from derived password key
            CryptographicKey symmKey = symProvider.CreateSymmetricKey(keyMaterial);

            // encrypt data buffer using symmetric key and derived salt material
            IBuffer resultBuffer = CryptographicEngine.Encrypt(symmKey, plainBuffer, saltMaterial);
            (new DataWriter(outStream.AsOutputStream())).WriteBuffer(resultBuffer);
            writtenBytes = outStream.Position;
#else
            Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(options[PasswordOption], SALT);
            var key = pdb.GetBytes(32);
            pdb.Reset();
            var iv = pdb.GetBytes(16);

            using (var transform = encrypter.CreateEncryptor(key, iv))
            {
                using (MemoryStream internalStream = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(internalStream, transform, CryptoStreamMode.Write))
                    {
                        StreamTools.Write(inStream, csEncrypt);
                        inStream.Flush();
                        csEncrypt.FlushFinalBlock();

                        internalStream.Seek(0, 0);
                        StreamTools.Write(internalStream, outStream);
                        writtenBytes = outStream.Position;
                    }                    
                }
            }
#endif
        }

        /// <inheritdoc />
        public override void ReverseProcessDataStream(System.IO.Stream inStream, System.IO.Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            if (options == null) throw new ArgumentNullException("options");
            else if (!options.ContainsKey(PasswordOption)) throw new ArgumentException("Options must contain encryption key", "options");

            if (outStream == null) throw new ArgumentNullException("outStream");

            #if NETFX_CORE
            IBuffer pwBuffer = CryptographicBuffer.ConvertStringToBinary(options[PasswordOption], BinaryStringEncoding.Utf8);
            IBuffer saltBuffer = CryptographicBuffer.CreateFromByteArray(SALT);
            IBuffer cryptBuffer = (new DataReader(inStream.AsInputStream())).ReadBuffer((uint)inStream.Length);

            // Derive key material for password size 32 bytes for AES256 algorithm
            KeyDerivationAlgorithmProvider keyDerivationProvider = Windows.Security.Cryptography.Core.KeyDerivationAlgorithmProvider.OpenAlgorithm("PBKDF2_SHA1");
            // using salt and 1000 iterations
            KeyDerivationParameters pbkdf2Parms = KeyDerivationParameters.BuildForPbkdf2(saltBuffer, 1000);

            // create a key based on original key and derivation parmaters
            CryptographicKey keyOriginal = keyDerivationProvider.CreateKey(pwBuffer);
            IBuffer keyMaterial = CryptographicEngine.DeriveKeyMaterial(keyOriginal, pbkdf2Parms, 32);
            CryptographicKey derivedPwKey = keyDerivationProvider.CreateKey(pwBuffer);

            // derive buffer to be used for encryption salt from derived password key 
            IBuffer saltMaterial = CryptographicEngine.DeriveKeyMaterial(derivedPwKey, pbkdf2Parms, 16);

            // display the buffers - because KeyDerivationProvider always gets cleared after each use, they are very similar unforunately
            string keyMaterialString = CryptographicBuffer.EncodeToBase64String(keyMaterial);
            string saltMaterialString = CryptographicBuffer.EncodeToBase64String(saltMaterial);

            SymmetricKeyAlgorithmProvider symProvider = SymmetricKeyAlgorithmProvider.OpenAlgorithm("AES_CBC_PKCS7");
            // create symmetric key from derived password key
            CryptographicKey symmKey = symProvider.CreateSymmetricKey(keyMaterial);

            // encrypt data buffer using symmetric key and derived salt material
            IBuffer resultBuffer = CryptographicEngine.Decrypt(symmKey, cryptBuffer, saltMaterial);
            (new DataWriter(outStream.AsOutputStream())).WriteBuffer(resultBuffer);
            writtenBytes = outStream.Position;
#else

            Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(options[PasswordOption], SALT);
            var key = pdb.GetBytes(32);
            pdb.Reset();
            var iv = pdb.GetBytes(16);

            using (var transform = encrypter.CreateDecryptor(key, iv))
            {
                using (MemoryStream internalStream = new MemoryStream())
                {
                    using (CryptoStream csDecrypt = new CryptoStream(internalStream, transform, CryptoStreamMode.Write))
                    {
                        StreamTools.Write(inStream, csDecrypt);
                        inStream.Flush();
                        csDecrypt.FlushFinalBlock();

                        internalStream.Seek(0, 0);
                        StreamTools.Write(internalStream, outStream);
                        writtenBytes = outStream.Position;
                    }
                }
            }
#endif
        }
        
        /// <summary>
        /// Adds a password, using the correct key, to a Dictionary
        /// </summary>
        /// <param name="options">The Dictionary to add the option to</param>
        /// <param name="password">The password</param>        
        public static void AddPasswordToOptions(Dictionary<string, string> options, string password)
        {
            if (options == null) throw new ArgumentNullException("options");

            options[PasswordOption] = password;
        }

        /// <summary>
        /// Dispose of all resources.
        /// </summary>
        public void Dispose()
        {
#if !NETFX_CORE
            if (encrypter != null)
                (encrypter as IDisposable).Dispose();
#endif
        }
    }
#endif
}