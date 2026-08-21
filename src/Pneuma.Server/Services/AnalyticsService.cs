namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Responses;

    /// <summary>
    /// Computes per-subject chat analytics (volume, latency percentiles, per-stage timing, feedback) over a
    /// time window by aggregating persisted chat turns, performance events, and feedback in-process. Percentiles
    /// are computed in-service so the aggregation is provider-neutral.
    /// </summary>
    public class AnalyticsService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the analytics service.</summary>
        /// <param name="db">Database driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="db"/> is null.</exception>
        public AnalyticsService(DatabaseDriverBase db)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
        }

        #endregion

        #region Public-Methods

        /// <summary>Build the analytics report for a tenant, optionally scoped to one subject, since a cutoff.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject to scope to, or null for all subjects in the tenant.</param>
        /// <param name="sinceUtc">Only turns/events/feedback created on or after this instant are included.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The analytics report.</returns>
        public async Task<AnalyticsReport> BuildAsync(string tenantId, string? subjectId, DateTime sinceUtc, CancellationToken token = default)
        {
            AnalyticsReport report = new AnalyticsReport();

            List<ChatTurnRecord> turns = await _Db.ChatTurns.EnumerateAsync(tenantId, subjectId, token).ConfigureAwait(false);
            List<ChatTurnRecord> windowed = new List<ChatTurnRecord>();
            foreach (ChatTurnRecord turn in turns)
            {
                if (turn.CreatedUtc >= sinceUtc) windowed.Add(turn);
            }

            AnalyticsOverview overview = report.Overview;
            overview.TurnCount = windowed.Count;
            if (windowed.Count > 0)
            {
                List<double> generations = new List<double>(windowed.Count);
                double sumGeneration = 0, sumTtft = 0, sumPrompt = 0, sumCompletion = 0, sumTps = 0;
                foreach (ChatTurnRecord turn in windowed)
                {
                    generations.Add(turn.GenerationMs);
                    sumGeneration += turn.GenerationMs;
                    sumTtft += turn.TimeToFirstTokenMs;
                    sumPrompt += turn.PromptTokens;
                    sumCompletion += turn.CompletionTokens;
                    sumTps += (turn.GenerationMs > 0 && turn.CompletionTokens > 0) ? turn.CompletionTokens / (turn.GenerationMs / 1000.0) : 0;
                }
                overview.AvgGenerationMs = sumGeneration / windowed.Count;
                overview.AvgTimeToFirstTokenMs = sumTtft / windowed.Count;
                overview.AvgPromptTokens = sumPrompt / windowed.Count;
                overview.AvgCompletionTokens = sumCompletion / windowed.Count;
                overview.AvgTokensPerSecond = sumTps / windowed.Count;
                overview.P50GenerationMs = Percentile(generations, 50);
                overview.P95GenerationMs = Percentile(generations, 95);
                overview.P99GenerationMs = Percentile(generations, 99);
            }

            List<ChatFeedback> feedback = await _Db.ChatFeedback.EnumerateAsync(tenantId, subjectId, token).ConfigureAwait(false);
            foreach (ChatFeedback item in feedback)
            {
                if (item.CreatedUtc < sinceUtc) continue;
                if (item.Rating == FeedbackRatingEnum.Up) overview.ThumbsUp++;
                else if (item.Rating == FeedbackRatingEnum.Down) overview.ThumbsDown++;
            }

            Dictionary<DateTime, List<double>> byDay = new Dictionary<DateTime, List<double>>();
            foreach (ChatTurnRecord turn in windowed)
            {
                DateTime day = turn.CreatedUtc.Date;
                if (!byDay.TryGetValue(day, out List<double>? bucket))
                {
                    bucket = new List<double>();
                    byDay[day] = bucket;
                }
                bucket.Add(turn.GenerationMs);
            }
            List<DateTime> days = new List<DateTime>(byDay.Keys);
            days.Sort();
            foreach (DateTime day in days)
            {
                List<double> values = byDay[day];
                double sum = 0;
                foreach (double value in values) sum += value;
                report.Timeseries.Add(new AnalyticsBucket { BucketUtc = day, Count = values.Count, AvgGenerationMs = values.Count > 0 ? sum / values.Count : 0 });
            }

            List<ChatTurnPerfEvent> events = await _Db.ChatTurnPerfEvents.EnumerateBySubjectAsync(tenantId, subjectId, sinceUtc, token).ConfigureAwait(false);
            Dictionary<string, List<double>> byStage = new Dictionary<string, List<double>>(StringComparer.Ordinal);
            foreach (ChatTurnPerfEvent evt in events)
            {
                if (!byStage.TryGetValue(evt.Stage, out List<double>? durations))
                {
                    durations = new List<double>();
                    byStage[evt.Stage] = durations;
                }
                durations.Add(evt.DurationMs);
            }
            List<AnalyticsStageStat> stageStats = new List<AnalyticsStageStat>();
            foreach (KeyValuePair<string, List<double>> pair in byStage)
            {
                double sum = 0;
                foreach (double value in pair.Value) sum += value;
                stageStats.Add(new AnalyticsStageStat
                {
                    Stage = pair.Key,
                    Count = pair.Value.Count,
                    AvgDurationMs = pair.Value.Count > 0 ? sum / pair.Value.Count : 0,
                    P95DurationMs = Percentile(pair.Value, 95)
                });
            }
            stageStats.Sort((a, b) => b.AvgDurationMs.CompareTo(a.AvgDurationMs));
            report.Stages = stageStats;

            return report;
        }

        #endregion

        #region Private-Methods

        private static double Percentile(List<double> values, double percentile)
        {
            if (values == null || values.Count == 0) return 0;
            List<double> sorted = new List<double>(values);
            sorted.Sort();
            double rank = (percentile / 100.0) * (sorted.Count - 1);
            int lower = (int)Math.Floor(rank);
            int upper = (int)Math.Ceiling(rank);
            if (lower == upper) return sorted[lower];
            double weight = rank - lower;
            return sorted[lower] * (1 - weight) + sorted[upper] * weight;
        }

        #endregion
    }
}
