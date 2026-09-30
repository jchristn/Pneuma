namespace Pneuma.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Ontologies;

    /// <summary>Classification cache index data access. The cached results themselves live in the blob store.</summary>
    public interface IClassificationCacheMethods
    {
        /// <summary>Read an entry.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="cacheKey">Cache key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry, or null.</returns>
        Task<ClassificationCacheEntry?> ReadAsync(string tenantId, string cacheKey, CancellationToken token = default);

        /// <summary>Store an entry, replacing any entry with the same key.</summary>
        /// <param name="entry">The entry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry"/> is null.</exception>
        Task UpsertAsync(ClassificationCacheEntry entry, CancellationToken token = default);

        /// <summary>Record a reuse of an entry (increments hits and sets the last-used time).</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="cacheKey">Cache key.</param>
        /// <param name="token">Cancellation token.</param>
        Task RecordHitAsync(string tenantId, string cacheKey, CancellationToken token = default);

        /// <summary>Enumerate entries in any tenant last used before a time (for pruning), oldest first.</summary>
        /// <param name="beforeUtc">Cut-off time.</param>
        /// <param name="maxResults">Maximum entries to return.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entries.</returns>
        Task<List<ClassificationCacheEntry>> EnumerateUnusedSinceAsync(DateTime beforeUtc, int maxResults, CancellationToken token = default);

        /// <summary>Enumerate a tenant's entries, optionally only those a subject stored.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier, or null for every entry in the tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entries.</returns>
        Task<List<ClassificationCacheEntry>> EnumerateAsync(string tenantId, string? subjectId, CancellationToken token = default);

        /// <summary>Delete an entry.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="cacheKey">Cache key.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteAsync(string tenantId, string cacheKey, CancellationToken token = default);
    }
}
