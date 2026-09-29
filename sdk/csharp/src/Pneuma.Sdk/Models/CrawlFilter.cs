namespace Pneuma.Sdk.Models
{
    using System.Collections.Generic;

    /// <summary>Which enumerated objects a crawl plan keeps (globs match the object key).</summary>
    public class CrawlFilter
    {
        /// <summary>Globs a key must match (any); empty keeps everything.</summary>
        public List<string> IncludePatterns { get; set; } = new List<string>();

        /// <summary>Globs that exclude a key.</summary>
        public List<string> ExcludePatterns { get; set; } = new List<string>();

        /// <summary>Content types to keep; empty keeps every type.</summary>
        public List<string> AllowedContentTypes { get; set; } = new List<string>();

        /// <summary>Smallest object kept; 0 for no minimum.</summary>
        public long MinSizeBytes { get; set; } = 0;

        /// <summary>Largest object kept; 0 for no maximum.</summary>
        public long MaxSizeBytes { get; set; } = 0;

        /// <summary>Most objects one run keeps; 0 for no limit.</summary>
        public int MaxObjects { get; set; } = 0;
    }
}
