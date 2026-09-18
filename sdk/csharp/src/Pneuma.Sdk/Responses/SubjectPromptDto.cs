namespace Pneuma.Sdk.Responses
{
    using Pneuma.Sdk.Enums;

    /// <summary>
    /// A subject's effective prompt for a given key, describing the global baseline, any
    /// subject-level override, and the resolved effective content.
    /// </summary>
    public class SubjectPromptDto
    {
        /// <summary>Stable key identifying the prompt's role (e.g. "ontology.classify", "user.answer").</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Human-readable name of the prompt.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The resolved content in effect for the subject after applying any override.</summary>
        public string EffectiveContent { get; set; } = string.Empty;

        /// <summary>The global (tenant/system) prompt content this key is based on.</summary>
        public string GlobalContent { get; set; } = string.Empty;

        /// <summary>The subject-level override content, or null when no override is set.</summary>
        public string? OverrideContent { get; set; } = null;

        /// <summary>Where the effective content comes from: "Global" or "SubjectOverride".</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>How an override combines with the global prompt (append or replace).</summary>
        public PromptMergeModeEnum MergeMode { get; set; } = PromptMergeModeEnum.Append;
    }
}
