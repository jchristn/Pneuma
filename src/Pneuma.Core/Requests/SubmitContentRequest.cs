namespace Pneuma.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Content pushed to a subject for ingestion (<c>POST /v1.0/subjects/{id}/content</c>). The content is stored and
    /// ingested like a link's; an <see cref="ExternalKey"/> makes the call an upsert.
    /// </summary>
    public class SubmitContentRequest
    {
        #region Public-Members

        /// <summary>Title shown for the content and used as its chunk-header title.</summary>
        public string? Title { get; set; } = null;

        /// <summary>The content text. Required.</summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// Content type: <c>text/plain</c>, <c>text/markdown</c>, <c>text/html</c>, or <c>application/json</c>. Required; it
        /// replaces type detection.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// Optional key identifying the content within the subject. A second push with the same key replaces that
        /// content instead of adding a duplicate. At most 256 characters.
        /// </summary>
        public string? ExternalKey { get; set; } = null;

        /// <summary>Labels stamped onto every chunk.</summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>Key/value tags stamped onto every chunk.</summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
