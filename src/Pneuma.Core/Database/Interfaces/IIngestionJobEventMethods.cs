namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Ingestion job event data access methods.
    /// </summary>
    public interface IIngestionJobEventMethods
    {
        /// <summary>Create a job event.</summary>
        /// <param name="jobEvent">Event to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created event.</returns>
        Task<IngestionJobEvent> CreateAsync(IngestionJobEvent jobEvent, CancellationToken token = default);

        /// <summary>Enumerate events for a job within a tenant, chronological.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="jobId">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Events.</returns>
        Task<List<IngestionJobEvent>> EnumerateByJobAsync(string tenantId, string jobId, CancellationToken token = default);

        /// <summary>Delete all events for a job within a tenant. Idempotent.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="jobId">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteByJobAsync(string tenantId, string jobId, CancellationToken token = default);
    }
}
