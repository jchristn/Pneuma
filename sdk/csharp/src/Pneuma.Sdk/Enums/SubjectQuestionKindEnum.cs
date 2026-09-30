namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>The kind of an example question.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SubjectQuestionKindEnum
    {
        /// <summary>A single fact.</summary>
        Fact,
        /// <summary>How things relate.</summary>
        Relationship,
        /// <summary>Change over time.</summary>
        Timeline,
        /// <summary>Comparing things.</summary>
        Comparison,
        /// <summary>Why or how.</summary>
        Reasoning,
        /// <summary>A broad summary.</summary>
        Overview
    }
}
