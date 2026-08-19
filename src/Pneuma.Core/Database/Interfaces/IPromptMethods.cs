namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Prompt data access methods.
    /// </summary>
    public interface IPromptMethods
    {
        /// <summary>Create a prompt.</summary>
        /// <param name="prompt">Prompt to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created prompt.</returns>
        Task<Prompt> CreateAsync(Prompt prompt, CancellationToken token = default);

        /// <summary>Read a prompt by identifier.</summary>
        /// <param name="id">Prompt identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Prompt, or null if not found.</returns>
        Task<Prompt?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>Read a prompt by key, considering global (null-tenant) prompts and the given tenant.</summary>
        /// <param name="tenantId">Tenant identifier, or null for global only.</param>
        /// <param name="key">Prompt key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Prompt, or null if not found.</returns>
        Task<Prompt?> ReadByKeyAsync(string? tenantId, string key, CancellationToken token = default);

        /// <summary>Enumerate prompts visible to a tenant (global plus tenant-scoped).</summary>
        /// <param name="tenantId">Tenant identifier, or null for global only.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Prompts.</returns>
        Task<List<Prompt>> EnumerateAsync(string? tenantId, CancellationToken token = default);

        /// <summary>Update a prompt.</summary>
        /// <param name="prompt">Prompt to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated prompt.</returns>
        Task<Prompt> UpdateAsync(Prompt prompt, CancellationToken token = default);

        /// <summary>Delete a prompt by identifier.</summary>
        /// <param name="id">Prompt identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);
    }
}
