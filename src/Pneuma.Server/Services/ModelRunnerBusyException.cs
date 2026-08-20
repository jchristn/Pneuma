namespace Pneuma.Server.Services
{
    using System;

    /// <summary>
    /// Thrown by <see cref="ModelRunnerGate"/> when a model-runner request cannot be admitted because the
    /// concurrency limit is reached and the wait queue is already full. Callers translate this to an HTTP
    /// 429 (Too Many Requests) response.
    /// </summary>
    public class ModelRunnerBusyException : Exception
    {
        #region Public-Members

        /// <summary>Configured maximum number of concurrent model-runner requests.</summary>
        public int MaxConcurrentRequests { get; }

        /// <summary>Configured maximum queue depth before requests are rejected.</summary>
        public int MaxQueueDepth { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the exception.</summary>
        /// <param name="maxConcurrentRequests">Configured concurrency cap.</param>
        /// <param name="maxQueueDepth">Configured queue-depth cap.</param>
        public ModelRunnerBusyException(int maxConcurrentRequests, int maxQueueDepth)
            : base("The model runner is at capacity (" + maxConcurrentRequests + " concurrent, " + maxQueueDepth + " queued). Try again shortly.")
        {
            MaxConcurrentRequests = maxConcurrentRequests;
            MaxQueueDepth = maxQueueDepth;
        }

        #endregion
    }
}
