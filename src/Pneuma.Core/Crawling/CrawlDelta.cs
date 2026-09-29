namespace Pneuma.Core.Crawling
{
    using System.Collections.Generic;
    using System.Linq;
    using Pneuma.Core.Enums;

    /// <summary>The difference between what a source lists now and a plan's baseline, as planned actions.</summary>
    public class CrawlDelta
    {
        #region Public-Members

        /// <summary>Distinct objects the source listed (folders excluded).</summary>
        public int Enumerated { get; set; } = 0;

        /// <summary>Total size of the listed objects, when sizes are known.</summary>
        public long BytesEnumerated { get; set; } = 0;

        /// <summary>
        /// True when the planned deletions exceed the plan's <see cref="CrawlPlan.MaxDeletionFraction"/>: they wait for
        /// confirmation instead of running.
        /// </summary>
        public bool DeletionsHeld { get; set; } = false;

        /// <summary>The planned actions, one per object.</summary>
        public List<CrawlDeltaItem> Items { get; set; } = new List<CrawlDeltaItem>();

        #endregion

        #region Public-Methods

        /// <summary>How many items have an action.</summary>
        /// <param name="action">The action.</param>
        /// <returns>The count.</returns>
        public int Count(CrawlActionEnum action)
        {
            return Items.Count(i => i.Action == action);
        }

        #endregion
    }
}
