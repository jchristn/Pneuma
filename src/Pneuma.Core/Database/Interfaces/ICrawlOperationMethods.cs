namespace Pneuma.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Crawling;
    using Pneuma.Core.Enums;

    /// <summary>Crawl operation and operation object data access. Every method is scoped to a tenant except the scheduler's cross-tenant read.</summary>
    public interface ICrawlOperationMethods
    {
        /// <summary>Create an operation.</summary>
        /// <param name="operation">The operation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is null.</exception>
        Task<CrawlOperation> CreateAsync(CrawlOperation operation, CancellationToken token = default);

        /// <summary>Read an operation.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Operation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The operation, or null.</returns>
        Task<CrawlOperation?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Update an operation's status, counters, error, and timestamps.</summary>
        /// <param name="operation">The operation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is null.</exception>
        Task UpdateAsync(CrawlOperation operation, CancellationToken token = default);

        /// <summary>Enumerate a plan's operations, newest first.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="planId">Plan identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The operations.</returns>
        Task<List<CrawlOperation>> EnumerateByPlanAsync(string tenantId, string planId, CancellationToken token = default);

        /// <summary>Enumerate a tenant's operations, newest first.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The operations.</returns>
        Task<List<CrawlOperation>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>Enumerate operations in every tenant with a status.</summary>
        /// <param name="status">The status.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The operations.</returns>
        Task<List<CrawlOperation>> EnumerateByStatusAsync(CrawlOperationStatusEnum status, CancellationToken token = default);

        /// <summary>Delete a plan's finished operations (and their objects) that started before a cutoff.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="planId">Plan identifier.</param>
        /// <param name="cutoffUtc">Operations that started before this are deleted.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of operations deleted.</returns>
        Task<int> DeleteFinishedBeforeAsync(string tenantId, string planId, DateTime cutoffUtc, CancellationToken token = default);

        /// <summary>Create operation objects in batches.</summary>
        /// <param name="objects">The objects.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="objects"/> is null.</exception>
        Task CreateObjectsAsync(IEnumerable<CrawlOperationObject> objects, CancellationToken token = default);

        /// <summary>Update operation objects' outcome and detail in batches.</summary>
        /// <param name="objects">The objects.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="objects"/> is null.</exception>
        Task UpdateObjectsAsync(IEnumerable<CrawlOperationObject> objects, CancellationToken token = default);

        /// <summary>Enumerate an operation's objects in creation order.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="operationId">Operation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The objects.</returns>
        Task<List<CrawlOperationObject>> EnumerateObjectsAsync(string tenantId, string operationId, CancellationToken token = default);
    }
}
