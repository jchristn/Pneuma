namespace Test.Benchmark.Client
{
    using System;

    /// <summary>
    /// A content link (one submitted document).
    /// </summary>
    public class LinkInfo
    {
        #region Public-Members

        /// <summary>
        /// Link id.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Owning subject.
        /// </summary>
        public string? SubjectId { get; set; } = null;

        /// <summary>
        /// Source URL (the harness corpus server URL, which ends in the document id).
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Submitted, Processing, Ingested, or Failed.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Last error, when failed.
        /// </summary>
        public string? LastError { get; set; } = null;

        /// <summary>
        /// Last successful ingestion time.
        /// </summary>
        public DateTime? LastIngestedUtc { get; set; } = null;

        #endregion
    }
}
