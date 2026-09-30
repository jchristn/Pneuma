namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>What happens to an element that breaks a rule.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OntologyRuleActionEnum
    {
        /// <summary>Keep it and record a warning.</summary>
        Warn,
        /// <summary>Leave it out.</summary>
        Drop,
        /// <summary>Hold it for review.</summary>
        Quarantine,
        /// <summary>Reverse the edge when that satisfies an endpoint rule.</summary>
        Reverse
    }
}
