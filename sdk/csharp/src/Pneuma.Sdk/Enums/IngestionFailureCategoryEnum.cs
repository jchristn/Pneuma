namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Why an ingestion job (or one attempt of it) failed. Each category has a retry policy and remediation text,
    /// so operators can filter failures by cause and the server decides
    /// whether a retry could succeed.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum IngestionFailureCategoryEnum
    {
        /// <summary>The source could not be fetched (an HTTP error from the source, DNS failure, or refused connection). Retried.</summary>
        Fetch,
        /// <summary>The fetch was refused by the fetch-safety policy (a private address or a disallowed scheme). Not retried.</summary>
        Blocked,
        /// <summary>The source exceeded a size limit. Not retried.</summary>
        TooLarge,
        /// <summary>The content type is unknown or has no extractor. Not retried.</summary>
        UnsupportedType,
        /// <summary>Content extraction (DocumentAtom) failed. Retried.</summary>
        Extraction,
        /// <summary>Extraction succeeded but produced no content. Not retried.</summary>
        NoContent,
        /// <summary>A model endpoint was unavailable or rate limited after its retries. Retried after a longer delay.</summary>
        ModelUnavailable,
        /// <summary>A model endpoint rejected the request (a 4xx, including a context-length error). Not retried.</summary>
        ModelRejected,
        /// <summary>The subject or platform is missing required configuration (a model, an endpoint, or a collection). Not retried.</summary>
        Configuration,
        /// <summary>A storage dependency (RecallDB, LiteGraph, or the blob store) failed. Retried.</summary>
        Storage,
        /// <summary>A stage or request timed out. Retried.</summary>
        Timeout,
        /// <summary>Work was lost (dropped batches, cells, or chunks) and the partial-loss policy is Fail. Retried.</summary>
        PartialLoss,
        /// <summary>The worker running the job stopped before it finished. Retried.</summary>
        WorkerLost,
        /// <summary>An operator stopped the job. Not retried.</summary>
        Cancelled,
        /// <summary>An unexpected internal error. Retried.</summary>
        Internal
    }
}
