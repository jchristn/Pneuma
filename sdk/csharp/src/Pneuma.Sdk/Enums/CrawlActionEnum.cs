namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>What a crawl operation did with one object.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CrawlActionEnum
    {
        /// <summary>A new object: a link and a job were created.</summary>
        Add,
        /// <summary>A changed object: its link was re-ingested.</summary>
        Update,
        /// <summary>An object that failed last time: its link was re-ingested.</summary>
        Retry,
        /// <summary>An object gone from the source: its link was deleted.</summary>
        Delete,
        /// <summary>Filtered out, or over the object limit.</summary>
        Skip,
        /// <summary>The object could not be dispatched.</summary>
        Fail,
        /// <summary>Unchanged since the last run (counted, not recorded per object).</summary>
        Unchanged,
        /// <summary>Gone from the source and kept because deletions are off (counted, not recorded per object).</summary>
        Missing
    }
}
