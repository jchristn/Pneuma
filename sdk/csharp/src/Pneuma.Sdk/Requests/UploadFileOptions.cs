namespace Pneuma.Sdk.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Optional metadata for a file uploaded with <see cref="PneumaClient.UploadFileAsync(string, System.IO.Stream, string, UploadFileOptions?, System.Threading.CancellationToken)"/>.
    /// Reusing <see cref="ExternalKey"/> replaces the earlier file with that key instead of adding a duplicate.
    /// </summary>
    public class UploadFileOptions
    {
        /// <summary>Display title; the server uses the file name when null.</summary>
        public string? Title { get; set; } = null;

        /// <summary>Upsert key within the subject (at most 256 characters), or null.</summary>
        public string? ExternalKey { get; set; } = null;

        /// <summary>
        /// The file's MIME type. Null sends <c>application/octet-stream</c>, and the server infers the type from the
        /// file name extension.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>Labels stamped onto every chunk the file produces.</summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>Key/value tags stamped onto every chunk the file produces.</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
    }
}
