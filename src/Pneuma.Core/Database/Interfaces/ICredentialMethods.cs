namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Credential data access methods. Credentials are tenant-scoped and owned by a user.
    /// </summary>
    public interface ICredentialMethods
    {
        /// <summary>Create a credential.</summary>
        /// <param name="credential">Credential to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created credential.</returns>
        Task<Credential> CreateAsync(Credential credential, CancellationToken token = default);

        /// <summary>Read a credential by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Credential identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Credential, or null if not found.</returns>
        Task<Credential?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Read a credential by access key (global lookup for authentication).</summary>
        /// <param name="accessKey">Access key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Credential, or null if not found.</returns>
        Task<Credential?> ReadByAccessKeyAsync(string accessKey, CancellationToken token = default);

        /// <summary>Enumerate credentials within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Credentials.</returns>
        Task<List<Credential>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>Enumerate credentials owned by a user within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="userId">User identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Credentials.</returns>
        Task<List<Credential>> EnumerateByUserAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>Update a credential.</summary>
        /// <param name="credential">Credential to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated credential.</returns>
        Task<Credential> UpdateAsync(Credential credential, CancellationToken token = default);

        /// <summary>Delete a credential by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Credential identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);
    }
}
