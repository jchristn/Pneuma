namespace Test.Benchmark.Servers
{
    using System;
    using System.Collections.Concurrent;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Datasets;
    using WatsonWebserver;
    using WatsonWebserver.Core;

    /// <summary>
    /// Serves benchmark documents over HTTP so Pneuma's URL-only ingestion can fetch them. Each document is
    /// published at <c>/{dataset}/{corpus}/{documentId}.{ext}</c>, with a content type matching its format, and
    /// the document id is recovered from a link's URL when results are scored.
    /// </summary>
    public class CorpusServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Base URL the server listens on.
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// Documents fetched so far.
        /// </summary>
        public long Requests
        {
            get
            {
                return Interlocked.Read(ref _Requests);
            }
        }

        #endregion

        #region Private-Members

        private readonly Webserver _Server;
        private readonly ConcurrentDictionary<string, ServedDocument> _Documents = new ConcurrentDictionary<string, ServedDocument>(StringComparer.Ordinal);
        private long _Requests = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and start listening on the loopback interface.
        /// </summary>
        /// <param name="port">Port.</param>
        public CorpusServer(int port)
        {
            BaseUrl = "http://127.0.0.1:" + port;
            WebserverSettings settings = new WebserverSettings("127.0.0.1", port, false);
            _Server = new Webserver(settings, DefaultRouteAsync);
            _Server.Start();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Publish a document and return its URL.
        /// </summary>
        /// <param name="dataset">Dataset name.</param>
        /// <param name="corpus">Corpus id.</param>
        /// <param name="document">The document.</param>
        /// <returns>The document URL.</returns>
        public string Publish(string dataset, string corpus, BenchmarkDocument document)
        {
            string path = PathFor(dataset, corpus, document);
            _Documents[path] = Render(document);
            return BaseUrl + path;
        }

        /// <summary>
        /// The URL path a document is served at.
        /// </summary>
        /// <param name="dataset">Dataset name.</param>
        /// <param name="corpus">Corpus id.</param>
        /// <param name="document">The document.</param>
        /// <returns>The path, starting with a slash.</returns>
        public static string PathFor(string dataset, string corpus, BenchmarkDocument document)
        {
            return "/" + Uri.EscapeDataString(dataset) + "/" + Uri.EscapeDataString(corpus) + "/" + Uri.EscapeDataString(document.Id) + "." + Extension(document.Format);
        }

        /// <summary>
        /// Recover a document id from a served URL, or null when the URL is not one of ours.
        /// </summary>
        /// <param name="url">Link URL.</param>
        /// <returns>The document id or null.</returns>
        public static string? DocumentIdFromUrl(string? url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            int slash = url.LastIndexOf('/');
            int dot = url.LastIndexOf('.');
            if (slash < 0 || dot <= slash) return null;
            return Uri.UnescapeDataString(url.Substring(slash + 1, dot - slash - 1));
        }

        /// <summary>
        /// The text a document is served as (title and summary are prepended to md and txt bodies).
        /// </summary>
        /// <param name="document">The document.</param>
        /// <returns>The served text.</returns>
        public static string RenderText(BenchmarkDocument document)
        {
            return Render(document).Text;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private static string Extension(string? format)
        {
            string value = (format ?? "md").ToLowerInvariant();
            if (value == "html" || value == "htm") return "html";
            if (value == "txt" || value == "text") return "txt";
            return "md";
        }

        private static ServedDocument Render(BenchmarkDocument document)
        {
            string extension = Extension(document.Format);
            if (extension == "html") return new ServedDocument("text/html; charset=utf-8", document.Body);

            StringBuilder sb = new StringBuilder();
            bool markdown = extension == "md";
            if (!string.IsNullOrWhiteSpace(document.Title)) sb.Append(markdown ? "# " : string.Empty).Append(document.Title!.Trim()).Append("\n\n");
            if (!string.IsNullOrWhiteSpace(document.Summary)) sb.Append(document.Summary!.Trim()).Append("\n\n");
            sb.Append(document.Body ?? string.Empty);
            return new ServedDocument(markdown ? "text/markdown; charset=utf-8" : "text/plain; charset=utf-8", sb.ToString());
        }

        private async Task DefaultRouteAsync(HttpContextBase ctx)
        {
            string path = ctx.Request.Url.RawWithoutQuery ?? "/";
            if (_Documents.TryGetValue(path, out ServedDocument? document))
            {
                Interlocked.Increment(ref _Requests);
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = document.ContentType;
                await ctx.Response.Send(document.Text).ConfigureAwait(false);
                return;
            }

            ctx.Response.StatusCode = 404;
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.Send("not found").ConfigureAwait(false);
        }

        /// <summary>
        /// Dispose resources.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                try
                {
                    _Server.Stop();
                }
                catch (ObjectDisposedException)
                {
                }

                _Server.Dispose();
            }

            _Disposed = true;
        }

        #endregion
    }
}
