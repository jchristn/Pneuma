namespace Pneuma.Sdk.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Content pushed to a subject for ingestion. Reusing <see cref="ExternalKey"/> replaces earlier content with the
    /// same key instead of adding a duplicate.
    /// </summary>
    public class SubmitContentRequest
    {
        /// <summary>Optional title, also used as the document title in chunk headers.</summary>
        public string? Title { get; set; } = null;

        /// <summary>The content text. Required.</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>text/plain, text/markdown (default), text/html, or application/json.</summary>
        public string ContentType { get; set; } = "text/markdown";

        /// <summary>Optional stable key; at most 256 characters.</summary>
        public string? ExternalKey { get; set; } = null;

        /// <summary>Optional labels attached to every chunk.</summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>Optional key/value tags attached to every chunk.</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
    }
}
