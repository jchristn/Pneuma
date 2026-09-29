namespace Pneuma.Core.Ingestion.Configuration
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Limits on what the content fetchers and crawlers may reach and how much they may download. By default only public
    /// addresses are fetched: loopback, private (RFC 1918), link-local (including cloud metadata), carrier-grade NAT,
    /// unique-local IPv6, and multicast addresses are refused unless listed in <see cref="AllowedPrivateHosts"/>.
    /// </summary>
    public class FetchSafetySettings
    {
        #region Public-Members

        /// <summary>
        /// Refuse fetches that resolve to a non-public address. Default true. Turn off only for deployments where every
        /// user is trusted with the server's network position; prefer <see cref="AllowedPrivateHosts"/>.
        /// </summary>
        public bool BlockPrivateAddresses { get; set; } = true;

        /// <summary>
        /// Hosts that may be fetched even when they resolve to a private address. Each entry is a host name
        /// (<c>wiki.corp.local</c>), a wildcard (<c>*.corp.local</c>), an IP address (<c>10.1.2.3</c>), or a CIDR range
        /// (<c>10.0.0.0/8</c>). Empty by default. Never null.
        /// </summary>
        public List<string> AllowedPrivateHosts
        {
            get { return _AllowedPrivateHosts; }
            set { _AllowedPrivateHosts = value ?? new List<string>(); }
        }

        /// <summary>
        /// Maximum bytes downloaded for one fetch. The limit is enforced while streaming, so an oversized response is
        /// abandoned rather than read. Default 104857600 (100 MB); clamped to [1024, 10737418240].
        /// </summary>
        public long MaxDownloadBytes
        {
            get { return _MaxDownloadBytes; }
            set { _MaxDownloadBytes = Math.Clamp(value, 1024L, 10737418240L); }
        }

        /// <summary>
        /// Accept TLS certificates that fail validation (self-signed or internal PKI). Default false. Every fetch made
        /// with this on is exposed to interception, so enable it only for internal sources.
        /// </summary>
        public bool AllowInvalidCertificates { get; set; } = false;

        /// <summary>
        /// Maximum concurrent requests to one host across all fetchers and crawlers, so a crawl or a burst of links
        /// cannot overwhelm a site. Default 2; clamped to [1, 64].
        /// </summary>
        public int MaxRequestsPerHost
        {
            get { return _MaxRequestsPerHost; }
            set { _MaxRequestsPerHost = Math.Clamp(value, 1, 64); }
        }

        #endregion

        #region Private-Members

        private List<string> _AllowedPrivateHosts = new List<string>();
        private long _MaxDownloadBytes = 104857600L;
        private int _MaxRequestsPerHost = 2;

        #endregion
    }
}
