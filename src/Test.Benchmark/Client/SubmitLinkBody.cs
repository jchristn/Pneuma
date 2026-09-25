namespace Test.Benchmark.Client
{
    using System.Collections.Generic;

    /// <summary>
    /// Body for <c>POST /v1.0/subjects/{id}/links</c>.
    /// </summary>
    public class SubmitLinkBody
    {
        #region Public-Members

        /// <summary>
        /// URL Pneuma downloads.
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Optional title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Labels stamped onto every chunk.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags stamped onto every chunk.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
