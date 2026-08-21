namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;

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

        /// <summary>Update an existing job event in place (status, message, durations). Used to resolve a
        /// contended stage's queued entry to its terminal state without inserting a duplicate row.</summary>
        /// <param name="jobEvent">Event to update; matched by id and tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated event.</returns>
        Task<IngestionJobEvent> UpdateAsync(IngestionJobEvent jobEvent, CancellationToken token = default);

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

        /// <summary>
        /// Summarize ingestion activity into time buckets broken down by pipeline stage, scoped by the
        /// supplied filter (tenant, subject, time range, bucket size).
        /// </summary>
        /// <param name="filter">Summary filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Time-bucketed, stage-stacked activity summary.</returns>
        Task<IngestionActivitySummary> SummarizeAsync(IngestionActivityFilter filter, CancellationToken token = default);
    }
}
