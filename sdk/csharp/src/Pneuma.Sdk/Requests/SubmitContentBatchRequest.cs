namespace Pneuma.Sdk.Requests
{
    using System.Collections.Generic;

    /// <summary>Several content items pushed at once (at most 100).</summary>
    public class SubmitContentBatchRequest
    {
        /// <summary>The items.</summary>
        public List<SubmitContentRequest> Items { get; set; } = new List<SubmitContentRequest>();
    }
}
