namespace Pneuma.Server.Settings
{
    /// <summary>
    /// Names of the object-storage buckets: one per ingestion artifact stage (keyed by the originating link id), and the
    /// blob store's bucket.
    /// </summary>
    public class S3BucketSettings
    {
        #region Public-Members

        /// <summary>Bucket for the raw source asset retrieved when a link is crawled.</summary>
        public string Source { get; set; } = "pneuma-source";

        /// <summary>Bucket for atomized documents (DocumentAtom semantic cells).</summary>
        public string Atoms { get; set; } = "pneuma-atoms";

        /// <summary>Bucket for chunked documents.</summary>
        public string Chunks { get; set; } = "pneuma-chunks";

        /// <summary>Bucket for embeddings documents.</summary>
        public string Embeddings { get; set; } = "pneuma-embeddings";

        /// <summary>Bucket for candidate-subgraph JSON documents (model classification output).</summary>
        public string Subgraph { get; set; } = "pneuma-subgraph";

        /// <summary>
        /// Bucket for the blob store when <c>Integrations.Blob.Provider</c> is <c>S3</c>: uploaded files, pushed text, each
        /// job's copy of its source, and the classification cache.
        /// </summary>
        public string Blobs { get; set; } = "pneuma-blobs";

        #endregion
    }
}
