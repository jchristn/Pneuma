namespace Pneuma.Core.Responses
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;

    /// <summary>
    /// A prompt as seen for a single subject: its global default, the effective content after any per-subject
    /// override, and whether an override is in effect. Backs the dashboard's subject-scoped prompt view.
    /// </summary>
    public class SubjectPromptDto
    {
        /// <summary>Prompt key (e.g. "cell.summarize").</summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>Human-readable prompt name.</summary>
        public string? Name { get; set; } = null;

        /// <summary>Effective content the subject will use (global combined with any override and legacy addendum).</summary>
        public string EffectiveContent { get; set; } = String.Empty;

        /// <summary>The global (or tenant) default content.</summary>
        public string GlobalContent { get; set; } = String.Empty;

        /// <summary>The raw per-subject override content, or null when the subject inherits the global default.</summary>
        public string? OverrideContent { get; set; } = null;

        /// <summary>Whether the effective content comes from the global default or a per-subject override.</summary>
        public PromptSourceEnum Source { get; set; } = PromptSourceEnum.Global;

        /// <summary>The merge mode applied to the override (meaningful only when an override exists).</summary>
        public PromptMergeModeEnum MergeMode { get; set; } = PromptMergeModeEnum.Append;
    }
}
