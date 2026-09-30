namespace Pneuma.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Ontologies;

    /// <summary>SQL Server classification cache index methods.</summary>
    internal class ClassificationCacheMethods : SqlServerMethodsBase, IClassificationCacheMethods
    {
        internal ClassificationCacheMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<ClassificationCacheEntry?> ReadAsync(string tenantId, string cacheKey, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM classificationcache WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND cachekey = " + Sanitizer.Str(cacheKey) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task UpsertAsync(ClassificationCacheEntry entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            string t = Sanitizer.Str(entry.TenantId);
            string k = Sanitizer.Str(entry.CacheKey);
            await QueryTransaction(new List<string>
            {
                "DELETE FROM classificationcache WHERE tenantid = " + t + " AND cachekey = " + k + ";",
                "INSERT INTO classificationcache (tenantid, cachekey, subjectid, blobkey, hits, createdutc, lastusedutc) VALUES (" + t + ", " + k + ", " +
                    Sanitizer.Str(entry.SubjectId) + ", " + Sanitizer.Str(entry.BlobKey) + ", " + Sanitizer.Num(entry.Hits) + ", " +
                    Sanitizer.Ts(entry.CreatedUtc) + ", " + Sanitizer.Ts(entry.LastUsedUtc) + ");"
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task RecordHitAsync(string tenantId, string cacheKey, CancellationToken token = default)
        {
            await Query(
                "UPDATE classificationcache SET hits = hits + 1, lastusedutc = " + Sanitizer.Ts(DateTime.UtcNow) +
                " WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND cachekey = " + Sanitizer.Str(cacheKey) + ";", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ClassificationCacheEntry>> EnumerateUnusedSinceAsync(DateTime beforeUtc, int maxResults, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM classificationcache WHERE lastusedutc < " + Sanitizer.Ts(beforeUtc) + " ORDER BY lastusedutc ASC;", token).ConfigureAwait(false);
            List<ClassificationCacheEntry> result = new List<ClassificationCacheEntry>();
            foreach (DataRow row in table.Rows)
            {
                if (result.Count >= maxResults) break;
                result.Add(Map(row));
            }
            return result;
        }

        /// <inheritdoc />
        public async Task<List<ClassificationCacheEntry>> EnumerateAsync(string tenantId, string? subjectId, CancellationToken token = default)
        {
            string sql = "SELECT * FROM classificationcache WHERE tenantid = " + Sanitizer.Str(tenantId);
            if (!String.IsNullOrWhiteSpace(subjectId)) sql += " AND subjectid = " + Sanitizer.Str(subjectId);
            DataTable table = await Query(sql + ";", token).ConfigureAwait(false);
            List<ClassificationCacheEntry> result = new List<ClassificationCacheEntry>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string cacheKey, CancellationToken token = default)
        {
            await Query(
                "DELETE FROM classificationcache WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND cachekey = " + Sanitizer.Str(cacheKey) + ";", token).ConfigureAwait(false);
        }

        internal static ClassificationCacheEntry Map(DataRow row)
        {
            return new ClassificationCacheEntry
            {
                TenantId = RowReader.GetString(row, "tenantid"),
                CacheKey = RowReader.GetString(row, "cachekey"),
                SubjectId = RowReader.GetNullableString(row, "subjectid"),
                BlobKey = RowReader.GetString(row, "blobkey"),
                Hits = RowReader.GetInt(row, "hits"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUsedUtc = RowReader.GetDateTime(row, "lastusedutc")
            };
        }
    }
}
