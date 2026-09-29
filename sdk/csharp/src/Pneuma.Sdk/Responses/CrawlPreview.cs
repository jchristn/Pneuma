namespace Pneuma.Sdk.Responses
{
    using System.Collections.Generic;

    /// <summary>What a crawl plan would do if it ran now.</summary>
    public class CrawlPreview
    {
        /// <summary>Objects listed.</summary>
        public int Enumerated { get; set; } = 0;

        /// <summary>Total size listed.</summary>
        public long BytesEnumerated { get; set; } = 0;

        /// <summary>Would be added.</summary>
        public int Add { get; set; } = 0;

        /// <summary>Would be re-ingested.</summary>
        public int Update { get; set; } = 0;

        /// <summary>Would be retried.</summary>
        public int Retry { get; set; } = 0;

        /// <summary>Unchanged.</summary>
        public int Unchanged { get; set; } = 0;

        /// <summary>Links that would be deleted.</summary>
        public int Delete { get; set; } = 0;

        /// <summary>Gone but kept.</summary>
        public int Missing { get; set; } = 0;

        /// <summary>Filtered out.</summary>
        public int Skip { get; set; } = 0;

        /// <summary>True when deletions would be held.</summary>
        public bool DeletionsHeld { get; set; } = false;

        /// <summary>True when Items was cut.</summary>
        public bool Truncated { get; set; } = false;

        /// <summary>Most items returned.</summary>
        public int MaxItems { get; set; } = 500;

        /// <summary>The objects a run would act on.</summary>
        public List<CrawlPreviewItem> Items { get; set; } = new List<CrawlPreviewItem>();
    }
}
