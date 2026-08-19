namespace Pneuma.Sdk.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Response listing the Partio endpoints available for ingestion, grouped by usage.
    /// </summary>
    public class IngestionEndpointsResponse
    {
        /// <summary>Embedding endpoints available for ingestion.</summary>
        public List<PartioEndpoint> Embedding { get; set; } = new List<PartioEndpoint>();

        /// <summary>Completion endpoints available for ingestion.</summary>
        public List<PartioEndpoint> Completion { get; set; } = new List<PartioEndpoint>();
    }
}
