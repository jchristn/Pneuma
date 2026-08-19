namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// In-memory <see cref="IVectorRepository"/> for tests: stores per-node embeddings and tags and
    /// answers searches by cosine similarity with an optional exact-match tag filter.
    /// </summary>
    public sealed class FakeVectorRepository : IVectorRepository
    {
        #region Private-Members

        private readonly Dictionary<string, float[]> _Vectors = new Dictionary<string, float[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, string>> _Tags = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        #endregion

        #region Public-Members

        /// <summary>Number of stored vectors.</summary>
        public int Count
        {
            get { return _Vectors.Count; }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task UpsertVectorAsync(string nodeId, IReadOnlyList<float> embedding, IReadOnlyDictionary<string, string>? tags = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(nodeId)) throw new ArgumentNullException(nameof(nodeId));
            if (embedding == null) throw new ArgumentNullException(nameof(embedding));

            _Vectors[nodeId] = embedding.ToArray();
            _Tags[nodeId] = tags == null ? new Dictionary<string, string>() : new Dictionary<string, string>((IDictionary<string, string>)tags);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<List<VectorSearchHit>> SearchAsync(IReadOnlyList<float> embedding, int topK, double minimumScore, IReadOnlyDictionary<string, string>? tags, CancellationToken token = default)
        {
            if (embedding == null) throw new ArgumentNullException(nameof(embedding));
            int limit = Math.Max(1, topK);

            List<VectorSearchHit> hits = new List<VectorSearchHit>();
            float[] query = embedding.ToArray();

            foreach (KeyValuePair<string, float[]> entry in _Vectors)
            {
                if (!TagsMatch(entry.Key, tags)) continue;

                double score = CosineSimilarity(query, entry.Value);
                if (score < minimumScore) continue;

                hits.Add(new VectorSearchHit { NodeId = entry.Key, Score = score });
            }

            List<VectorSearchHit> ordered = hits.OrderByDescending(h => h.Score).Take(limit).ToList();
            return Task.FromResult(ordered);
        }

        #endregion

        #region Private-Methods

        private bool TagsMatch(string nodeId, IReadOnlyDictionary<string, string>? tags)
        {
            if (tags == null || tags.Count == 0) return true;
            if (!_Tags.TryGetValue(nodeId, out Dictionary<string, string>? stored)) return false;

            foreach (KeyValuePair<string, string> tag in tags)
            {
                if (!stored.TryGetValue(tag.Key, out string? value) || !String.Equals(value, tag.Value, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static double CosineSimilarity(float[] a, float[] b)
        {
            int length = Math.Min(a.Length, b.Length);
            double dot = 0.0;
            double magnitudeA = 0.0;
            double magnitudeB = 0.0;

            for (int i = 0; i < length; i++)
            {
                dot += a[i] * b[i];
                magnitudeA += a[i] * a[i];
                magnitudeB += b[i] * b[i];
            }

            if (magnitudeA <= 0.0 || magnitudeB <= 0.0) return 0.0;
            return dot / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
        }

        #endregion
    }
}
