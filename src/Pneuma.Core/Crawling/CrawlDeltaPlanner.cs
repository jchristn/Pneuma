namespace Pneuma.Core.Crawling
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Pneuma.Core.Enums;

    /// <summary>
    /// Compares a source's listing with a plan's baseline and decides what to do with each object. Pure: nothing is
    /// read or written, so a run and a preview plan the same way.
    /// </summary>
    public static class CrawlDeltaPlanner
    {
        #region Public-Methods

        /// <summary>Plan a run.</summary>
        /// <param name="plan">The plan (filter, flags, and deletion limit).</param>
        /// <param name="listed">What the source lists now.</param>
        /// <param name="baseline">The plan's crawl objects from earlier runs.</param>
        /// <returns>The planned actions.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static CrawlDelta Compute(CrawlPlan plan, IEnumerable<CrawledObject> listed, IEnumerable<CrawlObject> baseline)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (listed == null) throw new ArgumentNullException(nameof(listed));
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));

            Dictionary<string, CrawlObject> known = new Dictionary<string, CrawlObject>(StringComparer.Ordinal);
            foreach (CrawlObject obj in baseline)
            {
                if (!known.ContainsKey(obj.ExternalKey)) known[obj.ExternalKey] = obj;
            }

            CrawlDelta delta = new CrawlDelta();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> kept = new HashSet<string>(StringComparer.Ordinal);

            foreach (CrawledObject obj in listed)
            {
                if (obj == null || obj.IsFolder || String.IsNullOrEmpty(obj.Key)) continue;
                if (!seen.Add(obj.Key)) continue;
                delta.Enumerated++;
                delta.BytesEnumerated += Math.Max(0L, obj.SizeBytes);

                CrawlObject? prior;
                known.TryGetValue(obj.Key, out prior);

                string? reason = CrawlFilterMatcher.Reject(plan.Filter, obj);
                if (reason == null && plan.Filter.MaxObjects > 0 && kept.Count >= plan.Filter.MaxObjects) reason = "Over the plan's object limit (" + plan.Filter.MaxObjects + ").";
                if (reason != null)
                {
                    delta.Items.Add(new CrawlDeltaItem { Key = obj.Key, Action = CrawlActionEnum.Skip, Source = obj, Baseline = prior, Detail = reason });
                    continue;
                }
                kept.Add(obj.Key);

                if (prior == null || prior.LinkId == null)
                {
                    if (plan.ProcessAdditions) delta.Items.Add(new CrawlDeltaItem { Key = obj.Key, Action = CrawlActionEnum.Add, Source = obj, Baseline = prior });
                    else delta.Items.Add(new CrawlDeltaItem { Key = obj.Key, Action = CrawlActionEnum.Skip, Source = obj, Baseline = prior, Detail = "Additions are off." });
                    continue;
                }

                if (Changed(prior.VersionToken, obj.VersionToken))
                {
                    if (plan.ProcessUpdates) delta.Items.Add(new CrawlDeltaItem { Key = obj.Key, Action = CrawlActionEnum.Update, Source = obj, Baseline = prior });
                    else delta.Items.Add(new CrawlDeltaItem { Key = obj.Key, Action = CrawlActionEnum.Skip, Source = obj, Baseline = prior, Detail = "Updates are off." });
                    continue;
                }

                if (prior.Status == CrawlObjectStatusEnum.Failed && plan.RetryFailedObjects)
                {
                    delta.Items.Add(new CrawlDeltaItem { Key = obj.Key, Action = CrawlActionEnum.Retry, Source = obj, Baseline = prior });
                    continue;
                }

                delta.Items.Add(new CrawlDeltaItem { Key = obj.Key, Action = CrawlActionEnum.Unchanged, Source = obj, Baseline = prior });
            }

            List<CrawlObject> gone = known.Values.Where(o => !kept.Contains(o.ExternalKey)).OrderBy(o => o.ExternalKey, StringComparer.Ordinal).ToList();
            if (plan.ProcessDeletions)
            {
                int linked = known.Values.Count(o => o.LinkId != null);
                int deleting = gone.Count(o => o.LinkId != null);
                if (linked > 0 && deleting > 0 && plan.MaxDeletionFraction < 1.0 && (double)deleting / linked > plan.MaxDeletionFraction) delta.DeletionsHeld = true;
                foreach (CrawlObject obj in gone)
                {
                    delta.Items.Add(new CrawlDeltaItem { Key = obj.ExternalKey, Action = CrawlActionEnum.Delete, Baseline = obj });
                }
            }
            else
            {
                foreach (CrawlObject obj in gone)
                {
                    delta.Items.Add(new CrawlDeltaItem { Key = obj.ExternalKey, Action = CrawlActionEnum.Missing, Baseline = obj });
                }
            }
            return delta;
        }

        /// <summary>
        /// True when an object changed: its version token differs, or either side has none (a source without version
        /// tokens re-ingests every run, and unchanged content then completes early by content hash).
        /// </summary>
        /// <param name="prior">The stored token.</param>
        /// <param name="current">The listed token.</param>
        /// <returns>True when changed.</returns>
        public static bool Changed(string? prior, string? current)
        {
            if (String.IsNullOrEmpty(prior) || String.IsNullOrEmpty(current)) return true;
            return !String.Equals(prior, current, StringComparison.Ordinal);
        }

        #endregion
    }
}
