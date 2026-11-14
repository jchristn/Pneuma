namespace Pneuma.Sdk.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>Upload limits and the advisory file types (<c>GET /v1.0/ingestion/upload-settings</c>).</summary>
    public class UploadSettings
    {
        /// <summary>Largest accepted file, in bytes.</summary>
        public long MaxUploadBytes { get; set; } = 0;

        /// <summary>Longest accepted file name, in characters.</summary>
        public int MaxFileNameLength { get; set; } = 0;

        /// <summary>Whether the server checks each file's type before accepting it.</summary>
        public bool DetectsTypeOnUpload { get; set; } = false;

        /// <summary>Types the server expects to extract. Its type detection remains authoritative.</summary>
        public List<UploadSupportedType> SupportedTypes { get; set; } = new List<UploadSupportedType>();
    }
}
