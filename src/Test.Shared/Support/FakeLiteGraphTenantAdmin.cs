namespace Test.Shared.Support
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Server.Services;

    /// <summary>
    /// In-memory <see cref="ILiteGraphTenantAdmin"/> for tests: records the LiteGraph tenant GUIDs it was
    /// asked to provision and returns a deterministic graph GUID per tenant, without any HTTP.
    /// </summary>
    public class FakeLiteGraphTenantAdmin : ILiteGraphTenantAdmin
    {
        private readonly object _Lock = new object();
        private readonly Dictionary<string, string> _GraphByTenantGuid = new Dictionary<string, string>();

        /// <summary>The LiteGraph tenant GUIDs this admin was asked to provision.</summary>
        public List<string> ProvisionedTenantGuids { get; } = new List<string>();

        /// <inheritdoc />
        public Task<string?> ProvisionAsync(string tenantGuid, string name, CancellationToken token = default)
        {
            lock (_Lock)
            {
                ProvisionedTenantGuids.Add(tenantGuid);
                if (!_GraphByTenantGuid.TryGetValue(tenantGuid, out string? graphGuid))
                {
                    graphGuid = "graph-" + tenantGuid;
                    _GraphByTenantGuid[tenantGuid] = graphGuid;
                }
                return Task.FromResult<string?>(graphGuid);
            }
        }
    }
}
