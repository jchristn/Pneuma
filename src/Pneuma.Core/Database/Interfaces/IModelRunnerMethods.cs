namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Model runner data access methods.
    /// </summary>
    public interface IModelRunnerMethods
    {
        /// <summary>Create a model runner.</summary>
        /// <param name="runner">Runner to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created runner.</returns>
        Task<ModelRunner> CreateAsync(ModelRunner runner, CancellationToken token = default);

        /// <summary>Read a runner by identifier.</summary>
        /// <param name="id">Runner identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Runner, or null if not found.</returns>
        Task<ModelRunner?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>Read a runner by name, considering global (null-tenant) runners and the given tenant.</summary>
        /// <param name="tenantId">Tenant identifier, or null for global only.</param>
        /// <param name="name">Runner name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Runner, or null if not found.</returns>
        Task<ModelRunner?> ReadByNameAsync(string? tenantId, string name, CancellationToken token = default);

        /// <summary>Enumerate runners visible to a tenant (global plus tenant-scoped).</summary>
        /// <param name="tenantId">Tenant identifier, or null for global only.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Runners.</returns>
        Task<List<ModelRunner>> EnumerateAsync(string? tenantId, CancellationToken token = default);

        /// <summary>Update a runner.</summary>
        /// <param name="runner">Runner to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated runner.</returns>
        Task<ModelRunner> UpdateAsync(ModelRunner runner, CancellationToken token = default);

        /// <summary>Delete a runner by identifier.</summary>
        /// <param name="id">Runner identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);
    }
}
