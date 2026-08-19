namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Role-permission mapping data access methods.
    /// </summary>
    public interface IRolePermissionMapMethods
    {
        /// <summary>Create a role-permission mapping.</summary>
        /// <param name="map">Mapping to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created mapping.</returns>
        Task<RolePermissionMap> CreateAsync(RolePermissionMap map, CancellationToken token = default);

        /// <summary>Read a mapping by identifier.</summary>
        /// <param name="id">Mapping identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Mapping, or null if not found.</returns>
        Task<RolePermissionMap?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>Enumerate mappings for a role.</summary>
        /// <param name="roleId">Role identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Mappings.</returns>
        Task<List<RolePermissionMap>> EnumerateByRoleAsync(string roleId, CancellationToken token = default);

        /// <summary>Delete a mapping by identifier.</summary>
        /// <param name="id">Mapping identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);
    }
}
