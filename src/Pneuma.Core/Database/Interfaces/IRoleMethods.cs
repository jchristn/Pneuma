namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Role data access methods. Built-in roles have a null tenant and are globally visible.
    /// </summary>
    public interface IRoleMethods
    {
        /// <summary>Create a role.</summary>
        /// <param name="role">Role to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created role.</returns>
        Task<UserRole> CreateAsync(UserRole role, CancellationToken token = default);

        /// <summary>Read a role by identifier.</summary>
        /// <param name="id">Role identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Role, or null if not found.</returns>
        Task<UserRole?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>Read a role by name, considering built-in (null-tenant) roles and the given tenant.</summary>
        /// <param name="tenantId">Tenant identifier, or null for built-in only.</param>
        /// <param name="name">Role name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Role, or null if not found.</returns>
        Task<UserRole?> ReadByNameAsync(string? tenantId, string name, CancellationToken token = default);

        /// <summary>Enumerate roles visible to a tenant (built-in plus tenant-scoped).</summary>
        /// <param name="tenantId">Tenant identifier, or null for built-in only.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Roles.</returns>
        Task<List<UserRole>> EnumerateAsync(string? tenantId, CancellationToken token = default);

        /// <summary>Update a role.</summary>
        /// <param name="role">Role to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated role.</returns>
        Task<UserRole> UpdateAsync(UserRole role, CancellationToken token = default);

        /// <summary>Delete a role by identifier.</summary>
        /// <param name="id">Role identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Atomically create a role together with its permissions and role-permission maps in a single
        /// transaction, so first-boot seeding never leaves a role half-provisioned with some of its
        /// permissions missing.
        /// </summary>
        /// <param name="role">Role to create.</param>
        /// <param name="permissions">Permissions to create and attach to the role.</param>
        /// <param name="maps">Role-permission maps binding each permission to the role.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created role.</returns>
        Task<UserRole> CreateWithPermissionsAsync(UserRole role, IReadOnlyList<Permission> permissions, IReadOnlyList<RolePermissionMap> maps, CancellationToken token = default);
    }
}
