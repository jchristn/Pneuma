namespace Pneuma.Server.Services
{
    using System;

    /// <summary>The outcome of reading a request body under a size limit.</summary>
    public class RequestBodyResult
    {
        #region Public-Members

        /// <summary>The body, or null when it was over the limit.</summary>
        public byte[]? Data { get; set; } = null;

        /// <summary>True when the body was larger than the limit; reading stopped at the limit.</summary>
        public bool TooLarge { get; set; } = false;

        /// <summary>Bytes read (for an oversized body, how far reading got before it stopped). Minimum 0.</summary>
        public long BytesRead
        {
            get { return _BytesRead; }
            set { _BytesRead = Math.Max(0L, value); }
        }

        /// <summary>True when the body arrived with chunked transfer encoding.</summary>
        public bool Chunked { get; set; } = false;

        #endregion

        #region Private-Members

        private long _BytesRead = 0;

        #endregion
    }
}
