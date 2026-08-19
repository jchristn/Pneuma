namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Processing status of an subject-submitted content link.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SubjectLinkStatusEnum
    {
        /// <summary>Submitted; an ingestion job has been queued.</summary>
        Submitted,
        /// <summary>Ingestion is in progress.</summary>
        Processing,
        /// <summary>Ingestion completed successfully.</summary>
        Ingested,
        /// <summary>Ingestion failed; see the last error.</summary>
        Failed
    }
}
