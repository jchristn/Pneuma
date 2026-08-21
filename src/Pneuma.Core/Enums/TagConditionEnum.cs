namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A comparison applied to a chunk tag value when filtering retrieval. The names match the conditions the
    /// retrieval store (RecallDB) understands in its tag filter.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TagConditionEnum
    {
        /// <summary>The tag value equals the supplied value.</summary>
        Equals,
        /// <summary>The tag value does not equal the supplied value.</summary>
        NotEquals,
        /// <summary>The tag value contains the supplied value.</summary>
        Contains,
        /// <summary>The tag value starts with the supplied value.</summary>
        StartsWith,
        /// <summary>The tag value ends with the supplied value.</summary>
        EndsWith,
        /// <summary>The tag value is greater than the supplied value.</summary>
        GreaterThan,
        /// <summary>The tag value is less than the supplied value.</summary>
        LessThan,
        /// <summary>The tag is absent.</summary>
        IsNull,
        /// <summary>The tag is present.</summary>
        IsNotNull
    }
}
