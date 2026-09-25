namespace Test.Benchmark.Agent
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The final JSON object <c>claude -p --output-format json</c> prints.
    /// </summary>
    public class ClaudeResult
    {
        #region Public-Members

        /// <summary>
        /// The final answer text.
        /// </summary>
        [JsonPropertyName("result")]
        public string? Result { get; set; } = null;

        /// <summary>
        /// Turns taken.
        /// </summary>
        [JsonPropertyName("num_turns")]
        public int NumTurns { get; set; } = 0;

        /// <summary>
        /// Cost in USD.
        /// </summary>
        [JsonPropertyName("total_cost_usd")]
        public double TotalCostUsd { get; set; } = 0.0;

        /// <summary>
        /// True when the run ended in error.
        /// </summary>
        [JsonPropertyName("is_error")]
        public bool IsError { get; set; } = false;

        /// <summary>
        /// Result subtype (success, error_max_turns, ...).
        /// </summary>
        [JsonPropertyName("subtype")]
        public string? Subtype { get; set; } = null;

        #endregion
    }
}
