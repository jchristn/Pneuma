namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The comparison applied by a retrieval tag predicate. Serialized as its string name.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TagConditionEnum
    {
        /// <summary>Tag value equals the comparison value.</summary>
        Equals,
        /// <summary>Tag value does not equal the comparison value (or the tag is absent).</summary>
        NotEquals,
        /// <summary>Tag value contains the comparison value.</summary>
        Contains,
        /// <summary>Tag value starts with the comparison value.</summary>
        StartsWith,
        /// <summary>Tag value ends with the comparison value.</summary>
        EndsWith,
        /// <summary>Tag value is ordinally greater than the comparison value.</summary>
        GreaterThan,
        /// <summary>Tag value is ordinally less than the comparison value.</summary>
        LessThan,
        /// <summary>The tag is absent.</summary>
        IsNull,
        /// <summary>The tag is present.</summary>
        IsNotNull
    }
}
