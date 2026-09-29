namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>The state of one object a crawl plan has seen.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CrawlObjectStatusEnum
    {
        /// <summary>Present in the source and ingested (or queued).</summary>
        Active,
        /// <summary>No longer in the source; its link was kept because deletions are off or held.</summary>
        Missing,
        /// <summary>Its last ingestion failed; the next run retries it.</summary>
        Failed,
        /// <summary>Seen but filtered out (pattern, type, or size).</summary>
        Excluded
    }
}
