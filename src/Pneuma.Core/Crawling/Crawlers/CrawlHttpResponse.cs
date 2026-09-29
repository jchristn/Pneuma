namespace Pneuma.Core.Crawling.Crawlers
{
    using System;

    /// <summary>A successful HTTP fetch made by a crawler: the body and the headers change detection uses.</summary>
    public class CrawlHttpResponse
    {
        #region Public-Members

        /// <summary>HTTP status code.</summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>The body. Never null.</summary>
        public byte[] Bytes
        {
            get { return _Bytes; }
            set { _Bytes = value ?? Array.Empty<byte>(); }
        }

        /// <summary>Content type without parameters, or null.</summary>
        public string? ContentType { get; set; } = null;

        /// <summary>ETag header, or null.</summary>
        public string? ETag { get; set; } = null;

        /// <summary>Last-Modified header, or null.</summary>
        public DateTime? LastModifiedUtc { get; set; } = null;

        #endregion

        #region Private-Members

        private byte[] _Bytes = Array.Empty<byte>();

        #endregion
    }
}
