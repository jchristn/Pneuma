namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>What happens to an element whose type the version does not declare.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UndeclaredTypeActionEnum
    {
        /// <summary>Keep it.</summary>
        Allow,
        /// <summary>Keep it with a warning.</summary>
        Warn,
        /// <summary>Leave it out.</summary>
        Drop,
        /// <summary>Hold it for review.</summary>
        Quarantine
    }
}
