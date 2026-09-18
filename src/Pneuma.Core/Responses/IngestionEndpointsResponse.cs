namespace Pneuma.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// The available embedding and completion model endpoints exposed to the dashboards.
    /// </summary>
    public class IngestionEndpointsResponse
    {
        /// <summary>The available embedding endpoints.</summary>
        public List<ModelEndpointDto> Embedding { get; set; } = new List<ModelEndpointDto>();

        /// <summary>The available completion endpoints.</summary>
        public List<ModelEndpointDto> Completion { get; set; } = new List<ModelEndpointDto>();
    }
}
