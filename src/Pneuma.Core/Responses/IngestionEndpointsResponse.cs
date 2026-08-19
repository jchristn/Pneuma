namespace Pneuma.Core.Responses
{
    using System.Collections.Generic;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// The available embedding and completion model endpoints exposed to the dashboards.
    /// </summary>
    public class IngestionEndpointsResponse
    {
        /// <summary>The available embedding endpoints.</summary>
        public List<PartioEndpoint> Embedding { get; set; } = new List<PartioEndpoint>();

        /// <summary>The available completion endpoints.</summary>
        public List<PartioEndpoint> Completion { get; set; } = new List<PartioEndpoint>();
    }
}
