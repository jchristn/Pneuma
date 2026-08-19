namespace Test.Shared.Support
{
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Server.Services;

    /// <summary>
    /// Test graph-repository factory that returns a single in-memory graph for every tenant. Sufficient for
    /// tests that do not exercise per-tenant LiteGraph routing; isolation-specific tests supply their own
    /// per-tenant behavior.
    /// </summary>
    public class FakeGraphRepositoryFactory : IGraphRepositoryFactory
    {
        private readonly IGraphRepository _Graph;

        /// <summary>Instantiate with the graph returned for all tenants.</summary>
        /// <param name="graph">The graph repository.</param>
        public FakeGraphRepositoryFactory(IGraphRepository graph)
        {
            _Graph = graph;
        }

        /// <inheritdoc />
        public Task<IGraphRepository> ForTenantAsync(string tenantId, CancellationToken token = default)
        {
            return Task.FromResult(_Graph);
        }
    }
}
