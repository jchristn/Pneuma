namespace Pneuma.Core.Crawling
{
    using System;
    using System.Collections.Generic;
    using Cronos;
    using Pneuma.Core.Enums;

    /// <summary>Validates crawl schedules and computes when a plan runs next.</summary>
    public static class CrawlScheduleCalculator
    {
        #region Public-Methods

        /// <summary>Validate a schedule.</summary>
        /// <param name="schedule">The schedule.</param>
        /// <returns>The problems found; empty when valid.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="schedule"/> is null.</exception>
        public static List<string> Validate(CrawlSchedule schedule)
        {
            if (schedule == null) throw new ArgumentNullException(nameof(schedule));
            List<string> errors = new List<string>();
            if (schedule.Type == CrawlScheduleTypeEnum.Interval && (schedule.IntervalMinutes < CrawlSchedule.MinIntervalMinutes || schedule.IntervalMinutes > CrawlSchedule.MaxIntervalMinutes))
                errors.Add("schedule.intervalMinutes must be between " + CrawlSchedule.MinIntervalMinutes + " and " + CrawlSchedule.MaxIntervalMinutes + ".");
            if (schedule.Type == CrawlScheduleTypeEnum.Cron)
            {
                if (String.IsNullOrWhiteSpace(schedule.CronExpression))
                {
                    errors.Add("schedule.cronExpression is required for a Cron schedule.");
                }
                else
                {
                    try
                    {
                        CronExpression.Parse(schedule.CronExpression.Trim(), CronFormat.Standard);
                    }
                    catch (CronFormatException e)
                    {
                        errors.Add("schedule.cronExpression is not a valid five-field cron expression: " + e.Message);
                    }
                }
            }
            if (ResolveTimeZone(schedule.TimeZone) == null) errors.Add("schedule.timeZone '" + schedule.TimeZone + "' is not a known time zone.");
            return errors;
        }

        /// <summary>
        /// When a plan runs next after <paramref name="fromUtc"/>, or null for a manual or disabled plan. Cron times are
        /// evaluated in the schedule's time zone, so a 03:00 schedule stays at 03:00 local time across daylight saving
        /// changes.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="fromUtc">The time to compute from (the run's start, or now).</param>
        /// <returns>The next run in UTC, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="plan"/> is null.</exception>
        public static DateTime? NextRunUtc(CrawlPlan plan, DateTime fromUtc)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.Enabled) return null;
            CrawlSchedule schedule = plan.Schedule;
            DateTime from = DateTime.SpecifyKind(fromUtc.ToUniversalTime(), DateTimeKind.Utc);
            switch (schedule.Type)
            {
                case CrawlScheduleTypeEnum.Interval:
                    return from.AddMinutes(Math.Clamp(schedule.IntervalMinutes, CrawlSchedule.MinIntervalMinutes, CrawlSchedule.MaxIntervalMinutes));
                case CrawlScheduleTypeEnum.Cron:
                    if (String.IsNullOrWhiteSpace(schedule.CronExpression)) return null;
                    TimeZoneInfo zone = ResolveTimeZone(schedule.TimeZone) ?? TimeZoneInfo.Utc;
                    CronExpression expression = CronExpression.Parse(schedule.CronExpression.Trim(), CronFormat.Standard);
                    return expression.GetNextOccurrence(from, zone);
                default:
                    return null;
            }
        }

        /// <summary>Resolve an IANA or Windows time zone id; null or empty is UTC.</summary>
        /// <param name="id">The id.</param>
        /// <returns>The zone, or null when unknown.</returns>
        public static TimeZoneInfo? ResolveTimeZone(string? id)
        {
            if (String.IsNullOrWhiteSpace(id) || String.Equals(id, "UTC", StringComparison.OrdinalIgnoreCase)) return TimeZoneInfo.Utc;
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
            }
            catch (TimeZoneNotFoundException)
            {
                return null;
            }
            catch (InvalidTimeZoneException)
            {
                return null;
            }
        }

        #endregion
    }
}
