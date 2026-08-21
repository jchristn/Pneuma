namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>Persisted evaluation ground-truth fact data access methods.</summary>
    public interface IEvalFactMethods
    {
        /// <summary>Create a fact.</summary>
        /// <param name="fact">Fact to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created fact.</returns>
        Task<EvalFact> CreateAsync(EvalFact fact, CancellationToken token = default);

        /// <summary>Read a fact by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Fact identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Fact, or null if not found.</returns>
        Task<EvalFact?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Enumerate a subject's facts, newest first.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The subject's facts.</returns>
        Task<List<EvalFact>> EnumerateBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default);

        /// <summary>Delete a fact by identifier. Idempotent.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Fact identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Delete all facts for a subject. Idempotent.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default);
    }
}
