namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>When a crawl plan runs on its own.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CrawlScheduleTypeEnum
    {
        /// <summary>Only when started through the API or dashboard.</summary>
        Manual,
        /// <summary>Every IntervalMinutes after the previous run started.</summary>
        Interval,
        /// <summary>On a cron expression in a time zone.</summary>
        Cron
    }
}
