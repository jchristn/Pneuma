namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>The state of a background ontology operation.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OntologyOperationStatusEnum
    {
        /// <summary>Waiting for a worker.</summary>
        Queued,
        /// <summary>A worker is processing it.</summary>
        Running,
        /// <summary>Finished successfully.</summary>
        Succeeded,
        /// <summary>Finished with an error.</summary>
        Failed
    }
}
