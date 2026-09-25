namespace Test.Benchmark.Agent
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One MCP server in a Claude Code MCP configuration.
    /// </summary>
    public class McpServerEntry
    {
        #region Public-Members

        /// <summary>
        /// Transport type (http).
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "http";

        /// <summary>
        /// Endpoint URL.
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Request headers (authentication).
        /// </summary>
        [JsonPropertyName("headers")]
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
