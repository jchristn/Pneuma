namespace Pneuma.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Enums;

    /// <summary>
    /// A metadata filter applied to retrieval, expressed as chunk <b>labels</b> and <b>tag</b> predicates. A
    /// chunk is eligible only when it carries every required label and satisfies every required tag condition,
    /// and none of the excluded labels or excluded tag conditions match. Labels are matched against each chunk's
    /// conventional <c>label</c> tag. A subject may carry a default filter; a per-request filter is merged with
    /// it (union of required, union of excluded) so a query narrows — never widens — the subject default.
    /// </summary>
    public class RetrievalFilter
    {
        #region Public-Members

        /// <summary>Labels a chunk must all carry to be eligible.</summary>
        public List<string> RequiredLabels { get; set; } = new List<string>();

        /// <summary>Labels that, if any is present, exclude a chunk.</summary>
        public List<string> ExcludedLabels { get; set; } = new List<string>();

        /// <summary>Tag conditions a chunk must all satisfy to be eligible.</summary>
        public List<RetrievalTagCondition> RequiredTags { get; set; } = new List<RetrievalTagCondition>();

        /// <summary>Tag conditions that, if any matches, exclude a chunk.</summary>
        public List<RetrievalTagCondition> ExcludedTags { get; set; } = new List<RetrievalTagCondition>();

        #endregion

        #region Public-Methods

        /// <summary>Whether this filter carries any predicate at all.</summary>
        /// <returns>True when no label or tag predicate is present.</returns>
        public bool IsEmpty()
        {
            return (RequiredLabels == null || RequiredLabels.Count == 0)
                && (ExcludedLabels == null || ExcludedLabels.Count == 0)
                && (RequiredTags == null || RequiredTags.Count == 0)
                && (ExcludedTags == null || ExcludedTags.Count == 0);
        }

        /// <summary>The conventional chunk tag key that carries a chunk's label.</summary>
        public const string LabelTagKey = "label";

        /// <summary>The required predicates as tag conditions (tags plus each required label as an equals-condition on the label tag).</summary>
        /// <returns>The combined required conditions.</returns>
        public List<RetrievalTagCondition> EffectiveRequired()
        {
            return Combine(RequiredTags, RequiredLabels);
        }

        /// <summary>The excluded predicates as tag conditions (tags plus each excluded label as an equals-condition on the label tag).</summary>
        /// <returns>The combined excluded conditions.</returns>
        public List<RetrievalTagCondition> EffectiveExcluded()
        {
            return Combine(ExcludedTags, ExcludedLabels);
        }

        private static List<RetrievalTagCondition> Combine(List<RetrievalTagCondition> tags, List<string> labels)
        {
            List<RetrievalTagCondition> result = new List<RetrievalTagCondition>();
            if (tags != null)
            {
                foreach (RetrievalTagCondition condition in tags)
                {
                    if (condition != null && !String.IsNullOrWhiteSpace(condition.Key)) result.Add(condition);
                }
            }
            if (labels != null)
            {
                foreach (string label in labels)
                {
                    if (!String.IsNullOrWhiteSpace(label)) result.Add(new RetrievalTagCondition { Key = LabelTagKey, Condition = TagConditionEnum.Equals, Value = label });
                }
            }
            return result;
        }

        #endregion
    }
}
