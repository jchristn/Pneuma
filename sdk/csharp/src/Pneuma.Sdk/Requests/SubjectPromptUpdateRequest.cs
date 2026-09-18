namespace Pneuma.Sdk.Requests
{
    using Pneuma.Sdk.Enums;

    /// <summary>
    /// Request to set (create or update) a subject-level prompt override for a given key.
    /// </summary>
    public class SubjectPromptUpdateRequest
    {
        /// <summary>The override content to store for the subject.</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>How the override combines with the global prompt (append or replace).</summary>
        public PromptMergeModeEnum MergeMode { get; set; } = PromptMergeModeEnum.Append;
    }
}
