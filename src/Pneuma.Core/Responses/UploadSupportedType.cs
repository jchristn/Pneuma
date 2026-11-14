namespace Pneuma.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>A document type the file upload API expects DocumentAtom to extract, with its usual extensions and MIME types.</summary>
    public class UploadSupportedType
    {
        #region Public-Members

        /// <summary>The DocumentAtom document type, for example <c>Pdf</c>.</summary>
        public string DocumentType { get; set; } = String.Empty;

        /// <summary>File name extensions, lower case and without the dot.</summary>
        public List<string> Extensions { get; set; } = new List<string>();

        /// <summary>MIME types.</summary>
        public List<string> MimeTypes { get; set; } = new List<string>();

        #endregion
    }
}
