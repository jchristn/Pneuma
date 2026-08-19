namespace Pneuma.Server.Settings
{
    /// <summary>
    /// RecallDB integration settings. RecallDB is the retrieval store: it holds chunk documents with their
    /// embeddings and provides vector, full-text, and hybrid search. Pneuma operates under a single RecallDB
    /// tenant (ensured at startup) and manages collections on the operator's behalf.
    /// </summary>
    public class RecallDbSettings
    {
        /// <summary>Base URL of the RecallDB server.</summary>
        public string Endpoint { get; set; } = "http://127.0.0.1:8600/";

        /// <summary>Bearer token for RecallDB (admin API key or a tenant credential token).</summary>
        public string? BearerToken { get; set; } = "recalldbadmin";

        /// <summary>
        /// Fallback RecallDB tenant used when a caller supplies none. Each Pneuma tenant is provisioned as a
        /// RecallDB tenant of the same id, so this is only the default/system tenant ensured at startup.
        /// </summary>
        public string TenantId { get; set; } = "pneuma";

        /// <summary>Display name for the fallback/system tenant when it is created in RecallDB.</summary>
        public string TenantName { get; set; } = "Pneuma";

        /// <summary>Name of the default collection created for each tenant at provisioning time.</summary>
        public string DefaultCollectionName { get; set; } = "default";

        /// <summary>
        /// Embedding dimensionality of the default collection created per tenant. Fixed at creation and must
        /// match the embedding model used to ingest into it. RecallDB's own default is 384.
        /// </summary>
        public int DefaultCollectionDimensionality { get; set; } = 384;
    }
}
