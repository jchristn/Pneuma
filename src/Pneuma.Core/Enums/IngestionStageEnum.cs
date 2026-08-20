namespace Pneuma.Core.Enums
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
        /// <summary>Fetch of the source content from the link (HTTP / headless browser).</summary>
        ContentRetrieval,
        /// <summary>DocumentAtom type detection.</summary>
        TypeDetection,
        /// <summary>DocumentAtom semantic cell extraction.</summary>
        CellExtraction,
        /// <summary>PolyPrompt ontology classification into a candidate subgraph.</summary>
        Classification,
        /// <summary>
        /// End of the categorization phase: a candidate plan (proposed subgraph) has been produced and,
        /// under auto-approval, is committed by the hydration phase.
        /// </summary>
        Categorization,
        /// <summary>Start of the hydration phase: the approved candidate plan is committed to the graph and index.</summary>
        Hydration,
        /// <summary>LiteGraph subgraph merge.</summary>
        GraphMerge,
        /// <summary>Partio summarization of the extracted cells.</summary>
        Summarization,
        /// <summary>Partio chunking of the cell (and summary) text.</summary>
        Chunking,
        /// <summary>Partio embedding of the produced chunks.</summary>
        Embedding,
        /// <summary>RecallDB chunk-document storage.</summary>
        Indexing,
        /// <summary>Terminal stage after successful indexing.</summary>
        Done
    }
}
