namespace Pneuma.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Ingestion.Configuration;

    /// <summary>
    /// Decides which URLs the fetchers and crawlers may reach. It allows only <c>http</c> and <c>https</c>, refuses
    /// hosts that resolve to non-public addresses (unless allow-listed), and checks the address a socket actually
    /// connects to, so a redirect or a DNS answer that changes between check and connect (DNS rebinding) cannot reach an
    /// internal service. It also builds the HTTP handler that enforces this and reads response bodies under a size cap.
    /// Thread-safe.
    /// </summary>
    public class FetchSafetyPolicy
    {
        #region Public-Members

        /// <summary>The settings the policy enforces.</summary>
        public FetchSafetySettings Settings { get; }

        /// <summary>Per-host concurrency limiter shared by every fetcher and crawler that uses this policy.</summary>
        public HostRequestLimiter HostLimiter { get; }

        #endregion

        #region Private-Members

        private readonly Func<string, CancellationToken, Task<IPAddress[]>> _Resolver;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the policy.</summary>
        /// <param name="settings">Fetch-safety settings; null uses the defaults.</param>
        /// <param name="resolver">DNS resolver; null uses the system resolver. Tests supply one to simulate DNS answers.</param>
        public FetchSafetyPolicy(FetchSafetySettings? settings = null, Func<string, CancellationToken, Task<IPAddress[]>>? resolver = null)
        {
            Settings = settings ?? new FetchSafetySettings();
            HostLimiter = new HostRequestLimiter(Settings.MaxRequestsPerHost);
            _Resolver = resolver ?? ((host, token) => Dns.GetHostAddressesAsync(host, token));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Check a URL's shape without resolving it: it must be absolute, use <c>http</c> or <c>https</c>, and, when its
        /// host is an IP literal or <c>localhost</c>, that address must be allowed. Used at submission so an obviously
        /// unsafe URL is rejected with a 400 instead of becoming a job that fails.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <returns>Null when the URL is acceptable; otherwise a short reason code ("invalid-url", "scheme", "private-address").</returns>
        public string? CheckUrlShape(string? url)
        {
            if (String.IsNullOrWhiteSpace(url)) return "invalid-url";
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri)) return "invalid-url";
            // On Linux and macOS a rooted path such as "/docs/a" parses as an absolute file URI; it is a relative URL.
            if (uri.IsFile && !url.Trim().StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return "invalid-url";
            if (!String.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return "scheme";
            if (String.IsNullOrEmpty(uri.Host)) return "invalid-url";

            if (Settings.BlockPrivateAddresses && !IsHostAllowListed(uri.IdnHost))
            {
                if (String.Equals(uri.IdnHost, "localhost", StringComparison.OrdinalIgnoreCase)) return "private-address";
                if (IPAddress.TryParse(uri.IdnHost.Trim('[', ']'), out IPAddress? literal) && !IsPublicAddress(literal)) return "private-address";
            }

            return null;
        }

        /// <summary>Throw when a URL fails <see cref="CheckUrlShape"/>.</summary>
        /// <param name="url">The URL.</param>
        /// <exception cref="FetchBlockedException">Thrown when the URL is refused.</exception>
        public void EnsureUrlShape(string? url)
        {
            string? reason = CheckUrlShape(url);
            if (reason != null) throw new FetchBlockedException(url ?? String.Empty, reason, DescribeReason(reason, url));
        }

        /// <summary>
        /// Resolve a URL's host and throw unless one of its addresses may be fetched. Used before handing a URL to a
        /// browser, which cannot enforce the check at connect time.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="FetchBlockedException">Thrown when the URL or every address it resolves to is refused.</exception>
        public async Task EnsureAllowedAsync(string url, CancellationToken token)
        {
            EnsureUrlShape(url);
            Uri uri = new Uri(url.Trim(), UriKind.Absolute);
            await SelectAddressAsync(uri.IdnHost, url, token).ConfigureAwait(false);
        }

        /// <summary>True when the URL may be fetched (its shape is acceptable and one of its addresses is allowed).</summary>
        /// <param name="url">The URL.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when allowed.</returns>
        public async Task<bool> IsAllowedAsync(string url, CancellationToken token)
        {
            try
            {
                await EnsureAllowedAsync(url, token).ConfigureAwait(false);
                return true;
            }
            catch (FetchBlockedException)
            {
                return false;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        /// <summary>
        /// True when an address is publicly routable: not loopback, unspecified, private (RFC 1918), carrier-grade NAT,
        /// link-local (which includes the 169.254.169.254 metadata address), unique-local or site-local IPv6,
        /// multicast, or broadcast. IPv4-mapped IPv6 addresses are judged by their IPv4 form.
        /// </summary>
        /// <param name="address">The address.</param>
        /// <returns>True for a public address.</returns>
        public static bool IsPublicAddress(IPAddress address)
        {
            if (address == null) return false;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (IPAddress.IsLoopback(address)) return false;

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] b = address.GetAddressBytes();
                if (b[0] == 0) return false;
                if (b[0] == 10) return false;
                if (b[0] == 127) return false;
                if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;
                if (b[0] == 169 && b[1] == 254) return false;
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;
                if (b[0] == 192 && b[1] == 168) return false;
                if (b[0] == 192 && b[1] == 0 && b[2] == 0) return false;
                if (b[0] >= 224) return false;
                return true;
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Any)) return false;
                if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return false;
                byte[] b = address.GetAddressBytes();
                if ((b[0] & 0xFE) == 0xFC) return false;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Build an HTTP handler that validates the address of every connection it opens (including redirects), and
        /// validates certificates unless <see cref="FetchSafetySettings.AllowInvalidCertificates"/> is on.
        /// </summary>
        /// <returns>The handler; the caller owns and disposes it (usually through an <see cref="HttpClient"/>).</returns>
        public SocketsHttpHandler CreateHandler()
        {
            SocketsHttpHandler handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                ConnectCallback = ConnectAsync
            };

            if (Settings.AllowInvalidCertificates)
            {
                handler.SslOptions.RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true;
            }

            return handler;
        }

        /// <summary>
        /// Read a response body under <see cref="FetchSafetySettings.MaxDownloadBytes"/>. A declared Content-Length over
        /// the limit is refused before reading; otherwise the read stops as soon as the limit is passed.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <param name="url">The URL, for the error message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The body bytes.</returns>
        /// <exception cref="ContentTooLargeException">Thrown when the body exceeds the limit.</exception>
        public async Task<byte[]> ReadBodyAsync(HttpResponseMessage response, string url, CancellationToken token)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            long limit = Settings.MaxDownloadBytes;
            long? declared = response.Content.Headers.ContentLength;
            if (declared != null && declared.Value > limit) throw TooLarge(url, limit);

            using (Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[81920];
                while (true)
                {
                    int read = await stream.ReadAsync(chunk, 0, chunk.Length, token).ConfigureAwait(false);
                    if (read <= 0) break;
                    if (buffer.Length + read > limit) throw TooLarge(url, limit);
                    buffer.Write(chunk, 0, read);
                }

                return buffer.ToArray();
            }
        }

        /// <summary>Throw when a byte count exceeds <see cref="FetchSafetySettings.MaxDownloadBytes"/>.</summary>
        /// <param name="length">The byte count.</param>
        /// <param name="url">The URL, for the error message.</param>
        /// <exception cref="ContentTooLargeException">Thrown when the count exceeds the limit.</exception>
        public void EnsureWithinLimit(long length, string url)
        {
            if (length > Settings.MaxDownloadBytes) throw TooLarge(url, Settings.MaxDownloadBytes);
        }

        #endregion

        #region Private-Methods

        private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token)
        {
            string host = context.DnsEndPoint.Host;
            IPAddress address = await SelectAddressAsync(host, context.InitialRequestMessage.RequestUri?.ToString() ?? host, token).ConfigureAwait(false);

            Socket socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private async Task<IPAddress> SelectAddressAsync(string host, string url, CancellationToken token)
        {
            string bare = host.Trim('[', ']');
            IPAddress[] addresses = IPAddress.TryParse(bare, out IPAddress? literal)
                ? new[] { literal }
                : await _Resolver(bare, token).ConfigureAwait(false);
            if (addresses == null || addresses.Length == 0) throw new FetchBlockedException(url, "unresolvable", "The host '" + host + "' did not resolve to any address.");

            if (!Settings.BlockPrivateAddresses || IsHostAllowListed(bare)) return addresses[0];

            IPAddress? allowed = addresses.FirstOrDefault(a => IsPublicAddress(a) || IsAddressAllowListed(a));
            if (allowed == null)
            {
                throw new FetchBlockedException(url, "private-address",
                    "Fetching " + url + " was blocked: '" + host + "' resolves to a private or internal address (" + addresses[0] +
                    "). Add the host to Ingestion.FetchSafety.AllowedPrivateHosts if it should be ingested.");
            }

            return allowed;
        }

        private bool IsHostAllowListed(string host)
        {
            if (String.IsNullOrEmpty(host)) return false;
            string h = host.Trim('[', ']').TrimEnd('.');
            foreach (string raw in Settings.AllowedPrivateHosts)
            {
                if (String.IsNullOrWhiteSpace(raw)) continue;
                string entry = raw.Trim();
                if (String.Equals(entry, h, StringComparison.OrdinalIgnoreCase)) return true;
                if (entry.StartsWith("*.", StringComparison.Ordinal) && h.EndsWith(entry.Substring(1), StringComparison.OrdinalIgnoreCase)) return true;
                if (IPAddress.TryParse(h, out IPAddress? address) && EntryMatches(entry, address)) return true;
            }

            return false;
        }

        private bool IsAddressAllowListed(IPAddress address)
        {
            foreach (string raw in Settings.AllowedPrivateHosts)
            {
                if (String.IsNullOrWhiteSpace(raw)) continue;
                if (EntryMatches(raw.Trim(), address)) return true;
            }

            return false;
        }

        private static bool EntryMatches(string entry, IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            int slash = entry.IndexOf('/');
            if (slash < 0) return IPAddress.TryParse(entry, out IPAddress? single) && single.Equals(address);

            if (!IPAddress.TryParse(entry.Substring(0, slash), out IPAddress? network)) return false;
            if (!Int32.TryParse(entry.Substring(slash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int prefix)) return false;
            if (network.AddressFamily != address.AddressFamily) return false;

            byte[] a = address.GetAddressBytes();
            byte[] n = network.GetAddressBytes();
            int full = prefix / 8;
            int rest = prefix % 8;
            if (prefix < 0 || prefix > a.Length * 8) return false;
            for (int i = 0; i < full; i++) if (a[i] != n[i]) return false;
            if (rest == 0) return true;
            int mask = 0xFF << (8 - rest) & 0xFF;
            return (a[full] & mask) == (n[full] & mask);
        }

        private static ContentTooLargeException TooLarge(string url, long limit)
        {
            return new ContentTooLargeException(limit, "The content at " + url + " exceeds the " + limit.ToString(CultureInfo.InvariantCulture) +
                "-byte download limit (Ingestion.FetchSafety.MaxDownloadBytes).");
        }

        private static string DescribeReason(string reason, string? url)
        {
            switch (reason)
            {
                case "scheme": return "Only http and https URLs can be ingested: " + url;
                case "private-address": return "The URL " + url + " points at a private or internal address. Add the host to Ingestion.FetchSafety.AllowedPrivateHosts if it should be ingested.";
                default: return "The URL is not a valid absolute http or https URL: " + url;
            }
        }

        #endregion
    }
}
