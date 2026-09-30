namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>Who wrote a subject's example question.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SubjectQuestionOriginEnum
    {
        /// <summary>Drafted by a model and kept as written.</summary>
        Model,
        /// <summary>Written or edited by a person.</summary>
        User
    }
}
