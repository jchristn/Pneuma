namespace Pneuma.Server.Settings
{
    /// <summary>
    /// Partio integration settings (chunking, embedding, summarization).
    /// </summary>
    public class PartioSettings
    {
        /// <summary>Base URL of the Partio server.</summary>
        public string Endpoint { get; set; } = "http://127.0.0.1:8400/";

        /// <summary>Bearer token for Partio (admin key or tenant credential token).</summary>
        public string? BearerToken { get; set; } = "partioadmin";

        /// <summary>
        /// Partio tenant that Pneuma-managed endpoints are created and enumerated under. Partio scopes
        /// endpoint enumeration by tenant, so created endpoints must carry this tenant to be visible.
        /// </summary>
        public string? TenantId { get; set; } = "default";

        /// <summary>Partio embedding endpoint identifier (eep_...). Resolved at startup when null.</summary>
        public string? EmbeddingEndpointId { get; set; } = null;

        /// <summary>Partio completion endpoint identifier (cep_...). Resolved at startup when null.</summary>
        public string? CompletionEndpointId { get; set; } = null;
    }
}
