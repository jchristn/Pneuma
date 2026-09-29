namespace Pneuma.Core.Crawling
{
    using System;
    using Pneuma.Core.Enums;

    /// <summary>When a crawl plan runs on its own.</summary>
    public class CrawlSchedule
    {
        #region Public-Members

        /// <summary>Shortest interval allowed, in minutes.</summary>
        public const int MinIntervalMinutes = 5;

        /// <summary>Longest interval allowed, in minutes (one year).</summary>
        public const int MaxIntervalMinutes = 525600;

        /// <summary>Manual (default), Interval, or Cron.</summary>
        public CrawlScheduleTypeEnum Type { get; set; } = CrawlScheduleTypeEnum.Manual;

        /// <summary>
        /// Minutes between runs for an Interval schedule. Default 1440 (daily). Must be between
        /// <see cref="MinIntervalMinutes"/> and <see cref="MaxIntervalMinutes"/>; a value outside the range is rejected by
        /// validation rather than silently changed.
        /// </summary>
        public int IntervalMinutes { get; set; } = 1440;

        /// <summary>Five-field cron expression for a Cron schedule (for example "0 3 * * *" for 03:00 daily).</summary>
        public string? CronExpression { get; set; } = null;

        /// <summary>IANA or Windows time zone id the cron expression is evaluated in. Default UTC.</summary>
        public string TimeZone { get; set; } = "UTC";

        #endregion

    }
}
