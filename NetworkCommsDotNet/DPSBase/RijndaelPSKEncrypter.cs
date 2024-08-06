// 
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
// 

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

using System.Security.Cryptography;
using NetworkCommsDotNet.Tools;

namespace NetworkCommsDotNet.DPSBase
{

    /// <summary>
    /// <see cref="DataProcessor"/> which encrypts/decrypts data using the Rijndael algorithm and a pre-shared password
    /// </summary>
    [DataSerializerProcessor(4)]
    [SecurityCriticalDataProcessor(true)]
    public class RijndaelPSKEncrypter : DataProcessor, IDisposable
    {        
        private const string PasswordOption = "RijndaelPSKEncrypter_PASSWORD";
        private static readonly byte[] SALT = new byte[] { 118, 100, 123, 136, 20, 242, 170, 227, 97, 168, 101, 177, 214, 211, 118, 137 };

        SymmetricAlgorithm encrypter = new RijndaelManaged();

        private RijndaelPSKEncrypter() 
        {
            encrypter.BlockSize = 128;            
        }
        
        /// <inheritdoc />
        public override void ForwardProcessDataStream(System.IO.Stream inStream, System.IO.Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            if (options == null) throw new ArgumentNullException("options");
            else if (!options.ContainsKey(PasswordOption)) throw new ArgumentException("Options must contain encryption key", "options");

            if (outStream == null) throw new ArgumentNullException("outStream");

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
        }

        /// <inheritdoc />
        public override void ReverseProcessDataStream(System.IO.Stream inStream, System.IO.Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            if (options == null) throw new ArgumentNullException("options");
            else if (!options.ContainsKey(PasswordOption)) throw new ArgumentException("Options must contain encryption key", "options");

            if (outStream == null) throw new ArgumentNullException("outStream");

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
            if (encrypter != null)
                (encrypter as IDisposable).Dispose();
        }
    }

}