using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Contains SSL configuration
    /// </summary>
    public class SSLOptions
    {
        /// <summary>
        /// True if SSL has been enabled
        /// </summary>
        public bool SSLEnabled { get; private set; }

        /// <summary>
        /// The certificate name
        /// </summary>
        public string CertificateName { get; private set; }

        /// <summary>
        /// A suitable certificate
        /// </summary>
        public X509Certificate Certificate { get; private set; }

        /// <summary>
        /// If true the client must set the correct certificate in its SSLOptions. If false
        /// all the client requires to succesfully connect is the certificate name.
        /// </summary>
        public bool RequireMutualAuthentication { get; private set; }

        /// <summary>
        /// True once the SSL handshake has been authenticated
        /// </summary>
        public bool Authenticated { get; internal set; }

        /// <summary>
        /// Initialise a new empty instance of SSLOptions, which disables SSL.
        /// </summary>
        public SSLOptions()
        {
            SSLEnabled = false;
            Certificate = null;
        }

        /// <summary>
        /// Initialise a new instance of SSLOptions which enables SSL. If this SSLOptions is used server side any client
        /// requires either a copy of the provided certificate or the certificate name to succesfully connect.
        /// </summary>
        /// <param name="certificate">The certificate</param>
        public SSLOptions(X509Certificate certificate)
        {
            SSLEnabled = true;
            Certificate = certificate;
            CertificateName = certificate.Issuer.Replace("CN=","");
        }

        /// <summary>
        /// Initialise a new instance of SSLOptions which enables SSL. If requireMutualAuthentication is true, and 
        /// this SSLOptions is used server side, any client must have a copy of the certificate to succesfully connect.
        /// </summary>
        /// <param name="certificate">The certificate</param>
        /// <param name="requireMutualAuthentication">True if any client must also have a copy of the server certificate</param>
        public SSLOptions(X509Certificate certificate, bool requireMutualAuthentication)
        {
            SSLEnabled = true;
            RequireMutualAuthentication = requireMutualAuthentication;
            Certificate = certificate;
            CertificateName = certificate.Issuer.Replace("CN=", "");
        }

        /// <summary>
        /// Initialise a new instance of SSLOptions which enables SSL. Can be succesfully used to
        /// connect to a server with a matching certificateName. Server must not require MutualAuthentication.
        /// </summary>
        /// <param name="certificateName">The server certificate name</param>
        public SSLOptions(string certificateName)
        {
            SSLEnabled = true;
            Certificate = null;
            CertificateName = certificateName;
        }
    }
}
