namespace Test.Shared.Support
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;

    /// <summary>
    /// A programmable web site on the loopback interface for fetch-safety and crawler tests. Each path is mapped to a
    /// <see cref="StubPage"/> (status, content type, body, headers such as <c>Location</c> or <c>ETag</c>); unmapped
    /// paths return 404. Honors <c>If-None-Match</c> against a page's ETag with 304, and counts requests per path.
    /// Thread-safe.
    /// </summary>
    public class StubWebSite : IDisposable
    {
        #region Public-Members

        /// <summary>Base URL, for example <c>http://127.0.0.1:50123</c>.</summary>
        public string BaseUrl { get; }

        /// <summary>The loopback port the site listens on.</summary>
        public int Port { get; }

        /// <summary>When set, every request must carry this exact Authorization header or gets a 401.</summary>
        public string? RequiredAuthorization { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly Webserver _Server;
        private readonly ConcurrentDictionary<string, StubPage> _Pages = new ConcurrentDictionary<string, StubPage>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _Hits = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Start the site on a free loopback port.</summary>
        public StubWebSite()
        {
            Port = FreePort();
            BaseUrl = "http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture);
            _Server = new Webserver(new WebserverSettings("127.0.0.1", Port, false), RouteAsync);
            _Server.Start();
        }

        #endregion

        #region Public-Methods

        /// <summary>Serve a page at a path (replacing any page already there).</summary>
        /// <param name="path">Absolute path, for example <c>/docs/a.html</c>.</param>
        /// <param name="page">The page.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public void Set(string path, StubPage page)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            _Pages[path] = page ?? throw new ArgumentNullException(nameof(page));
        }

        /// <summary>Serve an HTML page at a path.</summary>
        /// <param name="path">Absolute path.</param>
        /// <param name="html">The HTML.</param>
        /// <param name="etag">Optional ETag.</param>
        public void Html(string path, string html, string? etag = null)
        {
            StubPage page = new StubPage { ContentType = "text/html; charset=utf-8", Body = Encoding.UTF8.GetBytes(html ?? String.Empty) };
            if (!String.IsNullOrEmpty(etag)) page.Headers["ETag"] = etag!;
            Set(path, page);
        }

        /// <summary>Serve a redirect at a path.</summary>
        /// <param name="path">Absolute path.</param>
        /// <param name="location">The redirect target (absolute or relative).</param>
        public void Redirect(string path, string location)
        {
            StubPage page = new StubPage { Status = 302, ContentType = "text/plain", Body = Array.Empty<byte>() };
            page.Headers["Location"] = location;
            Set(path, page);
        }

        /// <summary>Stop serving a path (it returns 404 afterwards).</summary>
        /// <param name="path">Absolute path.</param>
        public void Remove(string path)
        {
            _Pages.TryRemove(path, out StubPage? _);
        }

        /// <summary>Number of requests received for a path.</summary>
        /// <param name="path">Absolute path.</param>
        /// <returns>The request count.</returns>
        public int Hits(string path)
        {
            return _Hits.TryGetValue(path, out int count) ? count : 0;
        }

        /// <summary>Absolute URL of a path on this site.</summary>
        /// <param name="path">Absolute path.</param>
        /// <returns>The URL.</returns>
        public string Url(string path)
        {
            return BaseUrl + path;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private async Task RouteAsync(HttpContextBase ctx)
        {
            string path = ctx.Request.Url.RawWithoutQuery ?? "/";
            _Hits.AddOrUpdate(path, 1, (key, existing) => existing + 1);

            if (RequiredAuthorization != null && !String.Equals(ctx.Request.Headers.Get("Authorization"), RequiredAuthorization, StringComparison.Ordinal))
            {
                ctx.Response.StatusCode = 401;
                ctx.Response.ContentType = "text/plain";
                await ctx.Response.Send("unauthorized").ConfigureAwait(false);
                return;
            }

            if (!_Pages.TryGetValue(path, out StubPage? page))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.ContentType = "text/plain";
                await ctx.Response.Send("not found").ConfigureAwait(false);
                return;
            }

            string? ifNoneMatch = ctx.Request.Headers.Get("If-None-Match");
            if (!String.IsNullOrEmpty(ifNoneMatch) && page.Headers.TryGetValue("ETag", out string? etag) && String.Equals(ifNoneMatch, etag, StringComparison.Ordinal))
            {
                ctx.Response.StatusCode = 304;
                ctx.Response.Headers.Add("ETag", etag);
                await ctx.Response.Send().ConfigureAwait(false);
                return;
            }

            ctx.Response.StatusCode = page.Status;
            ctx.Response.ContentType = page.ContentType;
            foreach (KeyValuePair<string, string> header in page.Headers) ctx.Response.Headers.Add(header.Key, header.Value);
            await ctx.Response.Send(page.Body).ConfigureAwait(false);
        }

        private static int FreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <summary>Dispose resources.</summary>
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
