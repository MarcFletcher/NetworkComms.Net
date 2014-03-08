//  Copyright 2009-2014 Marc Fletcher, Matthew Dean
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
//  Non-GPL versions of this software can also be purchased. 
//  Please see <http://www.networkcomms.net> for details.

#if !NETFX_CORE

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace NetworkCommsDotNet.Tools
{
    /// <summary>
    /// Contains the information required to create self signed certificates
    /// </summary>
    public class CertificateDetails : IDisposable
    {
        /// <summary>
        /// Certificate "distinguished name". An example is "CN='My Certificate'; C='USA'". 
        /// Please see http://msdn.microsoft.com/en-us/library/aa377160 and http://en.wikipedia.org/wiki/X.509 for more information.
        /// </summary>
        public string X500 { get; private set; }

        /// <summary>
        /// Certificate validity start DateTime
        /// </summary>
        public DateTime StartTime { get; private set; }

        /// <summary>
        /// Certificate validity end DateTime
        /// </summary>
        public DateTime EndTime { get; private set; }

        /// <summary>
        /// Password for encrypting the key data
        /// </summary>
        public SecureString Password { get; private set; }

        static int _keyLength = 1024;
        /// <summary>
        /// The key length to be generated. Default is 1024. Minimum is 384. maximum is 16384.
        /// </summary>
        public int KeyLength
        {
            get
            {
                return _keyLength;
            }
            set
            {
                //Validate the provided key length
                if (value < 384) throw new ArgumentOutOfRangeException("Minimum key length is 384 Bits.", "KeyLength");
                if (value > 16384) throw new ArgumentOutOfRangeException("Maximum key length is 16384 Bits.", "KeyLength");

                _keyLength = value;
            }
        }

        /// <summary>
        /// Initialise certificate details.
        /// </summary>
        /// <param name="x500">Certificates "distinguished name". An example is "CN='My Certificate'; C='USA'". 
        /// Please see http://msdn.microsoft.com/en-us/library/aa377160 and http://en.wikipedia.org/wiki/X.509 for more information.</param>
        /// <param name="startTime">Certificate validity start DateTime</param>
        /// <param name="endTime">Certificate validity end DateTime</param>
        /// <param name="password">Password for encrypting the key data</param>
        public CertificateDetails(string x500, DateTime startTime, DateTime endTime, SecureString password)
        {
            if (x500 == null) X500 = "";
            else X500 = x500;

            this.StartTime = startTime;
            this.EndTime = endTime;
            this.Password = password;
        }

        /// <summary>
        /// Initialise certificate details.
        /// </summary>
        /// <param name="x500">Certificates "distinguished name". An example is "CN='My Certificate'; C='USA'". 
        /// Please see http://msdn.microsoft.com/en-us/library/aa377160 and http://en.wikipedia.org/wiki/X.509 for more information.</param>
        /// <param name="startTime">Certificate validity start DateTime</param>
        /// <param name="endTime">Certificate validity end DateTime</param>
        /// <param name="insecurePassword">Password for encrypting the key data</param>
        /// <returns>PFX file data</returns>
        public CertificateDetails(string x500, DateTime startTime, DateTime endTime, string insecurePassword)
        {
            if (x500 == null) X500 = "";
            else X500 = x500;

            this.StartTime = startTime;
            this.EndTime = endTime;

            this.Password = (SecureString)null;

            if (!string.IsNullOrEmpty(insecurePassword))
            {
                this.Password = new SecureString();
                foreach (char ch in insecurePassword)
                {
                    this.Password.AppendChar(ch);
                }

                this.Password.MakeReadOnly();
            }
        }

        /// <summary>
        /// Initialise certificate details.
        /// </summary>
        /// <param name="x500">Certificates "distinguished name". An example is "CN='My Certificate'; C='USA'". 
        /// Please see http://msdn.microsoft.com/en-us/library/aa377160 and http://en.wikipedia.org/wiki/X.509 for more information.</param>
        /// <param name="startTime">Certificate validity start DateTime</param>
        /// <param name="endTime">Certificate validity end DateTime</param>
        public CertificateDetails(string x500, DateTime startTime, DateTime endTime)
        {
            if (x500 == null) X500 = "";
            else X500 = x500;

            this.StartTime = startTime;
            this.EndTime = endTime;
            this.Password = (SecureString)null;
        }

        /// <summary>
        /// Dispose of the secure string password
        /// </summary>
        public void Dispose()
        {
            if (this.Password != null)
                this.Password.Dispose();
        }
    }

    /// <summary>
    /// Tools used in conjunction with SSL encrypted connections.
    /// </summary>
    public static class SSLTools
    {
        #region Create Self Signed Certificates
        /// <summary>
        /// Creates a self signed certificate which can be used for peer to peer authentication and 
        /// saves it to disk using provided certificateFileName. Initial implementation used with permission from http://blogs.msdn.com/b/dcook/archive/2008/11/25/creating-a-self-signed-certificate-in-c.aspx
        /// </summary>
        /// <param name="certificateDetails">The certificate details to use.</param>
        /// <param name="certificateFileName">The certificate file name, e.g. certFile.PFX</param>
        public static void CreateSelfSignedCertificatePFX(CertificateDetails certificateDetails, string certificateFileName)
        {
            if (certificateDetails == null) throw new ArgumentNullException("certificateDetails");
            if (certificateFileName == null) throw new ArgumentNullException("certificateFileName");

            if (Path.GetExtension(certificateFileName).ToLower() != ".pfx")
                throw new ArgumentException("Provided certificateFileName must end in extension PFX.");

            //Get the certificate
            byte[] pfxData = CreateSelfSignedCertificatePFX(certificateDetails);

            //Save the certificate
            using (BinaryWriter binWriter = new BinaryWriter(File.Open(certificateFileName, FileMode.Create)))
            {
                binWriter.Write(pfxData);
            }
        }

        /// <summary>
        /// Creates a self signed certificate which can be used for peer to peer authentication. 
        /// Initial implementation used with permission from http://blogs.msdn.com/b/dcook/archive/2008/11/25/creating-a-self-signed-certificate-in-c.aspx
        /// </summary>
        /// <param name="certificateDetails">The certificate details to use.</param>
        /// <returns>PFX file data</returns>
        public static byte[] CreateSelfSignedCertificatePFX(CertificateDetails certificateDetails)
        {
            if (certificateDetails == null) throw new ArgumentNullException("certificateDetails");

            byte[] pfxData;

            SystemTime startSystemTime = ToSystemTime(certificateDetails.StartTime);
            SystemTime endSystemTime = ToSystemTime(certificateDetails.EndTime);
            string containerName = Guid.NewGuid().ToString();

            GCHandle dataHandle = new GCHandle();
            IntPtr providerContext = IntPtr.Zero;
            IntPtr cryptKey = IntPtr.Zero;
            IntPtr certContext = IntPtr.Zero;
            IntPtr certStore = IntPtr.Zero;
            IntPtr storeCertContext = IntPtr.Zero;
            IntPtr passwordPtr = IntPtr.Zero;
            RuntimeHelpers.PrepareConstrainedRegions();
            try
            {
                Check(NativeMethods.CryptAcquireContextW(
                    out providerContext,
                    containerName,
                    null,
                    1, // PROV_RSA_FULL
                    8)); // CRYPT_NEWKEYSET

                Check(NativeMethods.CryptGenKey(
                    providerContext,
                    1, // AT_KEYEXCHANGE
                    1 | (certificateDetails.KeyLength << 16), // CRYPT_EXPORTABLE
                    out cryptKey));

                IntPtr errorStringPtr;
                int nameDataLength = 0;
                byte[] nameData;

                // errorStringPtr gets a pointer into the middle of the x500 string,
                // so x500 needs to be pinned until after we've copied the value
                // of errorStringPtr.
                dataHandle = GCHandle.Alloc(certificateDetails.X500, GCHandleType.Pinned);

                if (!NativeMethods.CertStrToNameW(
                    0x00010001, // X509_ASN_ENCODING | PKCS_7_ASN_ENCODING
                    dataHandle.AddrOfPinnedObject(),
                    3, // CERT_X500_NAME_STR = 3
                    IntPtr.Zero,
                    null,
                    ref nameDataLength,
                    out errorStringPtr))
                {
                    string error = Marshal.PtrToStringUni(errorStringPtr);
                    throw new ArgumentException(error);
                }

                nameData = new byte[nameDataLength];

                if (!NativeMethods.CertStrToNameW(
                    0x00010001, // X509_ASN_ENCODING | PKCS_7_ASN_ENCODING
                    dataHandle.AddrOfPinnedObject(),
                    3, // CERT_X500_NAME_STR = 3
                    IntPtr.Zero,
                    nameData,
                    ref nameDataLength,
                    out errorStringPtr))
                {
                    string error = Marshal.PtrToStringUni(errorStringPtr);
                    throw new ArgumentException(error);
                }

                dataHandle.Free();

                dataHandle = GCHandle.Alloc(nameData, GCHandleType.Pinned);
                CryptoApiBlob nameBlob = new CryptoApiBlob(
                    nameData.Length,
                    dataHandle.AddrOfPinnedObject());

                CryptKeyProviderInformation kpi = new CryptKeyProviderInformation();
                kpi.ContainerName = containerName;
                kpi.ProviderType = 1; // PROV_RSA_FULL
                kpi.KeySpec = 1; // AT_KEYEXCHANGE

                certContext = NativeMethods.CertCreateSelfSignCertificate(
                    providerContext,
                    ref nameBlob,
                    0,
                    ref kpi,
                    IntPtr.Zero, // default = SHA1RSA
                    ref startSystemTime,
                    ref endSystemTime,
                    IntPtr.Zero);
                Check(certContext != IntPtr.Zero);
                dataHandle.Free();

                certStore = NativeMethods.CertOpenStore(
                    "Memory", // sz_CERT_STORE_PROV_MEMORY
                    0,
                    IntPtr.Zero,
                    0x2000, // CERT_STORE_CREATE_NEW_FLAG
                    IntPtr.Zero);
                Check(certStore != IntPtr.Zero);

                Check(NativeMethods.CertAddCertificateContextToStore(
                    certStore,
                    certContext,
                    1, // CERT_STORE_ADD_NEW
                    out storeCertContext));

                NativeMethods.CertSetCertificateContextProperty(
                    storeCertContext,
                    2, // CERT_KEY_PROV_INFO_PROP_ID
                    0,
                    ref kpi);

                if (certificateDetails.Password != null)
                {
                    passwordPtr = Marshal.SecureStringToCoTaskMemUnicode(certificateDetails.Password);
                }

                CryptoApiBlob pfxBlob = new CryptoApiBlob();
                Check(NativeMethods.PFXExportCertStoreEx(
                    certStore,
                    ref pfxBlob,
                    passwordPtr,
                    IntPtr.Zero,
                    7)); // EXPORT_PRIVATE_KEYS | REPORT_NO_PRIVATE_KEY | REPORT_NOT_ABLE_TO_EXPORT_PRIVATE_KEY

                pfxData = new byte[pfxBlob.DataLength];
                dataHandle = GCHandle.Alloc(pfxData, GCHandleType.Pinned);
                pfxBlob.Data = dataHandle.AddrOfPinnedObject();
                Check(NativeMethods.PFXExportCertStoreEx(
                    certStore,
                    ref pfxBlob,
                    passwordPtr,
                    IntPtr.Zero,
                    7)); // EXPORT_PRIVATE_KEYS | REPORT_NO_PRIVATE_KEY | REPORT_NOT_ABLE_TO_EXPORT_PRIVATE_KEY
                dataHandle.Free();
            }
            finally
            {
                if (passwordPtr != IntPtr.Zero)
                {
                    Marshal.ZeroFreeCoTaskMemUnicode(passwordPtr);
                }

                if (dataHandle.IsAllocated)
                {
                    dataHandle.Free();
                }

                if (certContext != IntPtr.Zero)
                {
                    NativeMethods.CertFreeCertificateContext(certContext);
                }

                if (storeCertContext != IntPtr.Zero)
                {
                    NativeMethods.CertFreeCertificateContext(storeCertContext);
                }

                if (certStore != IntPtr.Zero)
                {
                    NativeMethods.CertCloseStore(certStore, 0);
                }

                if (cryptKey != IntPtr.Zero)
                {
                    NativeMethods.CryptDestroyKey(cryptKey);
                }

                if (providerContext != IntPtr.Zero)
                {
                    NativeMethods.CryptReleaseContext(providerContext, 0);
                    NativeMethods.CryptAcquireContextW(
                        out providerContext,
                        containerName,
                        null,
                        1, // PROV_RSA_FULL
                        0x10); // CRYPT_DELETEKEYSET
                }
            }

            return pfxData;
        }

        private static SystemTime ToSystemTime(DateTime dateTime)
        {
            long fileTime = dateTime.ToFileTime();
            SystemTime systemTime;
            Check(NativeMethods.FileTimeToSystemTime(ref fileTime, out systemTime));
            return systemTime;
        }

        private static void Check(bool nativeCallSucceeded)
        {
            if (!nativeCallSucceeded)
            {
                int error = Marshal.GetHRForLastWin32Error();
                Marshal.ThrowExceptionForHR(error);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemTime
        {
            public short Year;
            public short Month;
            public short DayOfWeek;
            public short Day;
            public short Hour;
            public short Minute;
            public short Second;
            public short Milliseconds;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CryptoApiBlob
        {
            public int DataLength;
            public IntPtr Data;

            public CryptoApiBlob(int dataLength, IntPtr data)
            {
                this.DataLength = dataLength;
                this.Data = data;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CryptKeyProviderInformation
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string ContainerName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string ProviderName;
            public int ProviderType;
            public int Flags;
            public int ProviderParameterCount;
            public IntPtr ProviderParameters; // PCRYPT_KEY_PROV_PARAM
            public int KeySpec;
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool FileTimeToSystemTime(
                [In] ref long fileTime,
                out SystemTime systemTime);

            [DllImport("AdvApi32.dll", SetLastError = true, ExactSpelling = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CryptAcquireContextW(
                out IntPtr providerContext,
                [MarshalAs(UnmanagedType.LPWStr)] string container,
                [MarshalAs(UnmanagedType.LPWStr)] string provider,
                int providerType,
                int flags);

            [DllImport("AdvApi32.dll", SetLastError = true, ExactSpelling = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CryptReleaseContext(
                IntPtr providerContext,
                int flags);

            /// <summary>
            /// See http://msdn.microsoft.com/en-us/library/windows/desktop/aa379941(v=vs.85).aspx for more information.
            /// </summary>
            /// <param name="providerContext"></param>
            /// <param name="algorithmId"></param>
            /// <param name="flags"></param>
            /// <param name="cryptKeyHandle"></param>
            /// <returns></returns>
            [DllImport("AdvApi32.dll", SetLastError = true, ExactSpelling = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CryptGenKey(
                IntPtr providerContext,
                int algorithmId,
                int flags,
                out IntPtr cryptKeyHandle);

            [DllImport("AdvApi32.dll", SetLastError = true, ExactSpelling = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CryptDestroyKey(
                IntPtr cryptKeyHandle);

            [DllImport("Crypt32.dll", SetLastError = true, ExactSpelling = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CertStrToNameW(
                int certificateEncodingType,
                IntPtr x500,
                int strType,
                IntPtr reserved,
                [MarshalAs(UnmanagedType.LPArray)] [Out] byte[] encoded,
                ref int encodedLength,
                out IntPtr errorString);

            [DllImport("Crypt32.dll", SetLastError = true, ExactSpelling = true)]
            public static extern IntPtr CertCreateSelfSignCertificate(
                IntPtr providerHandle,
                [In] ref CryptoApiBlob subjectIssuerBlob,
                int flags,
                [In] ref CryptKeyProviderInformation keyProviderInformation,
                IntPtr signatureAlgorithm,
                [In] ref SystemTime startTime,
                [In] ref SystemTime endTime,
                IntPtr extensions);

            [DllImport("Crypt32.dll", SetLastError = true, ExactSpelling = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CertFreeCertificateContext(
                IntPtr certificateContext);

            [DllImport("Crypt32.dll", SetLastError = true, ExactSpelling = true)]
            public static extern IntPtr CertOpenStore(
                [MarshalAs(UnmanagedType.LPStr)] string storeProvider,
                int messageAndCertificateEncodingType,
                IntPtr cryptProvHandle,
                int flags,
                IntPtr parameters);

            [DllImport("Crypt32.dll", SetLastError = true, ExactSpelling = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CertCloseStore(
                IntPtr certificateStoreHandle,
                int flags);

            [DllImport("Crypt32.dll", SetLastError = true, ExactSpelling = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CertAddCertificateContextToStore(
                IntPtr certificateStoreHandle,
                IntPtr certificateContext,
                int addDisposition,
                out IntPtr storeContextPtr);

            [DllImport("Crypt32.dll", SetLastError = true, ExactSpelling = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CertSetCertificateContextProperty(
                IntPtr certificateContext,
                int propertyId,
                int flags,
                [In] ref CryptKeyProviderInformation data);

            [DllImport("Crypt32.dll", SetLastError = true, ExactSpelling = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool PFXExportCertStoreEx(
                IntPtr certificateStoreHandle,
                ref CryptoApiBlob pfxBlob,
                IntPtr password,
                IntPtr reserved,
                int flags);
        }
        #endregion
    }
}
#endif