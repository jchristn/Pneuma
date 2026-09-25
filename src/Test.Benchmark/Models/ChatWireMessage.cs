namespace Test.Benchmark.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A chat message in the Ollama and OpenAI wire formats (both use role/content).
    /// </summary>
    public class ChatWireMessage
    {
        #region Public-Members

        /// <summary>
        /// system, user, or assistant.
        /// </summary>
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        /// <summary>
        /// Message text.
        /// </summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; } = null;

        #endregion
    }
}
