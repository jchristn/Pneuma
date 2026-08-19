namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Implementations;
    using Pneuma.Core.Models;

    /// <summary>
    /// Builds and caches a <see cref="LiteGraphClient"/> per Pneuma tenant, bound to that tenant's LiteGraph
    /// tenant GUID and graph GUID (recorded on the <see cref="Tenant"/> record during provisioning). A tenant
    /// that has not been provisioned yet (no GUIDs) falls back to the configured default graph client, and is
    /// not cached — so once it is provisioned the next call picks up its dedicated graph. Clients are cached
    /// for the process lifetime, keyed by Pneuma tenant id.
    /// </summary>
    public class LiteGraphRepositoryFactory : IGraphRepositoryFactory
    {
        #region Private-Members

        private readonly string _BaseUrl;
        private readonly string? _BearerToken;
        private readonly int _TimeoutMilliseconds;
        private readonly int _MaxConcurrentRequests;
        private readonly int _RetryCount;
        private readonly int _RetryDelayMilliseconds;
        private readonly DatabaseDriverBase _Db;
        private readonly IGraphRepository _Default;
        private readonly ConcurrentDictionary<string, IGraphRepository> _ClientsByTenantId = new ConcurrentDictionary<string, IGraphRepository>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the factory.</summary>
        /// <param name="baseUrl">LiteGraph base URL.</param>
        /// <param name="bearerToken">Admin bearer token (bypasses LiteGraph tenant scoping).</param>
        /// <param name="timeoutMilliseconds">Per-attempt timeout for built clients.</param>
        /// <param name="maxConcurrentRequests">Max concurrent requests per built client.</param>
        /// <param name="retryCount">Retry count for built clients.</param>
        /// <param name="retryDelayMilliseconds">Retry delay for built clients.</param>
        /// <param name="db">Database driver used to resolve a tenant's LiteGraph GUIDs.</param>
        /// <param name="defaultRepository">Fallback repository (the configured default LiteGraph tenant/graph).</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public LiteGraphRepositoryFactory(
            string baseUrl,
            string? bearerToken,
            int timeoutMilliseconds,
            int maxConcurrentRequests,
            int retryCount,
            int retryDelayMilliseconds,
            DatabaseDriverBase db,
            IGraphRepository defaultRepository)
        {
            if (String.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
            _BaseUrl = baseUrl;
            _BearerToken = bearerToken;
            _TimeoutMilliseconds = timeoutMilliseconds;
            _MaxConcurrentRequests = maxConcurrentRequests;
            _RetryCount = retryCount;
            _RetryDelayMilliseconds = retryDelayMilliseconds;
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Default = defaultRepository ?? throw new ArgumentNullException(nameof(defaultRepository));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<IGraphRepository> ForTenantAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) return _Default;
            if (_ClientsByTenantId.TryGetValue(tenantId, out IGraphRepository? cached)) return cached;

            Tenant? tenant = await _Db.Tenants.ReadAsync(tenantId, token).ConfigureAwait(false);
            if (tenant == null
                || String.IsNullOrWhiteSpace(tenant.LiteGraphTenantGuid)
                || String.IsNullOrWhiteSpace(tenant.LiteGraphGraphGuid))
            {
                // Not provisioned yet (or not a Pneuma tenant): use the default graph, and do not cache so the
                // dedicated per-tenant graph is picked up as soon as provisioning records its GUIDs.
                return _Default;
            }

            IGraphRepository client = new LiteGraphClient(
                _BaseUrl,
                _BearerToken,
                tenant.LiteGraphTenantGuid!,
                tenant.LiteGraphGraphGuid!,
                _TimeoutMilliseconds,
                _MaxConcurrentRequests,
                _RetryCount,
                _RetryDelayMilliseconds);

            return _ClientsByTenantId.GetOrAdd(tenantId, client);
        }

        #endregion
    }
}
