namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// Provisions a tenant's retrieval resources in RecallDB: the RecallDB tenant (id reused from the Pneuma
    /// tenant id) and a default collection. RecallDB is the authority for both; this provisioner relays the
    /// ensure/create calls through <see cref="ICollectionStore"/>. Idempotent — the tenant ensure checks
    /// existence first, and the default collection is created only when the tenant has no collection with the
    /// configured default name.
    /// </summary>
    public class RecallDbTenantProvisioner : ITenantProvisioner
    {
        #region Private-Members

        private readonly ICollectionStore _Collections;
        private readonly string _DefaultCollectionName;
        private readonly int _DefaultCollectionDimensionality;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the RecallDB provisioner.</summary>
        /// <param name="collections">Collection store (RecallDB) used to ensure the tenant and its default collection.</param>
        /// <param name="defaultCollectionName">Name of the default collection created per tenant.</param>
        /// <param name="defaultCollectionDimensionality">Embedding dimensionality of the default collection (>= 1).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="collections"/> is null.</exception>
        public RecallDbTenantProvisioner(ICollectionStore collections, string defaultCollectionName, int defaultCollectionDimensionality)
        {
            _Collections = collections ?? throw new ArgumentNullException(nameof(collections));
            _DefaultCollectionName = String.IsNullOrWhiteSpace(defaultCollectionName) ? "default" : defaultCollectionName;
            _DefaultCollectionDimensionality = Math.Max(1, defaultCollectionDimensionality);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public string Name => "recalldb";

        /// <inheritdoc />
        public async Task ProvisionAsync(string tenantId, string tenantName, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) return;

            // Ensure the RecallDB tenant exists, then ensure it has a default collection (idempotent by name).
            await _Collections.EnsureTenantAsync(tenantId, tenantName, token).ConfigureAwait(false);

            List<RecallCollection> existing = await _Collections.ListCollectionsAsync(tenantId, token).ConfigureAwait(false);
            foreach (RecallCollection collection in existing)
            {
                if (String.Equals(collection.Name, _DefaultCollectionName, StringComparison.OrdinalIgnoreCase)) return;
            }

            RecallCollection defaultCollection = new RecallCollection
            {
                Name = _DefaultCollectionName,
                Description = "Default collection",
                Dimensionality = _DefaultCollectionDimensionality,
                Active = true
            };
            await _Collections.CreateCollectionAsync(tenantId, defaultCollection, token).ConfigureAwait(false);
        }

        #endregion
    }
}
