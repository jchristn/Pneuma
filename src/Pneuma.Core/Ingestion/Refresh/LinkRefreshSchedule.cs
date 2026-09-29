namespace Pneuma.Core.Ingestion.Refresh
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;

    /// <summary>Refresh interval rules: validation, the effective interval, jittered next times, and back-off.</summary>
    public static class LinkRefreshSchedule
    {
        #region Public-Members

        /// <summary>Shortest interval allowed, in minutes.</summary>
        public const int MinIntervalMinutes = 60;

        /// <summary>Longest interval allowed, in minutes (one year).</summary>
        public const int MaxIntervalMinutes = 525600;

        /// <summary>Largest share of an interval the next refresh is moved by, so links added together spread out.</summary>
        public const double JitterFraction = 0.1;

        #endregion

        #region Private-Members

        private static readonly Random _Random = new Random();
        private static readonly object _RandomLock = new object();

        #endregion

        #region Public-Methods

        /// <summary>Why an interval is invalid, or null when it is valid (null inherits, 0 is off).</summary>
        /// <param name="minutes">The interval.</param>
        /// <param name="field">The field name for the message.</param>
        /// <returns>The problem, or null.</returns>
        public static string? Validate(int? minutes, string field)
        {
            if (minutes == null || minutes.Value == 0) return null;
            if (minutes.Value < MinIntervalMinutes || minutes.Value > MaxIntervalMinutes)
                return field + " must be 0 (off) or between " + MinIntervalMinutes + " and " + MaxIntervalMinutes + " minutes.";
            return null;
        }

        /// <summary>True when a link can be refreshed at all: an active URL link that no crawl plan owns.</summary>
        /// <param name="link">The link.</param>
        /// <returns>True when refreshable.</returns>
        public static bool IsRefreshable(SubjectLink link)
        {
            return link != null && link.Active && link.DeletionStatus == LinkDeletionStatusEnum.None
                && link.SourceKind == SourceKindEnum.Url && String.IsNullOrEmpty(link.CrawlPlanId);
        }

        /// <summary>The interval in effect for a link: its own, or the subject's default; 0 when refresh is off.</summary>
        /// <param name="link">The link.</param>
        /// <param name="subject">The link's subject, or null.</param>
        /// <returns>Minutes, or 0.</returns>
        public static int EffectiveInterval(SubjectLink link, Subject? subject)
        {
            if (link == null || !IsRefreshable(link)) return 0;
            int minutes = link.RefreshIntervalMinutes ?? subject?.DefaultRefreshIntervalMinutes ?? 0;
            return minutes <= 0 ? 0 : Math.Clamp(minutes, MinIntervalMinutes, MaxIntervalMinutes);
        }

        /// <summary>The next refresh: now plus the interval, moved by up to <see cref="JitterFraction"/> either way.</summary>
        /// <param name="nowUtc">The current time.</param>
        /// <param name="intervalMinutes">The interval; 0 means none.</param>
        /// <returns>The next refresh, or null when the interval is 0.</returns>
        public static DateTime? Next(DateTime nowUtc, int intervalMinutes)
        {
            if (intervalMinutes <= 0) return null;
            double jitter;
            lock (_RandomLock)
            {
                jitter = (_Random.NextDouble() * 2.0 - 1.0) * JitterFraction;
            }
            return nowUtc.AddMinutes(intervalMinutes * (1.0 + jitter));
        }

        /// <summary>
        /// When to try again after failed checks: 15 minutes doubled per failure, never later than the interval.
        /// </summary>
        /// <param name="nowUtc">The current time.</param>
        /// <param name="failures">Failures in a row (1 or more).</param>
        /// <param name="intervalMinutes">The interval.</param>
        /// <returns>The retry time.</returns>
        public static DateTime Backoff(DateTime nowUtc, int failures, int intervalMinutes)
        {
            double minutes = 15.0 * Math.Pow(2, Math.Clamp(failures - 1, 0, 16));
            return nowUtc.AddMinutes(Math.Min(minutes, Math.Max(MinIntervalMinutes, intervalMinutes)));
        }

        #endregion
    }
}
