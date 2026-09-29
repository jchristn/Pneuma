namespace Pneuma.Sdk.Responses
{
    using Pneuma.Sdk.Enums;

    /// <summary>One object in a crawl preview.</summary>
    public class CrawlPreviewItem
    {
        /// <summary>The object's key.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>What a run would do.</summary>
        public CrawlActionEnum Action { get; set; } = CrawlActionEnum.Add;

        /// <summary>Content type.</summary>
        public string? ContentType { get; set; } = null;

        /// <summary>Size in bytes.</summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>Why it would be skipped.</summary>
        public string? Detail { get; set; } = null;
    }
}
