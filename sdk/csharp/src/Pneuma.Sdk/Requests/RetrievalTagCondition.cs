namespace Pneuma.Sdk.Requests
{
    using Pneuma.Sdk.Enums;

    /// <summary>
    /// One tag predicate applied to retrieval: a chunk tag key, a comparison, and (for value comparisons) the
    /// value to compare against.
    /// </summary>
    public class RetrievalTagCondition
    {
        /// <summary>The chunk tag key to test (for example "author", "rights").</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>The comparison to apply.</summary>
        public TagConditionEnum Condition { get; set; } = TagConditionEnum.Equals;

        /// <summary>The value to compare against; ignored for IsNull/IsNotNull.</summary>
        public string? Value { get; set; } = null;
    }
}
