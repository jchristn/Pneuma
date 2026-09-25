namespace Test.Benchmark.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Ollama <c>/api/chat</c> response (one line of a stream, or the whole non-streamed reply).
    /// </summary>
    public class OllamaChatResponse
    {
        #region Public-Members

        /// <summary>
        /// Model name.
        /// </summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; } = null;

        /// <summary>
        /// Creation time.
        /// </summary>
        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; } = null;

        /// <summary>
        /// The reply.
        /// </summary>
        [JsonPropertyName("message")]
        public ChatWireMessage? Message { get; set; } = null;

        /// <summary>
        /// True on the final line.
        /// </summary>
        [JsonPropertyName("done")]
        public bool Done { get; set; } = true;

        /// <summary>
        /// Why generation stopped.
        /// </summary>
        [JsonPropertyName("done_reason")]
        public string? DoneReason { get; set; } = null;

        /// <summary>
        /// Prompt tokens.
        /// </summary>
        [JsonPropertyName("prompt_eval_count")]
        public int PromptEvalCount { get; set; } = 0;

        /// <summary>
        /// Generated tokens.
        /// </summary>
        [JsonPropertyName("eval_count")]
        public int EvalCount { get; set; } = 0;

        #endregion
    }
}
