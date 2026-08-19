namespace Pneuma.Core.Integrations.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One chunk document to store in the retrieval collection: its content, embedding, position within the
    /// source, and tags. <see cref="DocumentKey"/> uniquely identifies the chunk (upsert key);
    /// <see cref="DocumentId"/> groups all chunks of one source (the link id) for neighbor windows and
    /// delete-by-source.
    /// </summary>
    public class ChunkDocument
    {
        /// <summary>Stable unique key for this chunk (e.g. the chunk graph-node id).</summary>
        public string DocumentKey { get; set; } = String.Empty;

        /// <summary>Logical document id grouping a source's chunks (the link id).</summary>
        public string DocumentId { get; set; } = String.Empty;

        /// <summary>Zero-based chunk position within the logical document.</summary>
        public int Position { get; set; } = 0;

        /// <summary>Chunk text.</summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>Embedding vector; length must match the collection's dimensionality.</summary>
        public List<float> Embedding { get; set; } = new List<float>();

        /// <summary>Tags stored with the chunk (includes <c>litegraphNodeId</c>, <c>linkId</c>, <c>tenantId</c>, <c>subjectId</c>, <c>jobId</c>).</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
    }
}
