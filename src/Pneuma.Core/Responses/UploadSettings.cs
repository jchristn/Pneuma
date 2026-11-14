namespace Pneuma.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Limits and advisory file types for the file upload API (<c>GET /v1.0/ingestion/upload-settings</c>), so clients can
    /// refuse a file before sending it. DocumentAtom's type detection on the server remains authoritative.
    /// </summary>
    public class UploadSettings
    {
        #region Public-Members

        /// <summary>Largest accepted file, in bytes (<c>Ingestion.MaxUploadBytes</c>).</summary>
        public long MaxUploadBytes { get; set; } = 0;

        /// <summary>Longest accepted file name, in characters.</summary>
        public int MaxFileNameLength { get; set; } = 0;

        /// <summary>Whether the server checks each file's type with DocumentAtom before accepting it.</summary>
        public bool DetectsTypeOnUpload { get; set; } = false;

        /// <summary>Document types expected to extract, with their extensions and MIME types.</summary>
        public List<UploadSupportedType> SupportedTypes { get; set; } = new List<UploadSupportedType>();

        #endregion
    }
}
