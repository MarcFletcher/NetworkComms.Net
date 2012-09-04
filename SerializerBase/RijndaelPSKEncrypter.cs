using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.IO;

namespace SerializerBase
{
    public class RijndaelPSKEncrypter : DataProcessor
    {        
        private static readonly string PasswordOption = "RijndaelPSKEncrypter_PASSWORD";
        private static readonly byte[] SALT = new byte[] { 118, 100, 123, 136, 20, 242, 170, 227, 97, 168, 101, 177, 214, 211, 118, 137 };

        RijndaelManaged encrypter = new RijndaelManaged();
        
        private RijndaelPSKEncrypter() { }

        public override byte Identifier
        {
            get { return 4; }
        }

        public override void ForwardProcessDataStream(System.IO.Stream inStream, System.IO.Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(options[PasswordOption], SALT);

            var transform = encrypter.CreateEncryptor(pdb.GetBytes(32), pdb.GetBytes(16));
            
            CryptoStream csEncrypt = new CryptoStream(outStream, transform, CryptoStreamMode.Write);
            inStream.CopyTo(csEncrypt);
            inStream.Flush();
            csEncrypt.FlushFinalBlock();

            writtenBytes = outStream.Position; 
        }

        public override void ReverseProcessDataStream(System.IO.Stream inStream, System.IO.Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(options[PasswordOption], SALT);

            var transform = encrypter.CreateDecryptor(pdb.GetBytes(32), pdb.GetBytes(16));

            CryptoStream csDecrypt = new CryptoStream(outStream, transform, CryptoStreamMode.Write);
            inStream.CopyTo(csDecrypt);
            inStream.Flush();
            csDecrypt.FlushFinalBlock();

            writtenBytes = outStream.Position; 
        }
        
        public static void AddPasswordToOptions(Dictionary<string, string> options, string password)
        {
            options[PasswordOption] = password;
        }
    }
}
