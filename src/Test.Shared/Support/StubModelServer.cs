namespace Test.Shared.Support
{
    using System;
    using System.Globalization;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;

    /// <summary>
    /// A programmable Ollama-format model server on the loopback interface for ingestion and retry tests. It answers
    /// embeddings (<c>/api/embed</c>, <c>/api/embeddings</c>) with a fixed vector and chat or generate calls
    /// (<c>/api/chat</c>, <c>/api/generate</c>) with a fixed text, and can be told to fail the first N requests with a
    /// given status (and optional <c>Retry-After</c>), to reject inputs longer than a character limit with a
    /// context-length error, and to hold each request for a delay so concurrency can be observed. Thread-safe.
    /// </summary>
    public class StubModelServer : IDisposable
    {
        #region Public-Members

        /// <summary>Base URL of the server, for example <c>http://127.0.0.1:50123</c>.</summary>
        public string BaseUrl { get; }

        /// <summary>Text returned by chat and generate calls. Default: an empty subgraph JSON object.</summary>
        public string ChatText { get; set; } = "{\"nodes\":[],\"edges\":[]}";

        /// <summary>Status code used for the scripted failures. Default 429.</summary>
        public int FailureStatus { get; set; } = 429;

        /// <summary>Value of the <c>Retry-After</c> header sent with scripted failures, in seconds; 0 sends none.</summary>
        public int RetryAfterSeconds { get; set; } = 0;

        /// <summary>
        /// When greater than zero, an embedding input longer than this many characters is rejected with a 400
        /// context-length error. Default 0 (no limit).
        /// </summary>
        public int MaxInputCharacters { get; set; } = 0;

        /// <summary>Delay applied to every successful request, in milliseconds. Default 0.</summary>
        public int DelayMs { get; set; } = 0;

        /// <summary>Total requests received.</summary>
        public int RequestCount => Volatile.Read(ref _RequestCount);

        /// <summary>Embedding requests received (including failed ones).</summary>
        public int EmbedRequestCount => Volatile.Read(ref _EmbedRequestCount);

        /// <summary>Chat and generate requests received (including failed ones).</summary>
        public int ChatRequestCount => Volatile.Read(ref _ChatRequestCount);

        /// <summary>The largest number of requests that were in flight at the same time.</summary>
        public int MaxConcurrent => Volatile.Read(ref _MaxConcurrent);

        /// <summary>The body of the most recent embedding request, or empty.</summary>
        public string LastEmbedBody
        {
            get { lock (_Lock) { return _LastEmbedBody; } }
        }

        /// <summary>The body of the most recent chat or generate request, or empty.</summary>
        public string LastChatBody
        {
            get { lock (_Lock) { return _LastChatBody; } }
        }

        #endregion

        #region Private-Members

        private readonly Webserver _Server;
        private readonly float[] _Vector;
        private readonly object _Lock = new object();
        private int _FailuresRemaining = 0;
        private int _RequestCount = 0;
        private int _EmbedRequestCount = 0;
        private int _ChatRequestCount = 0;
        private int _InFlight = 0;
        private int _MaxConcurrent = 0;
        private string _LastEmbedBody = String.Empty;
        private string _LastChatBody = String.Empty;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Start the server on a free loopback port.</summary>
        /// <param name="vector">The vector returned for every embedding input; null uses an 8-dimensional unit vector.</param>
        public StubModelServer(float[]? vector = null)
        {
            _Vector = vector ?? new float[] { 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
            int port = FreePort();
            BaseUrl = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);
            _Server = new Webserver(new WebserverSettings("127.0.0.1", port, false), RouteAsync);
            _Server.Start();
        }

        #endregion

        #region Public-Methods

        /// <summary>Make the next <paramref name="count"/> requests fail with <see cref="FailureStatus"/>.</summary>
        /// <param name="count">Number of requests to fail. Minimum 0.</param>
        public void FailNext(int count)
        {
            Interlocked.Exchange(ref _FailuresRemaining, Math.Max(0, count));
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
            Interlocked.Increment(ref _RequestCount);
            string path = ctx.Request.Url.RawWithoutQuery ?? "/";
            bool isEmbed = path.EndsWith("/api/embed", StringComparison.Ordinal) || path.EndsWith("/api/embeddings", StringComparison.Ordinal);
            if (isEmbed) Interlocked.Increment(ref _EmbedRequestCount);
            else Interlocked.Increment(ref _ChatRequestCount);

            int inFlight = Interlocked.Increment(ref _InFlight);
            UpdateMax(inFlight);
            try
            {
                if (Interlocked.Decrement(ref _FailuresRemaining) >= 0)
                {
                    ctx.Response.StatusCode = FailureStatus;
                    ctx.Response.ContentType = "application/json";
                    if (RetryAfterSeconds > 0) ctx.Response.Headers.Add("Retry-After", RetryAfterSeconds.ToString(CultureInfo.InvariantCulture));
                    await ctx.Response.Send("{\"error\":\"stub failure " + FailureStatus.ToString(CultureInfo.InvariantCulture) + ": at capacity\"}").ConfigureAwait(false);
                    return;
                }

                Interlocked.Exchange(ref _FailuresRemaining, 0);
                if (DelayMs > 0) await Task.Delay(DelayMs).ConfigureAwait(false);

                string body = ctx.Request.DataAsString ?? String.Empty;
                if (isEmbed)
                {
                    await EmbedAsync(ctx, path, body).ConfigureAwait(false);
                    return;
                }

                lock (_Lock) { _LastChatBody = body; }
                string text = JsonEncodedText.Encode(ChatText).ToString();
                string json = path.EndsWith("/api/generate", StringComparison.Ordinal)
                    ? "{\"model\":\"stub\",\"response\":\"" + text + "\",\"done\":true}"
                    : "{\"model\":\"stub\",\"message\":{\"role\":\"assistant\",\"content\":\"" + text + "\"},\"done\":true}";
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.Send(json).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _InFlight);
            }
        }

        private async Task EmbedAsync(HttpContextBase ctx, string path, string body)
        {
            lock (_Lock) { _LastEmbedBody = body; }

            int count = 1;
            int longest = 0;
            using (JsonDocument doc = String.IsNullOrWhiteSpace(body) ? JsonDocument.Parse("{}") : JsonDocument.Parse(body))
            {
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("input", out JsonElement input))
                {
                    if (input.ValueKind == JsonValueKind.Array)
                    {
                        count = Math.Max(1, input.GetArrayLength());
                        foreach (JsonElement item in input.EnumerateArray()) longest = Math.Max(longest, (item.GetString() ?? String.Empty).Length);
                    }
                    else if (input.ValueKind == JsonValueKind.String)
                    {
                        longest = (input.GetString() ?? String.Empty).Length;
                    }
                }
                else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("prompt", out JsonElement prompt) && prompt.ValueKind == JsonValueKind.String)
                {
                    longest = (prompt.GetString() ?? String.Empty).Length;
                }
            }

            if (MaxInputCharacters > 0 && longest > MaxInputCharacters)
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.Send("{\"error\":\"the input length exceeds the context length\"}").ConfigureAwait(false);
                return;
            }

            string vector = "[" + String.Join(",", Array.ConvertAll(_Vector, v => v.ToString(CultureInfo.InvariantCulture))) + "]";
            StringBuilder json = new StringBuilder();
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

        private void UpdateMax(int value)
        {
            int current;
            do
            {
                current = Volatile.Read(ref _MaxConcurrent);
                if (value <= current) return;
            }
            while (Interlocked.CompareExchange(ref _MaxConcurrent, value, current) != current);
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
