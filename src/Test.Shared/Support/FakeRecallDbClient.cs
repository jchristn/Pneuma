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
    /// In-memory RecallDB fake implementing the collection store, vector repository, and inverted index.
    /// Everything is scoped to a tenant (the Pneuma tenant id is reused as the RecallDB tenant id): tenants
    /// are ensured explicitly, collections and documents live under a tenant, and search/store/delete only
    /// see the given tenant's data. Vector search ranks by real cosine similarity; full-text search does
    /// case-insensitive substring matching; both honor an exact-match tag AND filter.
    /// </summary>
    public class FakeRecallDbClient : ICollectionStore, IVectorRepository, IInvertedIndex
    {
        private readonly object _Lock = new object();
        private readonly HashSet<string> _Tenants = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, RecallCollection> _Collections = new Dictionary<string, RecallCollection>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _CollectionTenant = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<Stored> _Docs = new List<Stored>();
        private int _CollectionSeq = 0;

        /// <summary>Total stored chunk documents across all tenants and collections.</summary>
        public int DocumentCount { get { lock (_Lock) { return _Docs.Count; } } }

        /// <summary>Whether a tenant has been ensured/created in the fake store.</summary>
        public bool TenantExists(string tenantId) { lock (_Lock) { return _Tenants.Contains(tenantId); } }

        private sealed class Stored
        {
            public string TenantId = string.Empty;
            public string CollectionId = string.Empty;
            public string DocumentKey = string.Empty;
            public string DocumentId = string.Empty;
            public int Position;
            public string Content = string.Empty;
            public float[] Embedding = Array.Empty<float>();
            public Dictionary<string, string> Tags = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        // ---- ICollectionStore ----

        /// <inheritdoc />
        public Task EnsureTenantAsync(string tenantId, string tenantName, CancellationToken token = default)
        {
            lock (_Lock) { if (!string.IsNullOrEmpty(tenantId)) _Tenants.Add(tenantId); }
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<List<RecallCollection>> ListCollectionsAsync(string tenantId, CancellationToken token = default)
        {
            lock (_Lock)
            {
                List<RecallCollection> list = new List<RecallCollection>();
                foreach (RecallCollection c in _Collections.Values)
                {
                    if (_CollectionTenant.TryGetValue(c.Id, out string? t) && t == tenantId) list.Add(c);
                }
                return Task.FromResult(list);
            }
        }

        /// <inheritdoc />
        public Task<RecallCollection?> ReadCollectionAsync(string tenantId, string id, CancellationToken token = default)
        {
            lock (_Lock)
            {
                if (_Collections.TryGetValue(id, out RecallCollection? c) && _CollectionTenant.TryGetValue(id, out string? t) && t == tenantId) return Task.FromResult<RecallCollection?>(c);
                return Task.FromResult<RecallCollection?>(null);
            }
        }

        /// <inheritdoc />
        public Task<RecallCollection> CreateCollectionAsync(string tenantId, RecallCollection collection, CancellationToken token = default)
        {
            lock (_Lock)
            {
                if (string.IsNullOrEmpty(collection.Id)) collection.Id = "col_test_" + (++_CollectionSeq);
                _Collections[collection.Id] = collection;
                _CollectionTenant[collection.Id] = tenantId;
                return Task.FromResult(collection);
            }
        }

        /// <inheritdoc />
        public Task<bool> DeleteCollectionAsync(string tenantId, string id, CancellationToken token = default)
        {
            lock (_Lock)
            {
                if (!_CollectionTenant.TryGetValue(id, out string? t) || t != tenantId) return Task.FromResult(false);
                _Docs.RemoveAll(d => d.TenantId == tenantId && d.CollectionId == id);
                _CollectionTenant.Remove(id);
                return Task.FromResult(_Collections.Remove(id));
            }
        }

        /// <inheritdoc />
        public Task<bool> CollectionExistsAsync(string tenantId, string id, CancellationToken token = default)
        {
            lock (_Lock)
            {
                return Task.FromResult(_Collections.ContainsKey(id) && _CollectionTenant.TryGetValue(id, out string? t) && t == tenantId);
            }
        }

        // ---- IVectorRepository ----

        /// <inheritdoc />
        public Task StoreChunksAsync(string tenantId, string collectionId, IReadOnlyList<ChunkDocument> chunks, CancellationToken token = default)
        {
            lock (_Lock)
            {
                foreach (ChunkDocument chunk in chunks)
                {
                    _Docs.RemoveAll(d => d.TenantId == tenantId && d.CollectionId == collectionId && d.DocumentKey == chunk.DocumentKey);
                    _Docs.Add(new Stored
                    {
                        TenantId = tenantId,
                        CollectionId = collectionId,
                        DocumentKey = chunk.DocumentKey,
                        DocumentId = chunk.DocumentId,
                        Position = chunk.Position,
                        Content = chunk.Content,
                        Embedding = chunk.Embedding.ToArray(),
                        Tags = new Dictionary<string, string>(chunk.Tags, StringComparer.Ordinal)
                    });
                }
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<List<VectorSearchHit>> SearchAsync(string tenantId, string collectionId, IReadOnlyList<float> embedding, int topK, double minimumScore, IReadOnlyDictionary<string, string>? tags, CancellationToken token = default)
        {
            List<VectorSearchHit> hits = new List<VectorSearchHit>();
            lock (_Lock)
            {
                foreach (Stored doc in _Docs)
                {
                    if (doc.TenantId != tenantId || doc.CollectionId != collectionId) continue;
                    if (!MatchesTags(doc, tags)) continue;
                    double score = Cosine(embedding, doc.Embedding);
                    if (score < minimumScore) continue;
                    if (!doc.Tags.TryGetValue("litegraphNodeId", out string? nodeId) || string.IsNullOrEmpty(nodeId)) continue;
                    hits.Add(new VectorSearchHit { NodeId = nodeId, Score = score, Content = doc.Content, Position = doc.Position });
                }
            }
            return Task.FromResult(hits.OrderByDescending(h => h.Score).Take(Math.Max(1, topK)).ToList());
        }

        /// <inheritdoc />
        public Task DeleteByTagAsync(string tenantId, string collectionId, string tagKey, string tagValue, CancellationToken token = default)
        {
            lock (_Lock)
            {
                _Docs.RemoveAll(d => d.TenantId == tenantId && d.CollectionId == collectionId && d.Tags.TryGetValue(tagKey, out string? v) && v == tagValue);
            }
            return Task.CompletedTask;
        }

        // ---- IInvertedIndex ----

        /// <inheritdoc />
        public Task<List<SearchHit>> SearchAsync(string tenantId, string collectionId, string query, int maxResults, IReadOnlyDictionary<string, string>? tags, CancellationToken token = default)
        {
            List<SearchHit> hits = new List<SearchHit>();
            lock (_Lock)
            {
                foreach (Stored doc in _Docs)
                {
                    if (doc.TenantId != tenantId || doc.CollectionId != collectionId) continue;
                    if (!MatchesTags(doc, tags)) continue;
                    if (doc.Content.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    hits.Add(new SearchHit
                    {
                        DocumentId = doc.DocumentKey,
                        Score = 1.0,
                        Position = doc.Position,
                        Snippet = doc.Content,
                        Tags = new Dictionary<string, string>(doc.Tags, StringComparer.Ordinal)
                    });
                }
            }
            return Task.FromResult(hits.Take(Math.Max(1, maxResults)).ToList());
        }

        private static bool MatchesTags(Stored doc, IReadOnlyDictionary<string, string>? tags)
        {
            if (tags == null || tags.Count == 0) return true;
            foreach (KeyValuePair<string, string> tag in tags)
            {
                if (!doc.Tags.TryGetValue(tag.Key, out string? v) || v != tag.Value) return false;
            }
            return true;
        }

        private static double Cosine(IReadOnlyList<float> a, float[] b)
        {
            if (a.Count == 0 || b.Length == 0) return 0;
            int n = Math.Min(a.Count, b.Length);
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < n; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
            if (na == 0 || nb == 0) return 0;
            return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
        }
    }
}
