namespace Pneuma.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>The subject's prompt additions in a wizard draft.</summary>
    public class WizardPrompts
    {
        /// <summary>Answering addition.</summary>
        public string? SystemPrompt { get; set; } = null;

        /// <summary>Classification addition.</summary>
        public string? ClassifyPrompt { get; set; } = null;

        /// <summary>Query rewriting addition.</summary>
        public string? RewritePrompt { get; set; } = null;

        /// <summary>Reranking addition.</summary>
        public string? RerankingPrompt { get; set; } = null;

        /// <summary>Prompts to keep verbatim (systemPrompt, classifyPrompt, rewritePrompt, rerankingPrompt).</summary>
        public List<string> Locked { get; set; } = new List<string>();
    }
}
