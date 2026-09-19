namespace Pneuma.Core.Ingestion.Pipeline
{
    using System;

    /// <summary>
    /// Raised when an operator cancels a job mid-flight (its status was set to Cancelled out-of-band) so the
    /// pipeline stops cleanly without marking the job failed or retrying it. Detected at the start of each stage.
    /// </summary>
    public class JobCancelledException : Exception
    {
        /// <summary>Instantiate the exception.</summary>
        public JobCancelledException() : base("Ingestion job was cancelled by the operator.")
        {
        }
    }
}
