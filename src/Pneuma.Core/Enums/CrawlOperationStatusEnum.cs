namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>The lifecycle of one crawl operation (one run of a plan).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CrawlOperationStatusEnum
    {
        /// <summary>Enumerating the source and dispatching changes.</summary>
        Running,
        /// <summary>Changes are dispatched; waiting for their ingestion jobs to finish.</summary>
        Ingesting,
        /// <summary>Every change ingested.</summary>
        Succeeded,
        /// <summary>Some changes failed to ingest; they are retried on the next run.</summary>
        PartiallySucceeded,
        /// <summary>The run could not enumerate or dispatch.</summary>
        Failed,
        /// <summary>An operator stopped the run.</summary>
        Cancelled,
        /// <summary>The run would delete more than the plan allows and waits for confirmation.</summary>
        Held
    }
}
