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
        static string IVOption = "RijndaelPSKEncrypter_IV";
        static string KeyOption = "RijndaelPSKEncrypter_KEY";

        RijndaelManaged encrypter = new RijndaelManaged();

        private RijndaelPSKEncrypter() { }

        public override byte Identifier
        {
            get { return 4; }
        }

        public override void ForwardProcessDataStream(System.IO.Stream inStream, System.IO.Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            var transform = encrypter.CreateEncryptor(StringToByteArray(options[IVOption]), StringToByteArray(options[KeyOption]));
                         
            using (CryptoStream csEncrypt = new CryptoStream(outStream, transform, CryptoStreamMode.Write))
                inStream.CopyTo(outStream);

            writtenBytes = outStream.Position; 
        }

        public override void ReverseProcessDataStream(System.IO.Stream inStream, System.IO.Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            var transform = encrypter.CreateEncryptor(StringToByteArray(options[IVOption]), StringToByteArray(options[KeyOption]));

            using (CryptoStream csEncrypt = new CryptoStream(outStream, transform, CryptoStreamMode.Write))
                inStream.CopyTo(outStream);

            writtenBytes = outStream.Position; 
        }

        private byte[] StringToByteArray(string IVKeyAsString)
        {
            var res = Encoding.Unicode.GetBytes(IVKeyAsString);

            if (res.Length <= 16)
            {
                if (res.Length == 16)
                    return res;

                byte[] padded = new byte[16];
                Buffer.BlockCopy(res, 0, padded, 0, res.Length);
                return padded;
            }
            else if (res.Length <= 24)
            {
                if (res.Length == 24)
                    return res;

                byte[] padded = new byte[24];
                Buffer.BlockCopy(res, 0, padded, 0, res.Length);
                return padded;
            }
            else if (res.Length <= 32)
            {
                if (res.Length == 32)
                    return res;

                byte[] padded = new byte[32];
                Buffer.BlockCopy(res, 0, padded, 0, res.Length);
                return padded;
            }
            else
                throw new InvalidOperationException();
        }

        public static void AddEncryptionOptions(Dictionary<string, string> options, byte[] IV, byte[] Key)
        {
            options[IVOption] = new string(Encoding.Unicode.GetChars(IV));
            options[KeyOption] = new string(Encoding.Unicode.GetChars(Key));
        }
    }
}
