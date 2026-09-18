namespace Pneuma.Sdk.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Response listing the model endpoints available for ingestion, grouped by usage.
    /// </summary>
    public class IngestionEndpointsResponse
    {
        /// <summary>Embedding endpoints available for ingestion.</summary>
        public List<ModelEndpoint> Embedding { get; set; } = new List<ModelEndpoint>();

        /// <summary>Completion endpoints available for ingestion.</summary>
        public List<ModelEndpoint> Completion { get; set; } = new List<ModelEndpoint>();
    }
}
