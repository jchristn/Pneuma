namespace Pneuma.Sdk.Responses
{
    using System;

    /// <summary>A link's original content as downloaded from <c>GET /v1.0/links/{id}/source</c>.</summary>
    public class LinkSourceFile
    {
        /// <summary>The bytes.</summary>
        public byte[] Data { get; set; } = Array.Empty<byte>();

        /// <summary>The content type the server sent.</summary>
        public string ContentType { get; set; } = "application/octet-stream";

        /// <summary>The file name from the Content-Disposition header, or null when none was sent.</summary>
        public string? FileName { get; set; } = null;
    }
}
