namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Serialization;
    using Pneuma.Core.Storage;

    /// <summary>
    /// Stores classification results so an identical request is answered without a model call. The key is a SHA-256 of
    /// everything that shapes the answer: the model runner, the model, the temperature, the full system prompt (task,
    /// ontology, output contract), and the full user prompt (subject, cells, taxonomy hints). Results are stored in the
    /// blob store under the tenant's prefix and indexed in the database, so entries never cross tenants and can be
    /// pruned or removed with their tenant.
    /// </summary>
    public class ClassificationCache
    {
        #region Private-Members

        private const string _Prefix = "classification-cache/";
        private readonly DatabaseDriverBase _Db;
        private readonly IBlobStore _Blobs;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the cache.</summary>
        /// <param name="db">Database driver (the index).</param>
        /// <param name="blobs">Blob store (the results).</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public ClassificationCache(DatabaseDriverBase db, IBlobStore blobs)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
        }

        #endregion

        #region Public-Methods

        /// <summary>Compute the cache key of a classification request.</summary>
        /// <param name="runnerId">Model runner identifier.</param>
        /// <param name="model">Model name.</param>
        /// <param name="temperature">Temperature.</param>
        /// <param name="systemPrompt">Full system prompt.</param>
        /// <param name="userPrompt">Full user prompt.</param>
        /// <returns>The key (64 lower-case hex characters).</returns>
        public static string Key(string runnerId, string? model, double temperature, string systemPrompt, string userPrompt)
        {
            StringBuilder material = new StringBuilder();
            material.Append("runner:").Append(runnerId).Append('\n');
            material.Append("model:").Append(model ?? String.Empty).Append('\n');
            material.Append("temperature:").Append(temperature.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
            material.Append("system:").Append(systemPrompt).Append('\n');
            material.Append("user:").Append(userPrompt);
            return Sha256(material.ToString());
        }

        /// <summary>A short hash of a text, for provenance (the first 8 hex characters of its SHA-256).</summary>
        /// <param name="text">The text.</param>
        /// <returns>The short hash.</returns>
        public static string ShortHash(string? text)
        {
            return Sha256(text ?? String.Empty).Substring(0, 8);
        }

        /// <summary>Read a cached result, or null on a miss (a missing or unreadable blob is a miss and removes the index row).</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="key">Cache key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A copy of the cached subgraph, or null.</returns>
        public async Task<CandidateSubgraph?> TryGetAsync(string tenantId, string key, CancellationToken token = default)
        {
            ClassificationCacheEntry? entry = await _Db.ClassificationCache.ReadAsync(tenantId, key, token).ConfigureAwait(false);
            if (entry == null) return null;
            byte[]? bytes = await _Blobs.ReadAsync(entry.BlobKey, token).ConfigureAwait(false);
            CandidateSubgraph? subgraph = null;
            if (bytes != null && bytes.Length > 0)
            {
                try { subgraph = Json.Deserialize<CandidateSubgraph>(Encoding.UTF8.GetString(bytes)); }
                catch (Exception e) when (!(e is OperationCanceledException)) { subgraph = null; }
            }
            if (subgraph == null)
            {
                await _Db.ClassificationCache.DeleteAsync(tenantId, key, token).ConfigureAwait(false);
                return null;
            }
            await _Db.ClassificationCache.RecordHitAsync(tenantId, key, token).ConfigureAwait(false);
            return subgraph;
        }

        /// <summary>Store a result.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject whose ingestion produced it.</param>
        /// <param name="key">Cache key.</param>
        /// <param name="subgraph">The result.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="subgraph"/> is null.</exception>
        public async Task StoreAsync(string tenantId, string? subjectId, string key, CandidateSubgraph subgraph, CancellationToken token = default)
        {
            if (subgraph == null) throw new ArgumentNullException(nameof(subgraph));
            string blobKey = _Prefix + tenantId + "/" + key + ".json";
            await _Blobs.WriteAsync(blobKey, Encoding.UTF8.GetBytes(Json.Serialize(subgraph)), token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            await _Db.ClassificationCache.UpsertAsync(new ClassificationCacheEntry
            {
                TenantId = tenantId,
                CacheKey = key,
                SubjectId = subjectId,
                BlobKey = blobKey,
                Hits = 0,
                CreatedUtc = now,
                LastUsedUtc = now
            }, token).ConfigureAwait(false);
        }

        /// <summary>Remove a tenant's entries, or only those a subject stored.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier, or null for the whole tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>How many entries were removed.</returns>
        public async Task<int> ClearAsync(string tenantId, string? subjectId, CancellationToken token = default)
        {
            List<ClassificationCacheEntry> entries = await _Db.ClassificationCache.EnumerateAsync(tenantId, subjectId, token).ConfigureAwait(false);
            foreach (ClassificationCacheEntry entry in entries) await RemoveAsync(entry, token).ConfigureAwait(false);
            return entries.Count;
        }

        /// <summary>Remove entries unused for longer than the retention period.</summary>
        /// <param name="retentionDays">Days an unused entry is kept.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>How many entries were removed.</returns>
        public async Task<int> PruneAsync(int retentionDays, CancellationToken token = default)
        {
            DateTime cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, retentionDays));
            List<ClassificationCacheEntry> stale = await _Db.ClassificationCache.EnumerateUnusedSinceAsync(cutoff, 1000, token).ConfigureAwait(false);
            foreach (ClassificationCacheEntry entry in stale) await RemoveAsync(entry, token).ConfigureAwait(false);
            return stale.Count;
        }

        /// <summary>Count a tenant's entries, or those a subject stored.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier, or null for the whole tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of entries.</returns>
        public async Task<int> CountAsync(string tenantId, string? subjectId, CancellationToken token = default)
        {
            List<ClassificationCacheEntry> entries = await _Db.ClassificationCache.EnumerateAsync(tenantId, subjectId, token).ConfigureAwait(false);
            return entries.Count;
        }

        #endregion

        #region Private-Methods

        private async Task RemoveAsync(ClassificationCacheEntry entry, CancellationToken token)
        {
            try { await _Blobs.DeleteAsync(entry.BlobKey, token).ConfigureAwait(false); }
            catch (Exception e) when (!(e is OperationCanceledException)) { }
            await _Db.ClassificationCache.DeleteAsync(entry.TenantId, entry.CacheKey, token).ConfigureAwait(false);
        }

        private static string Sha256(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                StringBuilder hex = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        #endregion
    }
}
