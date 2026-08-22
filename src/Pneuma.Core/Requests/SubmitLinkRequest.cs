namespace Pneuma.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Request to submit a content link for ingestion. Optional <see cref="Labels"/> and <see cref="Tags"/>
    /// are attached to every chunk this link produces (in RecallDB) and to the link's source graph node (in
    /// LiteGraph), so retrieval can later be scoped to them via a <see cref="RetrievalFilter"/>.
    /// </summary>
    public class SubmitLinkRequest
    {
        /// <summary>The content URL to ingest.</summary>
        public string Url { get; set; } = String.Empty;

        /// <summary>Optional operator-facing title.</summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Optional labels (plain strings) attached to every chunk and to the link's source graph node. Each
        /// becomes a filterable retrieval label (matched by <see cref="RetrievalFilter.RequiredLabels"/> /
        /// <see cref="RetrievalFilter.ExcludedLabels"/>).
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Optional key/value tags attached to every chunk and to the link's source graph node. Each becomes a
        /// filterable retrieval tag (matched by <see cref="RetrievalFilter.RequiredTags"/> /
        /// <see cref="RetrievalFilter.ExcludedTags"/>).
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
    }
}
