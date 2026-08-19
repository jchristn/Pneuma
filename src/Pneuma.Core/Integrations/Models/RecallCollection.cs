namespace Pneuma.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// A vector collection managed in the retrieval store (RecallDB). A collection has a fixed embedding
    /// dimensionality set at creation and holds the chunk documents ingested against it. Pneuma proxies
    /// collection administration to the store and stores no local collection state of its own.
    /// </summary>
    public class RecallCollection
    {
        /// <summary>Collection identifier (store-assigned when omitted on create).</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Human-readable collection name (unique within the tenant).</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Optional description.</summary>
        public string? Description { get; set; } = null;

        /// <summary>Embedding dimensionality; fixed at creation and must match the embedding model used to ingest.</summary>
        public int Dimensionality { get; set; } = 768;

        /// <summary>Whether the collection is active.</summary>
        public bool Active { get; set; } = true;
    }
}
