namespace Pneuma.Sdk.Responses
{
    /// <summary>One step of a crawl plan connectivity test.</summary>
    public class ConnectivityLayer
    {
        /// <summary>Step name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>True when the step passed.</summary>
        public bool Success { get; set; } = false;

        /// <summary>What was checked and what to fix.</summary>
        public string Message { get; set; } = string.Empty;
    }
}
