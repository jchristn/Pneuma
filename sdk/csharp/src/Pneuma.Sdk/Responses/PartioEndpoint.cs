namespace Pneuma.Sdk.Responses
{
    /// <summary>
    /// A Partio embedding or completion endpoint available for ingestion.
    /// </summary>
    public class PartioEndpoint
    {
        /// <summary>Endpoint identifier (prefix "eep_" for embedding, "cep_" for completion).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Operator-facing name of the endpoint.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Model identifier the endpoint targets.</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>API format the endpoint speaks (for example "OpenAI", "Ollama").</summary>
        public string ApiFormat { get; set; } = string.Empty;

        /// <summary>Whether the endpoint is active and available for selection.</summary>
        public bool Active { get; set; } = true;
    }
}
