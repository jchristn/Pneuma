namespace Pneuma.Core.Integrations.Abstractions
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// Provider-neutral administration of tenants and their vector collections in the retrieval store
    /// (RecallDB). RecallDB is the authority for tenants and collections; Pneuma proxies management to it
    /// per-tenant rather than keeping local state, mirroring how model endpoints are proxied to Partio.
    /// Every operation is scoped to a RecallDB tenant (the Pneuma tenant id is used as the RecallDB tenant id).
    /// </summary>
    public interface ICollectionStore
    {
        /// <summary>
        /// Ensure a tenant exists in the retrieval store (idempotent — existence is checked first, so this is
        /// safe to call on every provisioning pass). RecallDB seeds only its own "default" tenant, so each
        /// Pneuma tenant must be created here before its collections can be managed.
        /// </summary>
        /// <param name="tenantId">Tenant id to ensure (the Pneuma tenant id is reused as the RecallDB tenant id).</param>
        /// <param name="tenantName">Display name used when the tenant is created.</param>
        /// <param name="token">Cancellation token.</param>
        Task EnsureTenantAsync(string tenantId, string tenantName, CancellationToken token = default);

        /// <summary>List the collections in a tenant.</summary>
        /// <param name="tenantId">Tenant that owns the collections.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The collections.</returns>
        Task<List<RecallCollection>> ListCollectionsAsync(string tenantId, CancellationToken token = default);

        /// <summary>Read one collection by id; null when not found.</summary>
        /// <param name="tenantId">Tenant that owns the collection.</param>
        /// <param name="id">Collection id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The collection, or null.</returns>
        Task<RecallCollection?> ReadCollectionAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Create a collection (with its fixed dimensionality) in a tenant.</summary>
        /// <param name="tenantId">Tenant to create the collection in.</param>
        /// <param name="collection">Collection to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created collection (including its store-assigned id).</returns>
        Task<RecallCollection> CreateCollectionAsync(string tenantId, RecallCollection collection, CancellationToken token = default);

        /// <summary>Delete a collection and all of its documents. Returns true when a collection was deleted.</summary>
        /// <param name="tenantId">Tenant that owns the collection.</param>
        /// <param name="id">Collection id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> DeleteCollectionAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Whether a collection with the given id exists in a tenant.</summary>
        /// <param name="tenantId">Tenant that owns the collection.</param>
        /// <param name="id">Collection id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when it exists.</returns>
        Task<bool> CollectionExistsAsync(string tenantId, string id, CancellationToken token = default);
    }
}
