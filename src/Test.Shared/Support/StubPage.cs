namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;

    /// <summary>One response served by <see cref="StubWebSite"/>.</summary>
    public class StubPage
    {
        #region Public-Members

        /// <summary>HTTP status code. Default 200.</summary>
        public int Status { get; set; } = 200;

        /// <summary>Content type. Default <c>text/html; charset=utf-8</c>.</summary>
        public string ContentType { get; set; } = "text/html; charset=utf-8";

        /// <summary>Response body. Default empty.</summary>
        public byte[] Body { get; set; } = Array.Empty<byte>();

        /// <summary>Extra response headers (for example <c>Location</c>, <c>ETag</c>, <c>Last-Modified</c>).</summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        #endregion
    }
}
