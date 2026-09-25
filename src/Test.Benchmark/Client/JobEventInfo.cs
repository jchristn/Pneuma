namespace Test.Benchmark.Client
{
    /// <summary>
    /// One per-stage ingestion log event.
    /// </summary>
    public class JobEventInfo
    {
        #region Public-Members

        /// <summary>
        /// Stage name.
        /// </summary>
        public string? Stage { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Message.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Stage duration.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        #endregion
    }
}
