namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The pipeline stage an ingestion job is on (or failed at).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum IngestionStageEnum
    {
        /// <summary>Not yet started.</summary>
        Pending,
        /// <summary>Document type detection.</summary>
        TypeDetection,
        /// <summary>Semantic cell extraction.</summary>
        CellExtraction,
        /// <summary>Ontology classification into a candidate subgraph.</summary>
        Classification,
        /// <summary>Knowledge-graph subgraph merge.</summary>
        GraphMerge,
        /// <summary>Summarize, chunk, and embed.</summary>
        Embedding,
        /// <summary>Search indexing.</summary>
        Indexing,
        /// <summary>Terminal stage after successful indexing.</summary>
        Done
    }
}
