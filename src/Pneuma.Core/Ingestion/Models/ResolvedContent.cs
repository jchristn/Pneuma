namespace Pneuma.Core.Ingestion.Models
{
    using System;

    /// <summary>A link's content as retrieved for ingestion.</summary>
    public class ResolvedContent
    {
        #region Public-Members

        /// <summary>The content bytes. Never null.</summary>
        public byte[] Bytes
        {
            get { return _Bytes; }
            set { _Bytes = value ?? Array.Empty<byte>(); }
        }

        /// <summary>
        /// The document type the content was declared as (for example Markdown or Json), which skips type detection;
        /// null when the type must be detected.
        /// </summary>
        public string? DeclaredDocumentType { get; set; } = null;

        #endregion

        #region Private-Members

        private byte[] _Bytes = Array.Empty<byte>();

        #endregion
    }
}
