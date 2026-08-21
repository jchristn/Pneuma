namespace Pneuma.Core.Requests
{
    using System;
    using Pneuma.Core.Enums;

    /// <summary>
    /// One tag predicate applied to retrieval: a chunk tag key, a comparison, and (for value comparisons) the
    /// value to compare against. Used inside a <see cref="RetrievalFilter"/> to constrain which stored chunks a
    /// query may draw on.
    /// </summary>
    public class RetrievalTagCondition
    {
        #region Public-Members

        /// <summary>The chunk tag key to test (for example "rights", "authority", "documentType").</summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>The comparison to apply.</summary>
        public TagConditionEnum Condition { get; set; } = TagConditionEnum.Equals;

        /// <summary>The value to compare against; ignored for <see cref="TagConditionEnum.IsNull"/>/<see cref="TagConditionEnum.IsNotNull"/>.</summary>
        public string? Value { get; set; } = null;

        #endregion
    }
}
