namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>Persisted evaluation run data access methods.</summary>
    public interface IEvalRunMethods
    {
        /// <summary>Create a run.</summary>
        /// <param name="run">Run to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created run.</returns>
        Task<EvalRun> CreateAsync(EvalRun run, CancellationToken token = default);

        /// <summary>Read a run by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Run, or null if not found.</returns>
        Task<EvalRun?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Enumerate a tenant's runs, newest first, optionally scoped to a subject.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject to scope to, or null for all subjects.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Runs, newest first.</returns>
        Task<List<EvalRun>> EnumerateAsync(string tenantId, string? subjectId, CancellationToken token = default);

        /// <summary>
        /// Atomically claim the oldest <c>Pending</c> run across all tenants, transitioning it to
        /// <c>Running</c>. Used by the background eval worker; returns null when none are queued.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The claimed run (now <c>Running</c>), or null.</returns>
        Task<EvalRun?> ClaimNextQueuedAsync(CancellationToken token = default);

        /// <summary>Update a run's status and tallies.</summary>
        /// <param name="run">Run to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated run.</returns>
        Task<EvalRun> UpdateAsync(EvalRun run, CancellationToken token = default);

        /// <summary>
        /// Update only a run's fact tallies (total/pass/partial/fail) while it is still <c>Running</c> — used
        /// for per-fact progress so a concurrent cancel (which sets the status) is never clobbered.
        /// </summary>
        /// <param name="run">Run whose tallies to persist.</param>
        /// <param name="token">Cancellation token.</param>
        Task UpdateProgressAsync(EvalRun run, CancellationToken token = default);

        /// <summary>Delete a run by identifier. Idempotent.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Delete all runs for a subject. Idempotent.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default);
    }
}
