namespace Pneuma.Sdk.Responses
{
    using System.Collections.Generic;

    /// <summary>The result of testing a crawl plan's connection.</summary>
    public class ConnectivityResult
    {
        /// <summary>True when every step passed.</summary>
        public bool Success { get; set; } = false;

        /// <summary>The steps, in order.</summary>
        public List<ConnectivityLayer> Layers { get; set; } = new List<ConnectivityLayer>();
    }
}
