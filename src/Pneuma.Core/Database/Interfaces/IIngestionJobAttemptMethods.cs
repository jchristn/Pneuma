namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Ingestion.Models;

    /// <summary>Ingestion job attempt data access methods. Every method is scoped to a tenant.</summary>
    public interface IIngestionJobAttemptMethods
    {
        /// <summary>Record an attempt.</summary>
        /// <param name="attempt">The attempt to record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The recorded attempt.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="attempt"/> is null.</exception>
        Task<IngestionJobAttempt> CreateAsync(IngestionJobAttempt attempt, CancellationToken token = default);

        /// <summary>Enumerate a job's attempts, oldest first.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="jobId">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The attempts; empty when the job has none.</returns>
        Task<List<IngestionJobAttempt>> EnumerateByJobAsync(string tenantId, string jobId, CancellationToken token = default);

        /// <summary>Delete every attempt of a job. Idempotent.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="jobId">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteByJobAsync(string tenantId, string jobId, CancellationToken token = default);
    }
}
