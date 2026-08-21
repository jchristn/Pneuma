namespace Pneuma.Core.Models
{
    using System;

    /// <summary>
    /// One measured stage of a chat turn's answer pipeline (for example prompt rewrite, conversation
    /// compaction, a tool call, or the final inference). Ordered stages make up a <see cref="TurnPerformance"/>
    /// and are also persisted individually as <see cref="ChatTurnPerfEvent"/> rows for analytics.
    /// </summary>
    public class TurnPerformanceStage
    {
        #region Public-Members

        /// <summary>Stage name, for example "prompt_rewrite", "compaction", "tool:pneuma_search", "final_inference".</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Stage kind, for example "inference", "retrieval", or "tool".</summary>
        public string Kind { get; set; } = String.Empty;

        /// <summary>Provider that served the stage's model call, when applicable (for example "Ollama", "OpenAI").</summary>
        public string? Provider { get; set; } = null;

        /// <summary>Model that served the stage, when applicable.</summary>
        public string? Model { get; set; } = null;

        /// <summary>Elapsed wall-clock time for the stage, in milliseconds.</summary>
        public double DurationMs { get; set; } = 0;

        /// <summary>Time to the first streamed token for an inference stage, in milliseconds (0 when not applicable).</summary>
        public double TimeToFirstTokenMs { get; set; } = 0;

        /// <summary>Prompt tokens consumed by the stage (0 when not applicable).</summary>
        public int PromptTokens { get; set; } = 0;

        /// <summary>Completion tokens produced by the stage (0 when not applicable).</summary>
        public int CompletionTokens { get; set; } = 0;

        /// <summary>Whether the stage completed successfully.</summary>
        public bool Success { get; set; } = true;

        #endregion
    }
}
