namespace Pneuma.Core.Crawling
{
    using System;
    using System.Collections.Generic;

    /// <summary>Server settings for crawl plans (the <c>Crawling</c> section of pneuma.json).</summary>
    public class CrawlingSettings
    {
        #region Public-Members

        /// <summary>Run scheduled crawl plans on this server. Default true; manual starts work either way.</summary>
        public bool SchedulerEnabled { get; set; } = true;

        /// <summary>Seconds between scheduler passes (due plans, finished operations, stop requests). Default 15; clamped to [2, 3600].</summary>
        public int SchedulerIntervalSeconds
        {
            get { return _SchedulerIntervalSeconds; }
            set { _SchedulerIntervalSeconds = Math.Clamp(value, 2, 3600); }
        }

        /// <summary>Crawl operations this server enumerates at once. Default 2; clamped to [1, 32].</summary>
        public int MaxConcurrentOperations
        {
            get { return _MaxConcurrentOperations; }
            set { _MaxConcurrentOperations = Math.Clamp(value, 1, 32); }
        }

        /// <summary>
        /// Minutes a server's claim on a running plan lasts without renewal. A server that dies mid-run releases the
        /// plan after this long. Default 30; clamped to [5, 1440].
        /// </summary>
        public int ClaimMinutes
        {
            get { return _ClaimMinutes; }
            set { _ClaimMinutes = Math.Clamp(value, 5, 1440); }
        }

        /// <summary>
        /// Folders on the server that local folder crawl plans may read (a plan's folder must be one of these or inside
        /// one). Empty (the default) disables local folder plans, so no tenant can read the server's files unless an
        /// administrator opts in. Never null.
        /// </summary>
        public List<string> AllowedLocalRoots
        {
            get { return _AllowedLocalRoots; }
            set { _AllowedLocalRoots = value ?? new List<string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _AllowedLocalRoots = new List<string>();

        private int _SchedulerIntervalSeconds = 15;
        private int _MaxConcurrentOperations = 2;
        private int _ClaimMinutes = 30;

        #endregion
    }
}
