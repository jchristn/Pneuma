namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Outcome of checking a link for changes.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LinkRefreshOutcomeEnum
    {
        /// <summary>The source has not changed; nothing was re-ingested.</summary>
        Unchanged,
        /// <summary>The source changed; a re-ingest was queued.</summary>
        Queued,
        /// <summary>The check failed; the current version is kept and the check is retried with back-off.</summary>
        Failed,
        /// <summary>The link already has an ingestion in progress.</summary>
        Busy,
        /// <summary>The link is not refreshed on a schedule.</summary>
        Skipped
    }
}
