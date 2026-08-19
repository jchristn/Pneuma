namespace Pneuma.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// DocumentAtom type-detection result. A Type of "Unknown" means ingestion should fail.
    /// </summary>
    public class TypeDetectResult
    {
        /// <summary>Detected document type (for example Pdf, Docx, Text, Unknown).</summary>
        public string Type { get; set; } = "Unknown";

        /// <summary>Detected MIME type.</summary>
        public string MimeType { get; set; } = String.Empty;

        /// <summary>Detected file extension.</summary>
        public string Extension { get; set; } = String.Empty;

        /// <summary>Whether the type is unknown/unsupported.</summary>
        public bool IsUnknown
        {
            get { return String.IsNullOrWhiteSpace(Type) || String.Equals(Type, "Unknown", StringComparison.OrdinalIgnoreCase); }
        }
    }
}
