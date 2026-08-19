namespace Pneuma.Core.Requests
{
    using System;

    /// <summary>
    /// Request to create a vector collection in the retrieval store (RecallDB). The dimensionality is fixed
    /// at creation and must match the embedding model used when ingesting into the collection.
    /// </summary>
    public class CreateCollectionRequest
    {
        /// <summary>Human-readable collection name (unique within the tenant).</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Optional description.</summary>
        public string? Description { get; set; } = null;

        /// <summary>Embedding dimensionality; fixed at creation. Defaults to 768.</summary>
        public int Dimensionality { get; set; } = 768;
    }
}
