namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A user's rating of a single chat answer.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum FeedbackRatingEnum
    {
        /// <summary>No explicit rating (a comment-only submission).</summary>
        None,
        /// <summary>Thumbs up — the answer was helpful.</summary>
        Up,
        /// <summary>Thumbs down — the answer was not helpful.</summary>
        Down
    }
}
