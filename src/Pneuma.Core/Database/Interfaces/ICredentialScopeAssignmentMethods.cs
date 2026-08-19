namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Credential scope assignment data access methods.
    /// </summary>
    public interface ICredentialScopeAssignmentMethods
    {
        /// <summary>Create an assignment.</summary>
        /// <param name="assignment">Assignment to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created assignment.</returns>
        Task<CredentialScopeAssignment> CreateAsync(CredentialScopeAssignment assignment, CancellationToken token = default);

        /// <summary>Read an assignment by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Assignment identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Assignment, or null if not found.</returns>
        Task<CredentialScopeAssignment?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Enumerate assignments for a credential within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="credentialId">Credential identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Assignments.</returns>
        Task<List<CredentialScopeAssignment>> EnumerateByCredentialAsync(string tenantId, string credentialId, CancellationToken token = default);

        /// <summary>Update an assignment.</summary>
        /// <param name="assignment">Assignment to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated assignment.</returns>
        Task<CredentialScopeAssignment> UpdateAsync(CredentialScopeAssignment assignment, CancellationToken token = default);

        /// <summary>Delete an assignment by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Assignment identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);
    }
}
