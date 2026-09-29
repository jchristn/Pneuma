namespace Pneuma.Sdk.Responses
{
    using System.Collections.Generic;

    /// <summary>The outcome of a content batch: one result per item, in request order.</summary>
    public class ContentBatchResponse
    {
        /// <summary>Items accepted (created or replaced).</summary>
        public int Accepted { get; set; } = 0;

        /// <summary>Items rejected.</summary>
        public int Rejected { get; set; } = 0;

        /// <summary>One result per item.</summary>
        public List<ContentSubmitResult> Results { get; set; } = new List<ContentSubmitResult>();
    }
}
