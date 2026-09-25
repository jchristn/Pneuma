namespace Test.Benchmark.Reference
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Client;
    using Test.Benchmark.Datasets;
    using Test.Benchmark.Servers;

    /// <summary>
    /// The reference arm: a plain RAG retriever built inside the harness over the same documents Pneuma ingests,
    /// with no Pneuma code involved. BM25 over whole documents, dense retrieval over fixed word windows using the
    /// same embedding model Pneuma is configured with, and RRF of the two. The gap between Pneuma and this arm is
    /// what Pneuma's pipeline (extraction, cell chunking, summary chunks, fusion, MMR) adds or costs.
    /// </summary>
    public class ReferenceArm
    {
        #region Public-Members

        /// <summary>
        /// Modes this arm answers.
        /// </summary>
        public static readonly string[] Modes = new string[] { "ref-bm25", "ref-dense", "ref-hybrid" };

        /// <summary>
        /// Chunks in the dense index.
        /// </summary>
        public int DenseChunks
        {
            get
            {
                return _Dense?.Count ?? 0;
            }
        }

        /// <summary>
        /// True when the dense leg is available.
        /// </summary>
        public bool HasDense
        {
            get
            {
                return _Dense != null;
            }
        }

        #endregion

        #region Private-Members

        private readonly Bm25Index _Bm25;
        private readonly DenseIndex? _Dense;
        private readonly DirectModelClient? _Embedder;
        private readonly EmbeddingCache? _Cache;
        private const int _RrfK = 60;
        private const int _Depth = 100;

        #endregion

        #region Constructors-and-Factories

        private ReferenceArm(Bm25Index bm25, DenseIndex? dense, DirectModelClient? embedder, EmbeddingCache? cache)
        {
            _Bm25 = bm25;
            _Dense = dense;
            _Embedder = embedder;
            _Cache = cache;
        }

        /// <summary>
        /// Build the arm for a corpus.
        /// </summary>
        /// <param name="corpus">Corpus.</param>
        /// <param name="embedder">Embedding client, or null for BM25 only.</param>
        /// <param name="cache">Embedding cache (required with an embedder).</param>
        /// <param name="k1">BM25 k1.</param>
        /// <param name="b">BM25 b.</param>
        /// <param name="wordsPerChunk">Dense chunk size in words.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The arm.</returns>
        public static async Task<ReferenceArm> BuildAsync(BenchmarkCorpus corpus, DirectModelClient? embedder, EmbeddingCache? cache, double k1, double b, int wordsPerChunk, CancellationToken token)
        {
            Bm25Index bm25 = new Bm25Index(k1, b);
            List<string> chunkDocIds = new List<string>();
            List<string> chunkTexts = new List<string>();
            foreach (BenchmarkDocument document in corpus.Documents)
            {
                string text = PlainText(document);
                bm25.Add(document.Id, text);
                foreach (string chunk in TextAnalyzer.WordChunks(text, wordsPerChunk))
                {
                    chunkDocIds.Add(document.Id);
                    chunkTexts.Add(chunk);
                }
            }

            DenseIndex? dense = null;
            if (embedder != null && cache != null)
            {
                dense = new DenseIndex();
                List<float[]> vectors = await cache.EmbedAsync(embedder, chunkTexts, token).ConfigureAwait(false);
                for (int i = 0; i < vectors.Count; i++) dense.Add(chunkDocIds[i], vectors[i]);
            }

            return new ReferenceArm(bm25, dense, embedder, cache);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Rank documents for a query in one of <see cref="Modes"/>.
        /// </summary>
        /// <param name="query">Query text.</param>
        /// <param name="mode">ref-bm25, ref-dense, or ref-hybrid.</param>
        /// <param name="k">Results.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Document ids, best first (empty for a dense mode without an embedder).</returns>
        public async Task<List<string>> SearchAsync(string query, string mode, int k, CancellationToken token)
        {
            if (mode == "ref-bm25") return _Bm25.Search(query, k);
            if (_Dense == null || _Embedder == null || _Cache == null) return new List<string>();

            List<float[]> vectors = await _Cache.EmbedAsync(_Embedder, new List<string> { query }, token).ConfigureAwait(false);
            List<string> dense = _Dense.Search(vectors[0], mode == "ref-dense" ? k : _Depth);
            if (mode == "ref-dense") return dense;

            List<string> lexical = _Bm25.Search(query, _Depth);
            Dictionary<string, double> fused = new Dictionary<string, double>(StringComparer.Ordinal);
            for (int i = 0; i < lexical.Count; i++) fused[lexical[i]] = (fused.TryGetValue(lexical[i], out double a) ? a : 0.0) + 1.0 / (_RrfK + i + 1);
            for (int i = 0; i < dense.Count; i++) fused[dense[i]] = (fused.TryGetValue(dense[i], out double c) ? c : 0.0) + 1.0 / (_RrfK + i + 1);
            return fused.OrderByDescending(f => f.Value).ThenBy(f => f.Key, StringComparer.Ordinal).Take(k).Select(f => f.Key).ToList();
        }

        /// <summary>
        /// The plain text of a document as served to Pneuma (HTML stripped).
        /// </summary>
        /// <param name="document">Document.</param>
        /// <returns>Text.</returns>
        public static string PlainText(BenchmarkDocument document)
        {
            string served = CorpusServer.RenderText(document);
            bool html = string.Equals(document.Format, "html", StringComparison.OrdinalIgnoreCase) || string.Equals(document.Format, "htm", StringComparison.OrdinalIgnoreCase);
            return html ? TextAnalyzer.StripHtml(served) : served;
        }

        #endregion
    }
}
