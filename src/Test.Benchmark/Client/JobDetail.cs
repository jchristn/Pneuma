namespace Test.Benchmark.Client
{
    using System.Collections.Generic;

    /// <summary>
    /// <c>GET /v1.0/jobs/{id}</c> response.
    /// </summary>
    public class JobDetail
    {
        #region Public-Members

        /// <summary>
        /// The job.
        /// </summary>
        public JobInfo? Job { get; set; } = null;

        /// <summary>
        /// Per-stage events.
        /// </summary>
        public List<JobEventInfo> Events { get; set; } = new List<JobEventInfo>();

        #endregion
    }
}
