namespace Pneuma.Server.Services
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Provisions the per-tenant resources a single subordinate service owns when a Pneuma tenant is created
    /// (for example a RecallDB tenant and its default collection). Implementations are registered with the
    /// <see cref="TenantProvisioningService"/>, which invokes them best-effort so an unavailable subordinate
    /// service never blocks tenant creation. Implementations must be idempotent — provisioning runs on tenant
    /// create, on first-boot seeding, and on startup, so it may be called more than once for the same tenant.
    /// </summary>
    public interface ITenantProvisioner
    {
        /// <summary>Short logical name of the subordinate service (used in diagnostics).</summary>
        string Name { get; }

        /// <summary>Provision the subordinate service's resources for a tenant. Idempotent.</summary>
        /// <param name="tenantId">The Pneuma tenant id (reused as the subordinate service's tenant id where applicable).</param>
        /// <param name="tenantName">The tenant's display name.</param>
        /// <param name="token">Cancellation token.</param>
        Task ProvisionAsync(string tenantId, string tenantName, CancellationToken token = default);
    }
}
