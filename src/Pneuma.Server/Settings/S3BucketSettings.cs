namespace Pneuma.Server.Settings
{
    /// <summary>
    /// Names of the per-stage object-storage buckets. Each ingestion artifact is written to its own
    /// bucket under a key that references the originating link id.
    /// </summary>
    public class S3BucketSettings
    {
        #region Public-Members

        /// <summary>Bucket for the raw source asset retrieved when a link is crawled.</summary>
        public string Source { get; set; } = "pneuma-source";

        /// <summary>Bucket for atomized documents (DocumentAtom semantic cells).</summary>
        public string Atoms { get; set; } = "pneuma-atoms";

        /// <summary>Bucket for chunked documents (Partio).</summary>
        public string Chunks { get; set; } = "pneuma-chunks";

        /// <summary>Bucket for embeddings documents (Partio).</summary>
        public string Embeddings { get; set; } = "pneuma-embeddings";

        /// <summary>Bucket for candidate-subgraph JSON documents (model classification output).</summary>
        public string Subgraph { get; set; } = "pneuma-subgraph";

        #endregion
    }
}
