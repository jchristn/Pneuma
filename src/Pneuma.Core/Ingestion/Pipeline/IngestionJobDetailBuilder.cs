namespace Pneuma.Core.Ingestion.Pipeline
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Responses;

    /// <summary>
    /// Builds the detail view of an ingestion job (the job, its stage events, its attempts, and remediation for its
    /// failure category) so every route that returns job detail returns the same shape.
    /// </summary>
    public static class IngestionJobDetailBuilder
    {
        #region Public-Methods

        /// <summary>Build a job's detail.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="job">The job.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The detail.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="db"/> or <paramref name="job"/> is null.</exception>
        public static async Task<IngestionJobDetail> BuildAsync(DatabaseDriverBase db, string tenantId, IngestionJob job, CancellationToken token)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (job == null) throw new ArgumentNullException(nameof(job));
            List<IngestionJobEvent> events = await db.IngestionJobEvents.EnumerateByJobAsync(tenantId, job.Id, token).ConfigureAwait(false);
            List<IngestionJobAttempt> attempts = await db.IngestionJobAttempts.EnumerateByJobAsync(tenantId, job.Id, token).ConfigureAwait(false);
            return new IngestionJobDetail
            {
                Job = job,
                Events = events,
                Attempts = attempts,
                Remediation = job.FailureCategory == null ? null : IngestionFailureClassifier.Describe(job.FailureCategory.Value).Remediation
            };
        }

        #endregion
    }
}
