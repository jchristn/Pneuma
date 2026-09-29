namespace Pneuma.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>Several content items pushed at once (<c>POST /v1.0/subjects/{id}/content/batch</c>), at most 100.</summary>
    public class SubmitContentBatchRequest
    {
        #region Public-Members

        /// <summary>The items.</summary>
        public List<SubmitContentRequest>? Items { get; set; } = null;

        #endregion
    }
}
