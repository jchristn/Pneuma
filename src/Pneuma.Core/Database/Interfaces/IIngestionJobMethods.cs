namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;

    /// <summary>
    /// Ingestion job data access methods.
    /// </summary>
    public interface IIngestionJobMethods
    {
        /// <summary>Create a job.</summary>
        /// <param name="job">Job to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created job.</returns>
        Task<IngestionJob> CreateAsync(IngestionJob job, CancellationToken token = default);

        /// <summary>Read a job by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Job, or null if not found.</returns>
        Task<IngestionJob?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Enumerate jobs within a tenant, optionally filtered by status.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="status">Optional status filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Jobs.</returns>
        Task<List<IngestionJob>> EnumerateAsync(string tenantId, IngestionStatusEnum? status, CancellationToken token = default);

        /// <summary>Enumerate jobs for a specific content link within a tenant, oldest first.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="linkId">Link identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Jobs.</returns>
        Task<List<IngestionJob>> EnumerateByLinkAsync(string tenantId, string linkId, CancellationToken token = default);

        /// <summary>Atomically claim the next queued job across all tenants for processing.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The claimed job, or null if none are queued.</returns>
        Task<IngestionJob?> ClaimNextQueuedAsync(CancellationToken token = default);

        /// <summary>Update a job.</summary>
        /// <param name="job">Job to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated job.</returns>
        Task<IngestionJob> UpdateAsync(IngestionJob job, CancellationToken token = default);

        /// <summary>Delete a job by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Atomically delete a job together with its job events in a single transaction, so a job never
        /// remains without its events, nor events without their job.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the job row existed and was deleted.</returns>
        Task<bool> DeleteWithEventsAsync(string tenantId, string id, CancellationToken token = default);
    }
}
