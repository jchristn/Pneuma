namespace Test.Benchmark.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One choice of an OpenAI chat completion (non-streamed <see cref="Message"/>, streamed <see cref="Delta"/>).
    /// </summary>
    public class OpenAiChoice
    {
        #region Public-Members

        /// <summary>
        /// Choice index.
        /// </summary>
        [JsonPropertyName("index")]
        public int Index { get; set; } = 0;

        /// <summary>
        /// The reply (non-streamed).
        /// </summary>
        [JsonPropertyName("message")]
        public ChatWireMessage? Message { get; set; } = null;

        /// <summary>
        /// The reply fragment (streamed).
        /// </summary>
        [JsonPropertyName("delta")]
        public ChatWireMessage? Delta { get; set; } = null;

        /// <summary>
        /// Why generation stopped.
        /// </summary>
        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; } = null;

        #endregion
    }
}
