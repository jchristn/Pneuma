namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>The kind of an example question, which tells the ontology what structure answering it needs.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SubjectQuestionKindEnum
    {
        /// <summary>A single fact (who, what, when, where).</summary>
        Fact,
        /// <summary>How people, works, organizations, or other things relate.</summary>
        Relationship,
        /// <summary>How something changed or happened over time.</summary>
        Timeline,
        /// <summary>Comparing two or more things.</summary>
        Comparison,
        /// <summary>Why or how; needs several facts combined.</summary>
        Reasoning,
        /// <summary>A broad summary of a theme or the whole subject.</summary>
        Overview
    }
}
