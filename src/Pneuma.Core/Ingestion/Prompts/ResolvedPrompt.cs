namespace Pneuma.Core.Ingestion.Prompts
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;

    /// <summary>
    /// The effective content of a prompt after applying any per-subject override and legacy per-subject
    /// addendum on top of the global default.
    /// </summary>
    public class ResolvedPrompt
    {
        /// <summary>Effective prompt content to send to the model.</summary>
        public string EffectiveContent { get; set; } = String.Empty;

        /// <summary>The global (or tenant) default content, before any override.</summary>
        public string GlobalContent { get; set; } = String.Empty;

        /// <summary>Whether the effective content came from the global default or a per-subject override.</summary>
        public PromptSourceEnum Source { get; set; } = PromptSourceEnum.Global;

        /// <summary>The merge mode applied when a subject override is present.</summary>
        public PromptMergeModeEnum MergeMode { get; set; } = PromptMergeModeEnum.Append;
    }
}
