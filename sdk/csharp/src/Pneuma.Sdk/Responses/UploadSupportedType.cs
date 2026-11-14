namespace Pneuma.Sdk.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>A document type the server expects to extract from uploads, with its extensions and MIME types.</summary>
    public class UploadSupportedType
    {
        /// <summary>DocumentAtom document type, for example <c>Pdf</c>.</summary>
        public string DocumentType { get; set; } = string.Empty;

        /// <summary>File name extensions, lower case, without the dot.</summary>
        public List<string> Extensions { get; set; } = new List<string>();

        /// <summary>MIME types.</summary>
        public List<string> MimeTypes { get; set; } = new List<string>();
    }
}
