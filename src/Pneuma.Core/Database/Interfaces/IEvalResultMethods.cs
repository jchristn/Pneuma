namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>Persisted evaluation result data access methods.</summary>
    public interface IEvalResultMethods
    {
        /// <summary>Create one result.</summary>
        /// <param name="result">Result to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created result.</returns>
        Task<EvalResult> CreateAsync(EvalResult result, CancellationToken token = default);

        /// <summary>Enumerate a run's results in creation order.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="runId">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The run's results.</returns>
        Task<List<EvalResult>> EnumerateByRunAsync(string tenantId, string runId, CancellationToken token = default);

        /// <summary>Delete all results for a run. Idempotent.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="runId">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteByRunAsync(string tenantId, string runId, CancellationToken token = default);
    }
}
