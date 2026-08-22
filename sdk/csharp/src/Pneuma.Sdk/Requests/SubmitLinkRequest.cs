namespace Pneuma.Sdk.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Request to submit a content link for ingestion. Optional <see cref="Labels"/> and <see cref="Tags"/>
    /// are attached to every chunk this link produces and to the link's source graph node, so retrieval can
    /// later be scoped to them via a <see cref="RetrievalFilter"/>.
    /// </summary>
    public class SubmitLinkRequest
    {
        /// <summary>The content URL to ingest.</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>Optional operator-facing title. The models and collection used for ingestion are taken from
        /// the subject, so no model selection is supplied here.</summary>
        public string? Title { get; set; } = null;

        /// <summary>Optional labels (plain strings) attached to every chunk and to the link's source graph node.</summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>Optional key/value tags attached to every chunk and to the link's source graph node.</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
    }
}
