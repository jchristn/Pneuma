namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Integrations.Models;

    /// <summary>In-memory Verbex fake. Stores indexed documents and does naive substring search.</summary>
    public class FakeVerbexClient : IVerbexClient
    {
        private readonly List<StoredDoc> _Docs = new List<StoredDoc>();
        private int _Counter = 0;

        /// <summary>Number of documents indexed so far.</summary>
        public int DocumentCount { get { return _Docs.Count; } }

        /// <inheritdoc />
        public Task<string> EnsureIndexAsync(CancellationToken token = default)
        {
            return Task.FromResult("idx_test");
        }

        /// <inheritdoc />
        public Task<string> AddDocumentAsync(string indexId, string content, Dictionary<string, string> tags, CancellationToken token = default)
        {
            _Counter++;
            string id = "doc_" + _Counter;
            _Docs.Add(new StoredDoc { Id = id, Content = content, Tags = tags });
            return Task.FromResult(id);
        }

        /// <inheritdoc />
        public Task<List<VerbexHit>> SearchAsync(string indexId, string query, int maxResults, CancellationToken token = default)
        {
            return SearchAsync(indexId, query, maxResults, null, token);
        }

        /// <inheritdoc />
        public Task<List<VerbexHit>> SearchAsync(string indexId, string query, int maxResults, Dictionary<string, string>? tags, CancellationToken token = default)
        {
            List<VerbexHit> hits = new List<VerbexHit>();
            foreach (StoredDoc doc in _Docs)
            {
                if (doc.Content.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (tags != null)
                {
                    bool match = true;
                    foreach (KeyValuePair<string, string> kv in tags)
                    {
                        if (!doc.Tags.TryGetValue(kv.Key, out string? v) || v != kv.Value) { match = false; break; }
                    }
                    if (!match) continue;
                }
                hits.Add(new VerbexHit { DocumentId = doc.Id, Score = 1.0, Tags = doc.Tags, Snippet = doc.Content });
                if (hits.Count >= maxResults) break;
            }
            return Task.FromResult(hits);
        }

        /// <inheritdoc />
        public Task DeleteDocumentsAsync(IEnumerable<string> documentIds, CancellationToken token = default)
        {
            if (documentIds == null) return Task.CompletedTask;
            HashSet<string> ids = new HashSet<string>(documentIds);
            _Docs.RemoveAll(doc => ids.Contains(doc.Id));
            return Task.CompletedTask;
        }

        private sealed class StoredDoc
        {
            public string Id { get; set; } = String.Empty;
            public string Content { get; set; } = String.Empty;
            public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
        }
    }
}
