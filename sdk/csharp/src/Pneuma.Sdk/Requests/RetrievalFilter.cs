namespace Pneuma.Sdk.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// A metadata filter applied to retrieval, expressed as chunk <b>labels</b> and <b>tag</b> predicates. A
    /// chunk is eligible only when it carries every required label and satisfies every required tag condition,
    /// and none of the excluded labels or excluded tag conditions match. Supplied on a query/chat request to
    /// scope retrieval to documents ingested with matching labels/tags.
    /// </summary>
    public class RetrievalFilter
    {
        /// <summary>Labels a chunk must all carry to be eligible.</summary>
        public List<string> RequiredLabels { get; set; } = new List<string>();

        /// <summary>Labels that, if any is present, exclude a chunk.</summary>
        public List<string> ExcludedLabels { get; set; } = new List<string>();

        /// <summary>Tag conditions a chunk must all satisfy to be eligible.</summary>
        public List<RetrievalTagCondition> RequiredTags { get; set; } = new List<RetrievalTagCondition>();

        /// <summary>Tag conditions that, if any matches, exclude a chunk.</summary>
        public List<RetrievalTagCondition> ExcludedTags { get; set; } = new List<RetrievalTagCondition>();
    }
}
