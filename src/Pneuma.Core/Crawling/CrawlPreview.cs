namespace Pneuma.Core.Crawling
{
    using System.Collections.Generic;

    /// <summary>
    /// What a crawl plan would do if it ran now, computed by listing the source and comparing it with the baseline.
    /// Nothing is created, changed, or deleted.
    /// </summary>
    public class CrawlPreview
    {
        #region Public-Members

        /// <summary>Distinct objects the source listed.</summary>
        public int Enumerated { get; set; } = 0;

        /// <summary>Total size of the listed objects, when known.</summary>
        public long BytesEnumerated { get; set; } = 0;

        /// <summary>Objects that would be added.</summary>
        public int Add { get; set; } = 0;

        /// <summary>Objects that would be re-ingested because they changed.</summary>
        public int Update { get; set; } = 0;

        /// <summary>Objects that would be retried after failing last time.</summary>
        public int Retry { get; set; } = 0;

        /// <summary>Objects unchanged since the last run.</summary>
        public int Unchanged { get; set; } = 0;

        /// <summary>Links that would be deleted.</summary>
        public int Delete { get; set; } = 0;

        /// <summary>Objects gone from the source that would be kept (deletions off).</summary>
        public int Missing { get; set; } = 0;

        /// <summary>Objects filtered out or over the limit.</summary>
        public int Skip { get; set; } = 0;

        /// <summary>True when the deletions would exceed the plan's limit and wait for confirmation.</summary>
        public bool DeletionsHeld { get; set; } = false;

        /// <summary>True when <see cref="Items"/> was cut at <see cref="MaxItems"/>.</summary>
        public bool Truncated { get; set; } = false;

        /// <summary>Most items a preview returns.</summary>
        public int MaxItems { get; set; } = 500;

        /// <summary>The objects a run would act on (unchanged objects are counted only), up to <see cref="MaxItems"/>.</summary>
        public List<CrawlPreviewItem> Items { get; set; } = new List<CrawlPreviewItem>();

        #endregion
    }
}
