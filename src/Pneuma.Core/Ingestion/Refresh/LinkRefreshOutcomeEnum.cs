namespace Pneuma.Core.Ingestion.Refresh
{
    using System.Text.Json.Serialization;

    /// <summary>What a refresh check did.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LinkRefreshOutcomeEnum
    {
        /// <summary>The source answered 304 Not Modified; nothing was queued.</summary>
        Unchanged,
        /// <summary>The source changed (or gave no validators); a re-ingest job was queued.</summary>
        Queued,
        /// <summary>The check failed; the link keeps its current version and is retried with back-off.</summary>
        Failed,
        /// <summary>A job for the link is already queued or running; the check was postponed.</summary>
        Busy,
        /// <summary>The link cannot be refreshed (not a URL link, owned by a crawl plan, inactive, or refresh is off).</summary>
        Skipped
    }
}
