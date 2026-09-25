namespace Test.Shared.Support
{
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;

    /// <summary>
    /// A minimal Ollama-format embedding endpoint on the loopback interface that returns the same fixed vector for
    /// every input, so API tests can exercise the real embedding path (model runner, PolyPrompt, vector search)
    /// with deterministic vectors.
    /// </summary>
    public class StubEmbeddingEndpoint : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Base URL of the endpoint.
        /// </summary>
        public string BaseUrl { get; }

        #endregion

        #region Private-Members

        private readonly Webserver _Server;
        private readonly float[] _Vector;
        private int _FailuresRemaining = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start the endpoint on a free loopback port.
        /// </summary>
        /// <param name="vector">The vector returned for every input.</param>
        /// <param name="failFirst">Requests answered with 429 "at capacity" before the endpoint starts succeeding.</param>
        /// <exception cref="ArgumentNullException">Thrown when the vector is null.</exception>
        public StubEmbeddingEndpoint(float[] vector, int failFirst = 0)
        {
            _FailuresRemaining = Math.Max(0, failFirst);
            _Vector = vector ?? throw new ArgumentNullException(nameof(vector));
            int port = FreePort();
            BaseUrl = "http://127.0.0.1:" + port;
            _Server = new Webserver(new WebserverSettings("127.0.0.1", port, false), RouteAsync);
            _Server.Start();
        }

        #endregion

        #region Public-Methods

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
            if (System.Threading.Interlocked.Decrement(ref _FailuresRemaining) >= 0)
            {
                ctx.Response.StatusCode = 429;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.Send("{\"Error\":\"All eligible endpoints are at capacity.\",\"StatusCode\":429}").ConfigureAwait(false);
                return;
            }

            string body = ctx.Request.DataAsString ?? String.Empty;
            int count = 1;
            using (JsonDocument doc = String.IsNullOrWhiteSpace(body) ? JsonDocument.Parse("{}") : JsonDocument.Parse(body))
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("input", out JsonElement input) && input.ValueKind == JsonValueKind.Array)
                {
                    count = Math.Max(1, input.GetArrayLength());
                }
            }

            string vector = "[" + String.Join(",", Array.ConvertAll(_Vector, v => v.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]";
            StringBuilder json = new StringBuilder();
            string path = ctx.Request.Url.RawWithoutQuery ?? "/";
            if (path.EndsWith("/api/embeddings", StringComparison.Ordinal))
            {
                json.Append("{\"embedding\":").Append(vector).Append('}');
            }
            else
            {
                json.Append("{\"model\":\"stub\",\"embeddings\":[");
                for (int i = 0; i < count; i++) json.Append(i > 0 ? "," : String.Empty).Append(vector);
                json.Append("]}");
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send(json.ToString()).ConfigureAwait(false);
        }

        private static int FreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
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
