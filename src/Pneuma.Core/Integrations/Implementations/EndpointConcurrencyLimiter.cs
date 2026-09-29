namespace Pneuma.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Observability;

    /// <summary>
    /// Bounds concurrent requests per model endpoint across the whole process, so ingestion and query traffic together
    /// never send an endpoint more than its <c>MaxConcurrentRequests</c>. One gate per endpoint key; the gate's size is
    /// fixed when the key is first seen (a changed limit applies after a restart). Waiting time is recorded as
    /// <c>pneuma_model_limiter_wait_seconds</c>. Thread-safe.
    /// </summary>
    public class EndpointConcurrencyLimiter
    {
        #region Public-Members

        /// <summary>The process-wide limiter used by the model client factory.</summary>
        public static EndpointConcurrencyLimiter Shared { get; } = new EndpointConcurrencyLimiter();

        #endregion

        #region Private-Members

        private readonly ConcurrentDictionary<string, SemaphoreSlim> _Gates = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>Wait for a free slot on an endpoint and take it.</summary>
        /// <param name="key">The endpoint key (the runner id).</param>
        /// <param name="maxConcurrent">The endpoint's limit, used when the key is first seen; values below 1 become 1.</param>
        /// <param name="label">Low-cardinality metric label for the endpoint.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A lease that frees the slot when disposed.</returns>
        public async Task<IDisposable> AcquireAsync(string key, int maxConcurrent, string label, CancellationToken token)
        {
            int limit = Math.Max(1, maxConcurrent);
            SemaphoreSlim gate = _Gates.GetOrAdd(key ?? String.Empty, _ => new SemaphoreSlim(limit, limit));
            Stopwatch waited = Stopwatch.StartNew();
            await gate.WaitAsync(token).ConfigureAwait(false);
            waited.Stop();
            PneumaMetrics.RecordModelLimiterWait(label, waited.Elapsed.TotalSeconds);
            return new HostLease(gate);
        }

        #endregion
    }
}
