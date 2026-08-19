namespace Test.Shared.Suites
{
    using System;
    using Pneuma.Core.Database;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Models;
    using Pneuma.Server.Services;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Per-tenant LiteGraph isolation: provisioning allocates and records a tenant's LiteGraph tenant/graph
    /// GUIDs, and the graph-repository factory routes provisioned tenants to their own graph while
    /// unprovisioned tenants fall back to the default graph.
    /// </summary>
    public static class GraphTenancySuite
    {
        /// <summary>Build the suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "GraphTenancy",
                displayName: "Per-tenant Graph Isolation",
                cases: new System.Collections.Generic.List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("GraphTenancy", "Provisioning_RecordsLiteGraphGuids", "Provisioning a tenant creates and records its LiteGraph tenant/graph GUIDs (idempotent)",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }, ct);

                            FakeLiteGraphTenantAdmin admin = new FakeLiteGraphTenantAdmin();
                            LiteGraphTenantProvisioner provisioner = new LiteGraphTenantProvisioner(db, admin);
                            await provisioner.ProvisionAsync(tenant.Id, tenant.Name, ct);

                            Tenant after = await db.Tenants.ReadAsync(tenant.Id, ct) ?? throw new Exception("tenant gone");
                            if (String.IsNullOrEmpty(after.LiteGraphTenantGuid)) throw new Exception("LiteGraph tenant GUID was not recorded");
                            if (after.LiteGraphGraphGuid != "graph-" + after.LiteGraphTenantGuid) throw new Exception("LiteGraph graph GUID was not recorded from the admin result");
                            if (admin.ProvisionedTenantGuids.Count != 1 || admin.ProvisionedTenantGuids[0] != after.LiteGraphTenantGuid) throw new Exception("admin was not asked to provision the recorded tenant GUID");

                            // Idempotent: a second pass leaves the record unchanged and does not re-provision.
                            await provisioner.ProvisionAsync(tenant.Id, tenant.Name, ct);
                            if (admin.ProvisionedTenantGuids.Count != 1) throw new Exception("provisioning must be idempotent; admin was called again");
                        }),

                    new TestCaseDescriptor("GraphTenancy", "Factory_RoutesProvisionedTenantsToOwnGraph", "The graph factory returns a dedicated client for provisioned tenants and the default for others",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeLiteGraphClient defaultGraph = new FakeLiteGraphClient();
                            LiteGraphRepositoryFactory factory = new LiteGraphRepositoryFactory("http://127.0.0.1:8701", null, 1000, 8, 0, 0, db, defaultGraph);

                            Tenant unprovisioned = await db.Tenants.CreateAsync(new Tenant { Name = "Unprovisioned" }, ct);
                            IGraphRepository forUnprovisioned = await factory.ForTenantAsync(unprovisioned.Id, ct);
                            if (!ReferenceEquals(forUnprovisioned, defaultGraph)) throw new Exception("an unprovisioned tenant must fall back to the default graph");

                            Tenant provisioned = await db.Tenants.CreateAsync(new Tenant { Name = "Provisioned", LiteGraphTenantGuid = Guid.NewGuid().ToString(), LiteGraphGraphGuid = Guid.NewGuid().ToString() }, ct);
                            IGraphRepository forProvisioned = await factory.ForTenantAsync(provisioned.Id, ct);
                            if (ReferenceEquals(forProvisioned, defaultGraph)) throw new Exception("a provisioned tenant must route to its own graph, not the default");

                            IGraphRepository forBlank = await factory.ForTenantAsync(String.Empty, ct);
                            if (!ReferenceEquals(forBlank, defaultGraph)) throw new Exception("a blank tenant id must fall back to the default graph");
                        })
                });
        }
    }
}
