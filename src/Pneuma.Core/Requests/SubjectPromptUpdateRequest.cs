namespace Pneuma.Core.Requests
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;

    /// <summary>
    /// Request body to set or update a per-subject prompt override.
    /// </summary>
    public class SubjectPromptUpdateRequest
    {
        /// <summary>Override content. When null or empty, the override is treated as a clear (revert to global).</summary>
        public string? Content { get; set; } = null;

        /// <summary>How the override combines with the global default. Defaults to Append.</summary>
        public PromptMergeModeEnum MergeMode { get; set; } = PromptMergeModeEnum.Append;
    }
}
