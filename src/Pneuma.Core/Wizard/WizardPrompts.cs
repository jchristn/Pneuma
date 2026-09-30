namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;

    /// <summary>The subject's prompt additions in a wizard draft. Each is appended to the global prompt it extends.</summary>
    public class WizardPrompts
    {
        #region Public-Members

        /// <summary>Addition to the answering prompt (the subject's system prompt).</summary>
        public string? SystemPrompt { get; set; } = null;

        /// <summary>Addition to the classification prompt.</summary>
        public string? ClassifyPrompt { get; set; } = null;

        /// <summary>Addition to the query rewriting prompt.</summary>
        public string? RewritePrompt { get; set; } = null;

        /// <summary>Addition to the reranking prompt.</summary>
        public string? RerankingPrompt { get; set; } = null;

        /// <summary>Prompts the user locked or edited (systemPrompt, classifyPrompt, rewritePrompt, rerankingPrompt); regeneration keeps them.</summary>
        public List<string> Locked { get; set; } = new List<string>();

        #endregion
    }
}
