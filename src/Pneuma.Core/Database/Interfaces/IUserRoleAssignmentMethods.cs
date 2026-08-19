namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// User role assignment data access methods (the authoritative RBAC assignment table).
    /// </summary>
    public interface IUserRoleAssignmentMethods
    {
        /// <summary>Create an assignment.</summary>
        /// <param name="assignment">Assignment to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created assignment.</returns>
        Task<UserRoleAssignment> CreateAsync(UserRoleAssignment assignment, CancellationToken token = default);

        /// <summary>Read an assignment by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Assignment identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Assignment, or null if not found.</returns>
        Task<UserRoleAssignment?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Enumerate assignments for a user within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="userId">User identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Assignments.</returns>
        Task<List<UserRoleAssignment>> EnumerateByUserAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>Enumerate all assignments within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Assignments.</returns>
        Task<List<UserRoleAssignment>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>Update an assignment.</summary>
        /// <param name="assignment">Assignment to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated assignment.</returns>
        Task<UserRoleAssignment> UpdateAsync(UserRoleAssignment assignment, CancellationToken token = default);

        /// <summary>Delete an assignment by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Assignment identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);
    }
}
