namespace Pneuma.Core.Responses
{
    using Pneuma.Core.Models;

    /// <summary>The outcome of one pushed content item.</summary>
    public class ContentSubmitResult
    {
        #region Public-Members

        /// <summary>The item's position in the request (0 for a single push).</summary>
        public int Index { get; set; } = 0;

        /// <summary>The status the item would have had on its own: 201 created, 200 replaced, or a 4xx.</summary>
        public int StatusCode { get; set; } = 201;

        /// <summary>True when an existing item with the same external key was replaced.</summary>
        public bool Replaced { get; set; } = false;

        /// <summary>The link that holds the content, or null when the item was rejected.</summary>
        public SubjectLink? Link { get; set; } = null;

        /// <summary>The ingestion job queued for the content, or null when the item was rejected.</summary>
        public string? JobId { get; set; } = null;

        /// <summary>Why the item was rejected, or null.</summary>
        public string? Error { get; set; } = null;

        #endregion
    }
}
