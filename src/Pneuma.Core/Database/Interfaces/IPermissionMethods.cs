namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Permission data access methods.
    /// </summary>
    public interface IPermissionMethods
    {
        /// <summary>Create a permission.</summary>
        /// <param name="permission">Permission to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created permission.</returns>
        Task<Permission> CreateAsync(Permission permission, CancellationToken token = default);

        /// <summary>Read a permission by identifier.</summary>
        /// <param name="id">Permission identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Permission, or null if not found.</returns>
        Task<Permission?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>Enumerate permissions visible to a tenant (built-in plus tenant-scoped).</summary>
        /// <param name="tenantId">Tenant identifier, or null for built-in only.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Permissions.</returns>
        Task<List<Permission>> EnumerateAsync(string? tenantId, CancellationToken token = default);

        /// <summary>Read the permissions mapped to a role.</summary>
        /// <param name="roleId">Role identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Permissions.</returns>
        Task<List<Permission>> EnumerateByRoleAsync(string roleId, CancellationToken token = default);

        /// <summary>Update a permission.</summary>
        /// <param name="permission">Permission to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated permission.</returns>
        Task<Permission> UpdateAsync(Permission permission, CancellationToken token = default);

        /// <summary>Delete a permission by identifier.</summary>
        /// <param name="id">Permission identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);
    }
}
