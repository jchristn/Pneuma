namespace Pneuma.Server.Services
{
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Abstractions;

    /// <summary>
    /// Resolves the knowledge-graph repository bound to a specific Pneuma tenant's isolated LiteGraph tenant
    /// and graph. Each Pneuma tenant's graph lives in its own LiteGraph tenant, so every graph operation must
    /// be routed through the caller's tenant to prevent cross-tenant reads/writes. A tenant that has not been
    /// provisioned yet resolves to the configured default/system graph.
    /// </summary>
    public interface IGraphRepositoryFactory
    {
        /// <summary>Resolve the graph repository for a tenant.</summary>
        /// <param name="tenantId">The Pneuma tenant id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A graph repository bound to the tenant's LiteGraph tenant/graph, or the default when unprovisioned.</returns>
        Task<IGraphRepository> ForTenantAsync(string tenantId, CancellationToken token = default);
    }
}
