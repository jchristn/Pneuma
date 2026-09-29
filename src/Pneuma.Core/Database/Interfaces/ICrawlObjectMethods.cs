namespace Pneuma.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Crawling;

    /// <summary>Crawl object data access (the per-plan delta baseline). Every method is scoped to a tenant.</summary>
    public interface ICrawlObjectMethods
    {
        /// <summary>Enumerate a plan's objects.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="planId">Plan identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The objects, ordered by key.</returns>
        Task<List<CrawlObject>> EnumerateByPlanAsync(string tenantId, string planId, CancellationToken token = default);

        /// <summary>Read the object that holds a link's content.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="linkId">Link identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The object, or null.</returns>
        Task<CrawlObject?> ReadByLinkAsync(string tenantId, string linkId, CancellationToken token = default);

        /// <summary>Create objects in batches, each batch in one transaction.</summary>
        /// <param name="objects">The objects.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="objects"/> is null.</exception>
        Task CreateManyAsync(IEnumerable<CrawlObject> objects, CancellationToken token = default);

        /// <summary>Update objects in batches, each batch in one transaction.</summary>
        /// <param name="objects">The objects.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="objects"/> is null.</exception>
        Task UpdateManyAsync(IEnumerable<CrawlObject> objects, CancellationToken token = default);

        /// <summary>Delete objects by id.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="ids">Object identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteManyAsync(string tenantId, IEnumerable<string> ids, CancellationToken token = default);
    }
}
