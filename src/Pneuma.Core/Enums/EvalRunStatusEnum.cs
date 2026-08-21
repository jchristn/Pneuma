namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The lifecycle status of a RAG evaluation run.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EvalRunStatusEnum
    {
        /// <summary>Created but not yet started.</summary>
        Pending,
        /// <summary>Currently executing facts.</summary>
        Running,
        /// <summary>Finished; all facts were judged.</summary>
        Completed,
        /// <summary>Aborted by an error.</summary>
        Failed,
        /// <summary>Cancelled by an operator.</summary>
        Cancelled
    }
}
