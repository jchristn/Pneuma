namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Legacy user-role map data access methods.
    /// </summary>
    public interface IUserRoleMapMethods
    {
        /// <summary>Create a mapping.</summary>
        /// <param name="map">Mapping to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created mapping.</returns>
        Task<UserRoleMap> CreateAsync(UserRoleMap map, CancellationToken token = default);

        /// <summary>Enumerate mappings for a user within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="userId">User identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Mappings.</returns>
        Task<List<UserRoleMap>> EnumerateByUserAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>Delete a mapping by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Mapping identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);
    }
}
