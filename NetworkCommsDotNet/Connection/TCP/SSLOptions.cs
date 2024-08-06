﻿// 
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
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace NetworkCommsDotNet.Connections.TCP
{
    /// <summary>
    /// Contains SSL configuration options
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
        /// If true self signed certificates may be used successfully. CAUTION: Allowing self signed certificates makes it 
        /// significantly easier for a remote peer to impersonate someone.
        /// </summary>
        public bool AllowSelfSignedCertificate { get; private set; }

        /// <summary>
        /// If true the client must set the correct certificate in its SSLOptions. If false
        /// all the client requires to successfully connect is the certificate name.
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
        /// requires either a copy of the provided certificate or the certificate name to successfully connect.
        /// </summary>
        /// <param name="certificate">The certificate</param>
        /// <param name="allowSelfSignedCertificates">If true self signed certificates may be used successfully. CAUTION: Allowing self signed certificates makes it 
        /// significantly easier for a remote peer to impersonate someone.</param>
        public SSLOptions(X509Certificate certificate, bool allowSelfSignedCertificates)
        {
            SSLEnabled = true;
            Certificate = certificate;
            CertificateName = certificate.Issuer.Replace("CN=","");
            AllowSelfSignedCertificate = allowSelfSignedCertificates;
        }

        /// <summary>
        /// Initialise a new instance of SSLOptions which enables SSL. If requireMutualAuthentication is true, and 
        /// this SSLOptions is used server side, any client must have a copy of the certificate to successfully connect.
        /// </summary>
        /// <param name="certificate">The certificate</param>
        /// <param name="allowSelfSignedCertificates">If true self signed certificates may be used successfully. CAUTION: Allowing self signed certificates makes it 
        /// significantly easier for a remote peer to impersonate someone.</param>
        /// <param name="requireMutualAuthentication">True if any client must also have a copy of the server certificate</param>
        public SSLOptions(X509Certificate certificate, bool allowSelfSignedCertificates, bool requireMutualAuthentication)
        {
            SSLEnabled = true;
            RequireMutualAuthentication = requireMutualAuthentication;
            Certificate = certificate;
            CertificateName = certificate.Issuer.Replace("CN=", "");
            AllowSelfSignedCertificate = allowSelfSignedCertificates;
        }

        /// <summary>
        /// Initialise a new instance of SSLOptions which enables SSL. Can be successfully used to
        /// connect to a server with a matching certificateName. Server must not require MutualAuthentication.
        /// </summary>
        /// <param name="certificateName">The server certificate name</param>
        /// <param name="allowSelfSignedCertificates">If true self signed certificates may be used successfully. CAUTION: Allowing self signed certificates makes it 
        /// significantly easier for a remote peer to impersonate someone.</param>
        public SSLOptions(string certificateName, bool allowSelfSignedCertificates)
        {
            SSLEnabled = true;
            Certificate = null;
            CertificateName = certificateName;
            AllowSelfSignedCertificate = allowSelfSignedCertificates;
        }
    }
}
