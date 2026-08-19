namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// Resolves which RecallDB collection a retrieval request should target. Resolution order is: an
    /// explicitly requested collection, then the configured default collection, then the tenant's first
    /// active collection. Search surfaces use this so a query without an explicit collection still resolves
    /// to a sensible target rather than failing.
    /// </summary>
    public static class CollectionResolver
    {
        /// <summary>
        /// Resolve a collection identifier for a retrieval request.
        /// </summary>
        /// <param name="collections">Collection store used to enumerate available collections.</param>
        /// <param name="tenantId">Tenant whose collections are considered.</param>
        /// <param name="requested">Explicitly requested collection id (from a query parameter); may be null or empty.</param>
        /// <param name="defaultCollectionId">Configured default collection id; may be null or empty.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The resolved collection id, or null when the tenant has no collections at all.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="collections"/> is null.</exception>
        public static async Task<string?> ResolveAsync(ICollectionStore collections, string tenantId, string? requested, string? defaultCollectionId, CancellationToken token)
        {
            if (collections == null) throw new ArgumentNullException(nameof(collections));

            if (!String.IsNullOrWhiteSpace(requested)) return requested;
            if (!String.IsNullOrWhiteSpace(defaultCollectionId)) return defaultCollectionId;

            List<RecallCollection> all = await collections.ListCollectionsAsync(tenantId, token).ConfigureAwait(false);
            foreach (RecallCollection collection in all)
            {
                if (collection.Active) return collection.Id;
            }

            return all.Count > 0 ? all[0].Id : null;
        }
    }
}
