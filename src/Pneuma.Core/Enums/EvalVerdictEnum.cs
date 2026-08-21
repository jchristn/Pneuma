namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The judge's verdict for one evaluated fact: did the produced answer match the expected answer?
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EvalVerdictEnum
    {
        /// <summary>Not yet judged.</summary>
        Unknown,
        /// <summary>The answer fully matches the expected answer.</summary>
        Pass,
        /// <summary>The answer partially matches the expected answer.</summary>
        Partial,
        /// <summary>The answer does not match the expected answer.</summary>
        Fail
    }
}
