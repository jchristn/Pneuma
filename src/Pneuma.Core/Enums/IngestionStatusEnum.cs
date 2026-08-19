namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Lifecycle status of an ingestion job.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum IngestionStatusEnum
    {
        /// <summary>Waiting to be claimed by a worker.</summary>
        Queued,
        /// <summary>Actively processing one of the pipeline stages.</summary>
        Processing,
        /// <summary>All stages completed successfully.</summary>
        Completed,
        /// <summary>A stage failed; see the recorded stage and error.</summary>
        Failed,
        /// <summary>Cancelled by an operator.</summary>
        Cancelled
    }
}
