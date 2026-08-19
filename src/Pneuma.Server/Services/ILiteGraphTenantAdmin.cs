namespace Pneuma.Server.Services
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Provisions and hydrates an isolated LiteGraph tenant (tenant + default user/credential + graph) for a
    /// Pneuma tenant, returning the graph GUID. Abstracted so the provisioning flow can be tested without a
    /// live LiteGraph server.
    /// </summary>
    public interface ILiteGraphTenantAdmin
    {
        /// <summary>Ensure the LiteGraph tenant and its "Pneuma" graph exist. Idempotent.</summary>
        /// <param name="tenantGuid">GUID of the LiteGraph tenant to provision.</param>
        /// <param name="name">Display name for the tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The graph GUID, or null when the graph could not be resolved/created.</returns>
        Task<string?> ProvisionAsync(string tenantGuid, string name, CancellationToken token = default);
    }
}
