namespace Pneuma.Core.Ingestion.Stages
{
    using System;
    using Pneuma.Core.Caching;
    using Pneuma.Core.Database;
    using Pneuma.Core.Ingestion.Pipeline;
    using Pneuma.Core.Integrations;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Security;
    using Pneuma.Core.Storage;
    using SyslogLogging;

    /// <summary>
    /// The shared collaborators every ingestion stage draws from, bundled so each stage takes a single
    /// constructor argument instead of repeating the full dependency list. Constructed once per worker and
    /// handed to every stage; individual stages read only the members they need, which keeps them independently
    /// testable (construct with fakes and exercise one stage's <see cref="IStage.ExecuteAsync"/>).
    /// </summary>
    public class StageDependencies
    {
        #region Public-Members

        /// <summary>Database driver (job/link/subject/prompt reads and writes).</summary>
        public DatabaseDriverBase Db { get; }

        /// <summary>Semantic processor (chunk / embed / summarize).</summary>
        public ISemanticProcessor Processor { get; }

        /// <summary>Cipher used to decrypt a model runner's stored key.</summary>
        public Aes256Cipher Cipher { get; }

        /// <summary>Per-tenant graph repository factory.</summary>
        public IGraphRepositoryFactory GraphFactory { get; }

        /// <summary>Vector repository (RecallDB) that stores chunk documents.</summary>
        public IVectorRepository Vectors { get; }

        /// <summary>Per-stage S3 artifact store.</summary>
        public IArtifactStore Artifacts { get; }

        /// <summary>Source content fetcher.</summary>
        public IContentFetcher Fetcher { get; }

        /// <summary>DocumentAtom client (type detection + cell extraction).</summary>
        public IAtomizer DocumentAtom { get; }

        /// <summary>Blob store for the raw fetched source.</summary>
        public IBlobStore Blobs { get; }

        /// <summary>Journal for stage events and best-effort artifact writes.</summary>
        public IngestionJournal Journal { get; }

        /// <summary>Process-wide bounded embedding cache.</summary>
        public EmbeddingCache EmbeddingCache { get; }

        /// <summary>Runtime concurrency manager (effective per-subject tuning).</summary>
        public ConcurrencyManager Concurrency { get; }

        /// <summary>LLM classifier that maps cells to a candidate subgraph.</summary>
        public PolyPromptClassifier Classifier { get; }

        /// <summary>Logging module.</summary>
        public LoggingModule Logging { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Bundle the shared stage collaborators.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="processor">Semantic processor.</param>
        /// <param name="cipher">Cipher for model-runner keys.</param>
        /// <param name="graphFactory">Per-tenant graph repository factory.</param>
        /// <param name="vectors">Vector repository.</param>
        /// <param name="artifacts">Artifact store.</param>
        /// <param name="fetcher">Content fetcher.</param>
        /// <param name="documentAtom">DocumentAtom client.</param>
        /// <param name="blobs">Blob store.</param>
        /// <param name="journal">Ingestion journal.</param>
        /// <param name="embeddingCache">Embedding cache.</param>
        /// <param name="concurrency">Concurrency manager.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public StageDependencies(
            DatabaseDriverBase db,
            ISemanticProcessor processor,
            Aes256Cipher cipher,
            IGraphRepositoryFactory graphFactory,
            IVectorRepository vectors,
            IArtifactStore artifacts,
            IContentFetcher fetcher,
            IAtomizer documentAtom,
            IBlobStore blobs,
            IngestionJournal journal,
            EmbeddingCache embeddingCache,
            ConcurrencyManager concurrency,
            LoggingModule logging)
        {
            Db = db ?? throw new ArgumentNullException(nameof(db));
            Processor = processor ?? throw new ArgumentNullException(nameof(processor));
            Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
            GraphFactory = graphFactory ?? throw new ArgumentNullException(nameof(graphFactory));
            Vectors = vectors ?? throw new ArgumentNullException(nameof(vectors));
            Artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
            Fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
            DocumentAtom = documentAtom ?? throw new ArgumentNullException(nameof(documentAtom));
            Blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
            Journal = journal ?? throw new ArgumentNullException(nameof(journal));
            EmbeddingCache = embeddingCache ?? throw new ArgumentNullException(nameof(embeddingCache));
            Concurrency = concurrency ?? throw new ArgumentNullException(nameof(concurrency));
            Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            Classifier = new PolyPromptClassifier(logging);
        }

        #endregion
    }
}
