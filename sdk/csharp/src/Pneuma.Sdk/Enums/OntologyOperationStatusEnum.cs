namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>State of an operation.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OntologyOperationStatusEnum
    {
        /// <summary>Waiting.</summary>
        Queued,
        /// <summary>Running.</summary>
        Running,
        /// <summary>Finished.</summary>
        Succeeded,
        /// <summary>Failed.</summary>
        Failed
    }
}
