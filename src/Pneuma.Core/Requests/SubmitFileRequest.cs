namespace Pneuma.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Metadata for a file uploaded to a subject for ingestion (<c>POST /v1.0/subjects/{id}/files</c>). The file bytes
    /// travel as the request body; these values come from the query string and the <c>Content-Type</c> header.
    /// </summary>
    public class SubmitFileRequest
    {
        #region Public-Members

        /// <summary>The original file name, for example <c>report.pdf</c>. Required; at most 256 characters.</summary>
        public string? FileName { get; set; } = null;

        /// <summary>
        /// The file's content type. Null, empty, or <c>application/octet-stream</c> means the type is inferred from the
        /// file name extension.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>Title shown for the file and used as its chunk-header title. Defaults to the file name.</summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Optional key identifying the file within the subject. A second upload with the same key replaces that file
        /// instead of adding a duplicate. At most 256 characters.
        /// </summary>
        public string? ExternalKey { get; set; } = null;

        /// <summary>Labels stamped onto every chunk.</summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>Key/value tags stamped onto every chunk.</summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
