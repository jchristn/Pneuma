namespace Test.Benchmark.Client
{
    using System;

    /// <summary>
    /// An ingestion job.
    /// </summary>
    public class JobInfo
    {
        #region Public-Members

        /// <summary>
        /// Job id.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Link the job ingests.
        /// </summary>
        public string? LinkId { get; set; } = null;

        /// <summary>
        /// Subject.
        /// </summary>
        public string? SubjectId { get; set; } = null;

        /// <summary>
        /// Queued, Processing, Completed, Failed, or Cancelled.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Current or last stage.
        /// </summary>
        public string? Stage { get; set; } = null;

        /// <summary>
        /// Error, when failed.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Attempts made.
        /// </summary>
        public int AttemptCount { get; set; } = 0;

        /// <summary>
        /// Creation time.
        /// </summary>
        public DateTime? CreatedUtc { get; set; } = null;

        /// <summary>
        /// Completion time.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        #endregion
    }
}
