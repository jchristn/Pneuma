namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Tenant data access methods.
    /// </summary>
    public interface ITenantMethods
    {
        /// <summary>Create a tenant.</summary>
        /// <param name="tenant">Tenant to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created tenant.</returns>
        Task<Tenant> CreateAsync(Tenant tenant, CancellationToken token = default);

        /// <summary>Read a tenant by identifier.</summary>
        /// <param name="id">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tenant, or null if not found.</returns>
        Task<Tenant?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>Enumerate all tenants.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tenants.</returns>
        Task<List<Tenant>> EnumerateAsync(CancellationToken token = default);

        /// <summary>Update a tenant.</summary>
        /// <param name="tenant">Tenant to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated tenant.</returns>
        Task<Tenant> UpdateAsync(Tenant tenant, CancellationToken token = default);

        /// <summary>Delete a tenant by identifier.</summary>
        /// <param name="id">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate tenants whose background deletion is pending or in progress (deletion status Pending or
        /// Deleting), so the deletion worker can claim and resume them.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tenants queued for or mid-deletion.</returns>
        Task<List<Tenant>> EnumeratePendingDeletionAsync(CancellationToken token = default);

        /// <summary>
        /// Delete a tenant and every tenant-scoped database row it owns (users, credentials, sessions, roles,
        /// permissions, subjects, links, jobs, model runners, prompts, chat, evaluation, request history, audit,
        /// and the tenant row) in a single transaction. Global (tenant-null) rows are preserved. External-store
        /// cleanup (graph, vectors, artifacts, subordinate-service tenants) is performed by the caller before this.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the tenant row was deleted.</returns>
        Task<bool> DeleteWithTenantDataAsync(string tenantId, CancellationToken token = default);
    }
}
