namespace Test.Benchmark.Agent
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A Claude Code MCP configuration file.
    /// </summary>
    public class McpConfigFile
    {
        #region Public-Members

        /// <summary>
        /// Server name to entry.
        /// </summary>
        [JsonPropertyName("mcpServers")]
        public Dictionary<string, McpServerEntry> McpServers { get; set; } = new Dictionary<string, McpServerEntry>();

        #endregion
    }
}
