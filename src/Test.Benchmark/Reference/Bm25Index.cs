namespace Test.Benchmark.Reference
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// An in-memory BM25 index over whole documents (Lucene's BM25 formulation, with its
    /// <c>log(1 + (N - df + 0.5) / (df + 0.5))</c> idf).
    /// </summary>
    public class Bm25Index
    {
        #region Public-Members

        /// <summary>
        /// Term-frequency saturation.
        /// </summary>
        public double K1 { get; }

        /// <summary>
        /// Length normalization.
        /// </summary>
        public double B { get; }

        /// <summary>
        /// Indexed documents.
        /// </summary>
        public int Count
        {
            get
            {
                return _DocIds.Count;
            }
        }

        #endregion

        #region Private-Members

        private readonly List<string> _DocIds = new List<string>();
        private readonly List<int> _Lengths = new List<int>();
        private readonly Dictionary<string, List<int[]>> _Postings = new Dictionary<string, List<int[]>>(StringComparer.Ordinal);
        private long _TotalLength = 0;
        private double _AverageLength = 0.0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="k1">k1, clamped to 0..3 (Pyserini default 0.9, Elasticsearch default 1.2).</param>
        /// <param name="b">b, clamped to 0..1 (Pyserini default 0.4, Elasticsearch default 0.75).</param>
        public Bm25Index(double k1, double b)
        {
            K1 = Math.Clamp(k1, 0.0, 3.0);
            B = Math.Clamp(b, 0.0, 1.0);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a document.
        /// </summary>
        /// <param name="id">Document id.</param>
        /// <param name="text">Document text.</param>
        public void Add(string id, string text)
        {
            List<string> terms = TextAnalyzer.Analyze(text);
            int index = _DocIds.Count;
            _DocIds.Add(id);
            _Lengths.Add(terms.Count);
            foreach (IGrouping<string, string> group in terms.GroupBy(t => t, StringComparer.Ordinal))
            {
                if (!_Postings.TryGetValue(group.Key, out List<int[]>? postings))
                {
                    postings = new List<int[]>();
                    _Postings[group.Key] = postings;
                }

                postings.Add(new int[] { index, group.Count() });
            }

            _TotalLength += terms.Count;
            _AverageLength = (double)_TotalLength / _Lengths.Count;
        }

        /// <summary>
        /// Rank documents for a query.
        /// </summary>
        /// <param name="query">Query text.</param>
        /// <param name="k">Results.</param>
        /// <returns>Document ids, best first.</returns>
        public List<string> Search(string query, int k)
        {
            Dictionary<int, double> scores = new Dictionary<int, double>();
            int n = _DocIds.Count;
            foreach (string term in TextAnalyzer.Analyze(query))
            {
                if (!_Postings.TryGetValue(term, out List<int[]>? postings)) continue;
                double idf = Math.Log(1.0 + (n - postings.Count + 0.5) / (postings.Count + 0.5));
                foreach (int[] posting in postings)
                {
                    double tf = posting[1];
                    double norm = K1 * (1.0 - B + B * _Lengths[posting[0]] / Math.Max(1e-9, _AverageLength));
                    double score = idf * tf * (K1 + 1.0) / (tf + norm);
                    scores[posting[0]] = (scores.TryGetValue(posting[0], out double existing) ? existing : 0.0) + score;
                }
            }

            return scores.OrderByDescending(s => s.Value).ThenBy(s => _DocIds[s.Key], StringComparer.Ordinal).Take(k).Select(s => _DocIds[s.Key]).ToList();
        }

        #endregion
    }
}
