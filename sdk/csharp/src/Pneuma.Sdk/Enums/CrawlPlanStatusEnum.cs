namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>Whether a crawl plan has an operation in progress.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CrawlPlanStatusEnum
    {
        /// <summary>No operation is running.</summary>
        Idle,
        /// <summary>An operation is enumerating or dispatching.</summary>
        Running,
        /// <summary>An operator asked the running operation to stop.</summary>
        Stopping
    }
}
