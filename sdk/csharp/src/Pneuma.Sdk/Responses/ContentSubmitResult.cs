namespace Pneuma.Sdk.Responses
{
    using Pneuma.Sdk.Models;

    /// <summary>The outcome of one pushed content item.</summary>
    public class ContentSubmitResult
    {
        /// <summary>The item's position in the request (0 for a single push).</summary>
        public int Index { get; set; } = 0;

        /// <summary>201 created, 200 replaced, or the 4xx the item was rejected with.</summary>
        public int StatusCode { get; set; } = 201;

        /// <summary>True when content with the same external key was replaced.</summary>
        public bool Replaced { get; set; } = false;

        /// <summary>The link holding the content, or null when rejected.</summary>
        public SubjectLink? Link { get; set; } = null;

        /// <summary>The queued ingestion job id, or null when rejected.</summary>
        public string? JobId { get; set; } = null;

        /// <summary>Why the item was rejected, or null.</summary>
        public string? Error { get; set; } = null;
    }
}
