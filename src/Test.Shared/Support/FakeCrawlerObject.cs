namespace Test.Shared.Support
{
    using System;

    /// <summary>One object held by a <see cref="FakeCrawler"/>.</summary>
    public sealed class FakeCrawlerObject
    {
        /// <summary>Text content.</summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>Version token, or null.</summary>
        public string? Version { get; set; } = null;

        /// <summary>Content type.</summary>
        public string ContentType { get; set; } = "text/plain";
    }
}
