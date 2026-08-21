namespace Pneuma.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// A metadata filter applied to retrieval, expressed as chunk-tag predicates. A chunk is eligible only when
    /// it satisfies every <see cref="Required"/> condition and none of the <see cref="Excluded"/> conditions. A
    /// subject may carry a default filter; a per-request filter is merged with it (union of required, union of
    /// excluded) so a query can further narrow — never widen — the subject default.
    /// </summary>
    public class RetrievalFilter
    {
        #region Public-Members

        /// <summary>Conditions a chunk must all satisfy to be eligible.</summary>
        public List<RetrievalTagCondition> Required { get; set; } = new List<RetrievalTagCondition>();

        /// <summary>Conditions that, if any match, exclude a chunk.</summary>
        public List<RetrievalTagCondition> Excluded { get; set; } = new List<RetrievalTagCondition>();

        #endregion

        #region Public-Methods

        /// <summary>Whether this filter carries any predicate at all.</summary>
        /// <returns>True when at least one required or excluded condition is present.</returns>
        public bool IsEmpty()
        {
            return (Required == null || Required.Count == 0) && (Excluded == null || Excluded.Count == 0);
        }

        #endregion
    }
}
