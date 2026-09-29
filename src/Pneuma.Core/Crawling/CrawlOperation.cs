namespace Pneuma.Core.Crawling
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// One run of a crawl plan: when it ran, what triggered it, what it found (the counters), and how it ended. A run
    /// enumerates the source, dispatches the changes as ingestion jobs, and is finished only when those jobs are.
    /// </summary>
    public class CrawlOperation
    {
        #region Public-Members

        /// <summary>Crawl operation identifier (prefix "cop_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>The plan that ran.</summary>
        public string PlanId { get; set; } = String.Empty;

        /// <summary>The plan's subject.</summary>
        public string SubjectId { get; set; } = String.Empty;

        /// <summary>What started the run.</summary>
        public CrawlTriggerEnum Trigger { get; set; } = CrawlTriggerEnum.Manual;

        /// <summary>The run's state.</summary>
        public CrawlOperationStatusEnum Status { get; set; } = CrawlOperationStatusEnum.Running;

        /// <summary>Objects the source listed.</summary>
        public int Enumerated { get; set; } = 0;

        /// <summary>New objects ingested.</summary>
        public int Added { get; set; } = 0;

        /// <summary>Changed objects re-ingested.</summary>
        public int Updated { get; set; } = 0;

        /// <summary>Objects that failed last time and were re-ingested.</summary>
        public int Retried { get; set; } = 0;

        /// <summary>Objects unchanged since the last run.</summary>
        public int Unchanged { get; set; } = 0;

        /// <summary>Links deleted because their objects disappeared.</summary>
        public int Deleted { get; set; } = 0;

        /// <summary>Objects that disappeared but were kept (deletions off) or held.</summary>
        public int Missing { get; set; } = 0;

        /// <summary>Objects filtered out or over the object limit.</summary>
        public int Skipped { get; set; } = 0;

        /// <summary>Objects that could not be dispatched or whose ingestion failed.</summary>
        public int Failed { get; set; } = 0;

        /// <summary>Total bytes of the objects the source listed, when sizes are known.</summary>
        public long BytesEnumerated { get; set; } = 0;

        /// <summary>Deletions waiting for confirmation when the run is Held; 0 otherwise.</summary>
        public int HeldDeletions { get; set; } = 0;

        /// <summary>Why the run failed or was held, or null.</summary>
        public string? Error { get; set; } = null;

        /// <summary>When the run started.</summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>When enumeration finished, or null.</summary>
        public DateTime? EnumeratedUtc { get; set; } = null;

        /// <summary>When the changes were dispatched as jobs, or null.</summary>
        public DateTime? DispatchedUtc { get; set; } = null;

        /// <summary>When the run finished (its jobs finished, or it failed or was cancelled), or null.</summary>
        public DateTime? FinishedUtc { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateCrawlOperationId();

        #endregion
    }
}
