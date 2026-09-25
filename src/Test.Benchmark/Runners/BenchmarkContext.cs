namespace Test.Benchmark.Runners
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Client;
    using Test.Benchmark.Servers;

    /// <summary>
    /// Everything a benchmark command shares: arguments, the logged-in Pneuma client, the model settings, the
    /// captured environment, the output directory, and the lazily-started in-process servers.
    /// </summary>
    public class BenchmarkContext : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Parsed arguments.
        /// </summary>
        public BenchmarkArguments Arguments { get; }

        /// <summary>
        /// Pneuma client (logged in).
        /// </summary>
        public PneumaClient Client { get; }

        /// <summary>
        /// Embedding model Pneuma subjects use (and the reference arm calls directly).
        /// </summary>
        public ModelSettings Embedding { get; }

        /// <summary>
        /// Completion model used for classification, summaries, and answers.
        /// </summary>
        public ModelSettings Inference { get; }

        /// <summary>
        /// full or lean.
        /// </summary>
        public string IngestProfile { get; }

        /// <summary>
        /// Captured environment.
        /// </summary>
        public BenchmarkEnvironment Environment { get; }

        /// <summary>
        /// Report output directory.
        /// </summary>
        public string ResultsDirectory { get; }

        /// <summary>
        /// Cache directory (embedding cache, downloaded data).
        /// </summary>
        public string CacheDirectory { get; }

        #endregion

        #region Private-Members

        private readonly object _ServerLock = new object();
        private CorpusServer? _Corpus = null;
        private StubModelServer? _Stub = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        private BenchmarkContext(BenchmarkArguments arguments, PneumaClient client, ModelSettings embedding, ModelSettings inference, string profile, BenchmarkEnvironment environment, string results, string cache)
        {
            Arguments = arguments;
            Client = client;
            Embedding = embedding;
            Inference = inference;
            IngestProfile = profile;
            Environment = environment;
            ResultsDirectory = results;
            CacheDirectory = cache;
        }

        /// <summary>
        /// Build the context: read settings, wait for Pneuma, and log in.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The context.</returns>
        /// <exception cref="InvalidOperationException">Thrown when Pneuma is unreachable.</exception>
        public static async Task<BenchmarkContext> CreateAsync(BenchmarkArguments arguments, CancellationToken token)
        {
            string url = arguments.Get("url", "http://127.0.0.1:28080");
            string email = arguments.Get("email", "admin@pneuma");
            string password = arguments.Get("password", "password");
            int timeoutSeconds = arguments.GetInt("timeout-seconds", 900);
            PneumaClient client = new PneumaClient(url, email, password, TimeSpan.FromSeconds(timeoutSeconds));
            if (!await client.WaitForHealthAsync(TimeSpan.FromSeconds(arguments.GetInt("wait-seconds", 60)), token).ConfigureAwait(false))
            {
                client.Dispose();
                throw new InvalidOperationException("Pneuma is not answering at " + url + " (start the bench server first; see benchmarks/README.md).");
            }

            await client.LoginAsync(token).ConfigureAwait(false);

            ModelSettings embedding = ModelSettings.FromArguments(arguments, "embedding", "nomic-embed-text", null);
            ModelSettings inference = ModelSettings.FromArguments(arguments, "inference", "gemma3:4b", embedding);
            string profile = arguments.Get("profile", "full").ToLowerInvariant();
            bool stub = arguments.GetFlag("stub");
            if (stub)
            {
                // --stub: embeddings come from the in-process stub (feature hashing, fixed latency) and ingestion is
                // lean, so the run measures Pneuma and RecallDB rather than the models.
                embedding = new ModelSettings { Format = "ollama", Url = "http://127.0.0.1:" + arguments.GetInt("stub-port", 28434), Model = "stub" };
                profile = "lean";
            }
            if (profile != "full" && profile != "lean") throw new ArgumentException("--profile must be full or lean.");

            BenchmarkEnvironment environment = BenchmarkEnvironment.Capture(url);
            environment.Embedding = embedding.Description;
            environment.Inference = profile == "lean" ? "stub (lean ingest); answers: " + inference.Description : inference.Description;
            environment.IngestProfile = profile;

            string results = Path.GetFullPath(arguments.Get("results", Path.Combine("benchmarks", "results")));
            string cache = Path.GetFullPath(arguments.Get("cache", Path.Combine("benchmarks", "data", "cache")));
            Directory.CreateDirectory(results);
            Directory.CreateDirectory(cache);
            BenchmarkContext context = new BenchmarkContext(arguments, client, embedding, inference, profile, environment, results, cache);
            if (stub) context.Stub(arguments.GetInt("dimensionality", 768));
            return context;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The corpus server, started on first use.
        /// </summary>
        /// <returns>The server.</returns>
        public CorpusServer Corpus()
        {
            lock (_ServerLock)
            {
                if (_Corpus == null) _Corpus = new CorpusServer(Arguments.GetInt("corpus-port", 28090));
                return _Corpus;
            }
        }

        /// <summary>
        /// The stub model server, started on first use.
        /// </summary>
        /// <param name="dimensionality">Embedding dimensionality.</param>
        /// <returns>The server.</returns>
        public StubModelServer Stub(int dimensionality)
        {
            lock (_ServerLock)
            {
                if (_Stub == null) _Stub = new StubModelServer(Arguments.GetInt("stub-port", 28434), dimensionality, Arguments.GetInt("stub-latency-ms", 0));
                return _Stub;
            }
        }

        /// <summary>
        /// A direct client for the embedding model (reference arm).
        /// </summary>
        /// <returns>The client.</returns>
        public DirectModelClient EmbeddingClient()
        {
            return new DirectModelClient(Embedding.Format, Embedding.Url, Embedding.Model, Embedding.ApiKey, TimeSpan.FromMinutes(10));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Dispose resources.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                _Corpus?.Dispose();
                _Stub?.Dispose();
                Client.Dispose();
            }

            _Disposed = true;
        }

        #endregion
    }
}
