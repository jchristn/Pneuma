namespace Pneuma.Core.Crawling.Crawlers
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.IO.Compression;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Models;

    /// <summary>
    /// Reads sitemaps (and sitemap indexes, gzipped or not) and ingests the URLs they list. With
    /// <see cref="SitemapCrawlSettings.UseLastModified"/> each URL's <c>lastmod</c> is its version, so unchanged pages
    /// are not re-read. Every fetch goes through the fetch-safety policy.
    /// </summary>
    public class SitemapCrawler : ICrawler
    {
        #region Public-Members

        /// <inheritdoc />
        public CrawlPlanTypeEnum Type => CrawlPlanTypeEnum.Sitemap;

        /// <inheritdoc />
        public string DisplayName => "Sitemap";

        /// <inheritdoc />
        public string Description => "Ingests the URLs listed in sitemaps or sitemap indexes. A page is re-ingested when its lastmod changes.";

        /// <summary>Deepest nesting of sitemap indexes followed.</summary>
        public const int MaxIndexDepth = 3;

        /// <summary>Most sitemap files one run reads.</summary>
        public const int MaxSitemaps = 1000;

        #endregion

        #region Private-Members

        private readonly CrawlHttpClient _Http;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="http">Policy-enforcing HTTP client.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="http"/> is null.</exception>
        public SitemapCrawler(CrawlHttpClient http)
        {
            _Http = http ?? throw new ArgumentNullException(nameof(http));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<ConnectivityResult> TestAsync(CrawlPlan plan, CancellationToken token)
        {
            ConnectivityResult result = new ConnectivityResult();
            SitemapCrawlSettings? settings = plan?.Sitemap;
            if (settings == null || settings.SitemapUrls.Count == 0)
            {
                result.Add("settings", false, "Add at least one sitemap URL.");
                return result;
            }
            Uri? uri;
            if (!Uri.TryCreate(settings.SitemapUrls[0], UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                result.Add("settings", false, settings.SitemapUrls[0] + " is not an absolute http or https URL.");
                return result;
            }
            result.Add("settings", true, settings.SitemapUrls.Count + " sitemap URL(s).");
            if (!await ConnectivityProbe.ResolveAsync(result, uri.Host, token).ConfigureAwait(false)) return result;
            if (!await _Http.Policy.IsAllowedAsync(uri.AbsoluteUri, token).ConfigureAwait(false))
            {
                result.Add("policy", false, uri.Host + " is a private or internal address. An administrator can allow it in Ingestion.FetchSafety.AllowedPrivateHosts.");
                return result;
            }
            result.Add("policy", true, "The fetch-safety policy allows " + uri.Host + ".");
            if (!await ConnectivityProbe.ConnectAsync(result, uri.Host, uri.Port, 10000, token).ConfigureAwait(false)) return result;
            try
            {
                CrawlHttpResponse response = await _Http.GetAsync(uri.AbsoluteUri, null, token).ConfigureAwait(false);
                List<string> children = new List<string>();
                List<CrawledObject> urls = new List<CrawledObject>();
                Parse(Decompress(response.Bytes), children, urls, settings.UseLastModified);
                result.Add("root", true, "Read the sitemap: " + urls.Count + " URL(s) and " + children.Count + " nested sitemap(s).");
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                result.Add("root", false, "Reading " + uri.AbsoluteUri + " failed: " + e.Message);
            }
            return result;
        }

        /// <inheritdoc />
        /// <exception cref="InvalidDataException">Thrown when a sitemap is not valid sitemap XML.</exception>
        public async IAsyncEnumerable<CrawledObject> EnumerateAsync(CrawlPlan plan, [EnumeratorCancellation] CancellationToken token)
        {
            SitemapCrawlSettings settings = plan?.Sitemap ?? throw new ArgumentException("A Sitemap plan needs sitemap settings.");
            Queue<KeyValuePair<string, int>> queue = new Queue<KeyValuePair<string, int>>();
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (string url in settings.SitemapUrls) queue.Enqueue(new KeyValuePair<string, int>(url.Trim(), 0));

            while (queue.Count > 0 && visited.Count < MaxSitemaps)
            {
                token.ThrowIfCancellationRequested();
                KeyValuePair<string, int> next = queue.Dequeue();
                if (!visited.Add(next.Key)) continue;

                CrawlHttpResponse response = await _Http.GetAsync(next.Key, null, token).ConfigureAwait(false);
                Uri source = new Uri(next.Key);
                if (source.AbsolutePath.EndsWith("/robots.txt", StringComparison.OrdinalIgnoreCase))
                {
                    // A robots.txt URL is a pointer: its "Sitemap:" lines name the sitemaps to read.
                    foreach (string sitemap in RobotsSitemaps(Encoding.UTF8.GetString(response.Bytes)))
                        queue.Enqueue(new KeyValuePair<string, int>(sitemap, next.Value));
                    continue;
                }

                List<string> children = new List<string>();
                List<CrawledObject> urls = new List<CrawledObject>();
                Parse(Decompress(response.Bytes), children, urls, settings.UseLastModified);

                if (next.Value < MaxIndexDepth)
                {
                    foreach (string child in children) queue.Enqueue(new KeyValuePair<string, int>(child, next.Value + 1));
                }
                foreach (CrawledObject obj in urls)
                {
                    // The sitemap protocol only lets a sitemap list pages on its own host.
                    if (!String.Equals(new Uri(obj.Key).Host, source.Host, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!emitted.Add(obj.Key)) continue;
                    yield return obj;
                    if (emitted.Count >= settings.MaxUrls) yield break;
                }
            }
        }

        /// <inheritdoc />
        public async Task<ResolvedContent> OpenAsync(CrawlPlan plan, string key, CancellationToken token)
        {
            CrawlHttpResponse response = await _Http.GetAsync(key, null, token).ConfigureAwait(false);
            return new ResolvedContent { Bytes = response.Bytes };
        }

        /// <summary>
        /// Parse sitemap XML: <c>sitemapindex</c> entries go to <paramref name="children"/>, <c>urlset</c> entries to
        /// <paramref name="urls"/>. DTDs are refused.
        /// </summary>
        /// <param name="xml">The XML bytes.</param>
        /// <param name="children">Receives nested sitemap URLs.</param>
        /// <param name="urls">Receives page entries.</param>
        /// <param name="useLastModified">Use lastmod as the version token.</param>
        /// <exception cref="InvalidDataException">Thrown when the document is not a sitemap.</exception>
        public static void Parse(byte[] xml, List<string> children, List<CrawledObject> urls, bool useLastModified)
        {
            XmlReaderSettings readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };
            string? root = null;
            try
            {
                using (MemoryStream stream = new MemoryStream(xml))
                using (XmlReader reader = XmlReader.Create(stream, readerSettings))
                {
                    string? loc = null;
                    string? lastmod = null;
                    reader.MoveToContent();
                    while (!reader.EOF)
                    {
                        if (reader.NodeType == XmlNodeType.Element)
                        {
                            if (root == null) root = reader.LocalName;
                            // Reading element content leaves the reader on the following node, so do not Read again.
                            if (reader.LocalName == "loc") { loc = reader.ReadElementContentAsString().Trim(); continue; }
                            if (reader.LocalName == "lastmod") { lastmod = reader.ReadElementContentAsString().Trim(); continue; }
                            if (reader.LocalName == "url" || reader.LocalName == "sitemap") { loc = null; lastmod = null; }
                        }
                        else if (reader.NodeType == XmlNodeType.EndElement && !String.IsNullOrEmpty(loc))
                        {
                            if (reader.LocalName == "sitemap") children.Add(loc!);
                            else if (reader.LocalName == "url")
                            {
                                string? key = CrawlUrl.Normalize(loc, null);
                                if (key != null) urls.Add(ToObject(key, lastmod, useLastModified));
                            }
                        }
                        reader.Read();
                    }
                }
            }
            catch (XmlException e)
            {
                throw new InvalidDataException("The sitemap is not valid XML: " + e.Message, e);
            }
            if (root != "urlset" && root != "sitemapindex") throw new InvalidDataException("The document is not a sitemap (root element '" + (root ?? "none") + "').");
        }

        #endregion

        #region Private-Methods

        private static CrawledObject ToObject(string key, string? lastmod, bool useLastModified)
        {
            DateTime parsed;
            DateTime? modified = !String.IsNullOrEmpty(lastmod) && DateTime.TryParse(lastmod, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed)
                ? parsed
                : (DateTime?)null;
            return new CrawledObject
            {
                Key = key,
                Uri = key,
                ModifiedUtc = modified,
                VersionToken = useLastModified && modified != null ? "lastmod:" + modified.Value.ToString("o", CultureInfo.InvariantCulture) : null
            };
        }

        private static List<string> RobotsSitemaps(string robots)
        {
            List<string> result = new List<string>();
            foreach (string raw in robots.Split('\n'))
            {
                string line = raw.Trim();
                if (!line.StartsWith("sitemap:", StringComparison.OrdinalIgnoreCase)) continue;
                string url = line.Substring("sitemap:".Length).Trim();
                if (Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)) result.Add(url);
            }
            return result;
        }

        private static byte[] Decompress(byte[] bytes)
        {
            if (bytes.Length < 2 || bytes[0] != 0x1f || bytes[1] != 0x8b) return bytes;
            using (MemoryStream input = new MemoryStream(bytes))
            using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream())
            {
                gzip.CopyTo(output);
                return output.ToArray();
            }
        }

        #endregion
    }
}
