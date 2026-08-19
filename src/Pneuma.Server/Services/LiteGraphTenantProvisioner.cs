namespace Pneuma.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Models;

    /// <summary>
    /// Provisions an isolated LiteGraph tenant for a Pneuma tenant so each tenant's knowledge graph lives in
    /// its own LiteGraph tenant (no cross-tenant leakage). On first provisioning it allocates a LiteGraph
    /// tenant GUID, hydrates the LiteGraph tenant + default user/credential + a "Pneuma" graph, and records
    /// the resulting tenant/graph GUIDs on the Pneuma tenant record. Idempotent: a tenant that already has
    /// both GUIDs is left untouched. Only Pneuma tenants that exist in the control plane are provisioned —
    /// subordinate-service system ids (which are not Pneuma tenants) are ignored.
    /// </summary>
    public class LiteGraphTenantProvisioner : ITenantProvisioner
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly ILiteGraphTenantAdmin _Admin;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the LiteGraph provisioner.</summary>
        /// <param name="db">Database driver used to read/update the Pneuma tenant record.</param>
        /// <param name="admin">LiteGraph tenant admin used to create/hydrate the LiteGraph tenant + graph.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public LiteGraphTenantProvisioner(DatabaseDriverBase db, ILiteGraphTenantAdmin admin)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Admin = admin ?? throw new ArgumentNullException(nameof(admin));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public string Name => "litegraph";

        /// <inheritdoc />
        public async Task ProvisionAsync(string tenantId, string tenantName, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) return;

            Tenant? tenant = await _Db.Tenants.ReadAsync(tenantId, token).ConfigureAwait(false);
            if (tenant == null) return;

            // Already provisioned: both the LiteGraph tenant and graph GUIDs are recorded.
            if (!String.IsNullOrWhiteSpace(tenant.LiteGraphTenantGuid) && !String.IsNullOrWhiteSpace(tenant.LiteGraphGraphGuid)) return;

            string liteGraphTenantGuid = String.IsNullOrWhiteSpace(tenant.LiteGraphTenantGuid) ? Guid.NewGuid().ToString() : tenant.LiteGraphTenantGuid!;
            string? graphGuid = await _Admin.ProvisionAsync(liteGraphTenantGuid, tenant.Name, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(graphGuid))
            {
                throw new InvalidOperationException("LiteGraph provisioning returned no graph GUID for tenant " + tenantId);
            }

            tenant.LiteGraphTenantGuid = liteGraphTenantGuid;
            tenant.LiteGraphGraphGuid = graphGuid;
            await _Db.Tenants.UpdateAsync(tenant, token).ConfigureAwait(false);
        }

        #endregion
    }
}
