namespace Pneuma.Sdk.Models
{
    using Pneuma.Sdk.Enums;

    /// <summary>When a crawl plan runs on its own.</summary>
    public class CrawlSchedule
    {
        /// <summary>Manual, Interval, or Cron.</summary>
        public CrawlScheduleTypeEnum Type { get; set; } = CrawlScheduleTypeEnum.Manual;

        /// <summary>Minutes between runs (5 to 525600) for an Interval schedule.</summary>
        public int IntervalMinutes { get; set; } = 1440;

        /// <summary>Five-field cron expression for a Cron schedule.</summary>
        public string? CronExpression { get; set; } = null;

        /// <summary>IANA or Windows time zone the cron expression is evaluated in.</summary>
        public string TimeZone { get; set; } = "UTC";
    }
}
