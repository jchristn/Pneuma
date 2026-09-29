namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>What started a crawl operation.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CrawlTriggerEnum
    {
        /// <summary>The plan's schedule.</summary>
        Schedule,
        /// <summary>An operator, through the API or dashboard.</summary>
        Manual
    }
}
