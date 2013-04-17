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
using System.Security.Cryptography;
using System.IO;

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
    public class RijndaelPSKEncrypter : DataProcessor
    {        
        private static readonly string PasswordOption = "RijndaelPSKEncrypter_PASSWORD";
        private static readonly byte[] SALT = new byte[] { 118, 100, 123, 136, 20, 242, 170, 227, 97, 168, 101, 177, 214, 211, 118, 137 };

#if WINDOWS_PHONE
        SymmetricAlgorithm encrypter = new AesManaged();
#else
        SymmetricAlgorithm encrypter = new RijndaelManaged();
#endif

#if ANDROID || iOS
        [Preserve]
#endif
        private RijndaelPSKEncrypter() 
        {
            encrypter.BlockSize = 128; 
        }
        
        /// <inheritdoc />
        public override void ForwardProcessDataStream(System.IO.Stream inStream, System.IO.Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(options[PasswordOption], SALT);
            
            var transform = encrypter.CreateEncryptor(pdb.GetBytes(32), pdb.GetBytes(16));
            
            CryptoStream csEncrypt = new CryptoStream(outStream, transform, CryptoStreamMode.Write);
            AsyncStreamCopier.CopyStreamTo(inStream, csEncrypt);
            inStream.Flush();
            csEncrypt.FlushFinalBlock();

            writtenBytes = outStream.Position; 
        }

        /// <inheritdoc />
        public override void ReverseProcessDataStream(System.IO.Stream inStream, System.IO.Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(options[PasswordOption], SALT);

            var transform = encrypter.CreateDecryptor(pdb.GetBytes(32), pdb.GetBytes(16));

            CryptoStream csDecrypt = new CryptoStream(outStream, transform, CryptoStreamMode.Write);
            AsyncStreamCopier.CopyStreamTo(inStream, csDecrypt);
            inStream.Flush();
            csDecrypt.FlushFinalBlock();

            writtenBytes = outStream.Position; 
        }
        
        /// <summary>
        /// Adds a password, using the correct key, to a Dicitonary
        /// </summary>
        /// <param name="options">The Dictionary to add the optoion to</param>
        /// <param name="password">The password</param>        
        public static void AddPasswordToOptions(Dictionary<string, string> options, string password)
        {
            options[PasswordOption] = password;
        }
    }
#endif
}