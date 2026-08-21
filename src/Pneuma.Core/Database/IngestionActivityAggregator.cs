namespace Pneuma.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Responses;

    /// <summary>
    /// Provider-neutral bucketing of ingestion job event rows into a time-bucketed, stage-stacked
    /// <see cref="IngestionActivitySummary"/>. Each provider builds its own filtered
    /// <c>SELECT createdutc, stage FROM ingestionjobevents</c> query (dialect-specific WHERE) and hands the
    /// resulting rows here, mirroring how request-history summarization buckets in memory after a flat scan.
    /// </summary>
    internal static class IngestionActivityAggregator
    {
        /// <summary>
        /// Bucket the supplied rows (each carrying a "createdutc" and "stage" column) into fixed-width time
        /// buckets spanning [fromUtc, toUtc), counting events per pipeline stage.
        /// </summary>
        /// <param name="table">Rows with "createdutc" and "stage" columns.</param>
        /// <param name="fromUtc">Inclusive UTC range start.</param>
        /// <param name="toUtc">Exclusive UTC range end.</param>
        /// <param name="bucketMinutes">Bucket width in minutes (at least 1).</param>
        /// <returns>Stage-stacked activity summary.</returns>
        internal static IngestionActivitySummary Aggregate(DataTable table, DateTime fromUtc, DateTime toUtc, int bucketMinutes)
        {
            if (bucketMinutes < 1) bucketMinutes = 1;

            List<IngestionActivityBucket> buckets = new List<IngestionActivityBucket>();
            List<Dictionary<IngestionStageEnum, long>> bucketStageCounts = new List<Dictionary<IngestionStageEnum, long>>();

            DateTime cursor = fromUtc;
            while (cursor < toUtc)
            {
                DateTime bucketEnd = cursor.AddMinutes(bucketMinutes);
                if (bucketEnd > toUtc) bucketEnd = toUtc;
                buckets.Add(new IngestionActivityBucket { BucketStartUtc = cursor, BucketEndUtc = bucketEnd });
                bucketStageCounts.Add(new Dictionary<IngestionStageEnum, long>());
                cursor = bucketEnd;
            }

            Dictionary<IngestionStageEnum, long> totals = new Dictionary<IngestionStageEnum, long>();
            long totalCount = 0;

            if (table != null)
            {
                foreach (DataRow row in table.Rows)
                {
                    DateTime created = RowReader.GetDateTime(row, "createdutc");
                    if (created < fromUtc || created >= toUtc) continue;
                    IngestionStageEnum stage = RowReader.GetEnum<IngestionStageEnum>(row, "stage", IngestionStageEnum.Pending);

                    long bucketIndex = (long)((created - fromUtc).TotalMinutes / bucketMinutes);
                    if (bucketIndex < 0 || bucketIndex >= bucketStageCounts.Count) continue;

                    Dictionary<IngestionStageEnum, long> counts = bucketStageCounts[(int)bucketIndex];
                    counts.TryGetValue(stage, out long existing);
                    counts[stage] = existing + 1;

                    totals.TryGetValue(stage, out long existingTotal);
                    totals[stage] = existingTotal + 1;
                    totalCount++;
                }
            }

            for (int i = 0; i < buckets.Count; i++)
            {
                Dictionary<IngestionStageEnum, long> counts = bucketStageCounts[i];
                long bucketTotal = 0;
                foreach (KeyValuePair<IngestionStageEnum, long> kvp in counts.OrderBy(k => (int)k.Key))
                {
                    buckets[i].Stages.Add(new IngestionStageCount { Stage = kvp.Key, Count = kvp.Value });
                    bucketTotal += kvp.Value;
                }
                buckets[i].TotalCount = bucketTotal;
            }

            List<IngestionStageCount> orderedTotals = new List<IngestionStageCount>();
            foreach (KeyValuePair<IngestionStageEnum, long> kvp in totals.OrderBy(k => (int)k.Key))
            {
                orderedTotals.Add(new IngestionStageCount { Stage = kvp.Key, Count = kvp.Value });
            }

            return new IngestionActivitySummary
            {
                TotalCount = totalCount,
                Totals = orderedTotals,
                Buckets = buckets
            };
        }
    }
}
