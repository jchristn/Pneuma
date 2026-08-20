namespace Pneuma.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A process-wide bulkhead around model-runner usage. Caps the number of model-runner requests that may
    /// run concurrently; requests beyond the cap queue up to a bounded depth, and a request that arrives when
    /// the queue is already full is rejected immediately with <see cref="ModelRunnerBusyException"/> (which
    /// callers translate to HTTP 429). This protects the answering model from being overwhelmed by bursts on
    /// the grounded-query and agentic-chat endpoints while still letting a reasonable backlog wait its turn.
    /// </summary>
    public sealed class ModelRunnerGate : IDisposable
    {
        #region Private-Members

        private readonly SemaphoreSlim _Semaphore;
        private readonly int _MaxConcurrentRequests;
        private readonly int _MaxQueueDepth;
        private int _WaitingCount;
        private bool _Disposed;

        #endregion

        #region Public-Members

        /// <summary>Configured maximum number of concurrent model-runner requests.</summary>
        public int MaxConcurrentRequests { get { return _MaxConcurrentRequests; } }

        /// <summary>Configured maximum queue depth before requests are rejected with 429.</summary>
        public int MaxQueueDepth { get { return _MaxQueueDepth; } }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the gate.</summary>
        /// <param name="maxConcurrentRequests">Maximum concurrent model-runner requests (clamped to [1, 1024]).</param>
        /// <param name="maxQueueDepth">Maximum queued (waiting) requests before rejection (clamped to [0, 100000]).</param>
        public ModelRunnerGate(int maxConcurrentRequests, int maxQueueDepth)
        {
            _MaxConcurrentRequests = Math.Clamp(maxConcurrentRequests, 1, 1024);
            _MaxQueueDepth = Math.Clamp(maxQueueDepth, 0, 100000);
            _Semaphore = new SemaphoreSlim(_MaxConcurrentRequests, _MaxConcurrentRequests);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Acquire a slot, waiting in a bounded queue if the concurrency limit is currently reached. The
        /// returned lease must be disposed to release the slot.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A disposable lease that releases the slot on <see cref="IDisposable.Dispose"/>.</returns>
        /// <exception cref="ModelRunnerBusyException">Thrown when the concurrency limit is reached and the queue is full.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the wait is cancelled.</exception>
        public async Task<IDisposable> AcquireAsync(CancellationToken token = default)
        {
            // Fast path: a slot is immediately available, so no queueing is needed.
            if (_Semaphore.Wait(0))
            {
                return new Lease(this);
            }

            // Slots are full — attempt to enter the bounded wait queue.
            int waiting = Interlocked.Increment(ref _WaitingCount);
            if (waiting > _MaxQueueDepth)
            {
                Interlocked.Decrement(ref _WaitingCount);
                throw new ModelRunnerBusyException(_MaxConcurrentRequests, _MaxQueueDepth);
            }

            try
            {
                await _Semaphore.WaitAsync(token).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _WaitingCount);
            }

            return new Lease(this);
        }

        /// <summary>Release the semaphore and dispose it.</summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _Semaphore.Dispose();
        }

        #endregion

        #region Private-Methods

        private void Release()
        {
            _Semaphore.Release();
        }

        #endregion

        #region Private-Types

        private sealed class Lease : IDisposable
        {
            private ModelRunnerGate? _Gate;

            public Lease(ModelRunnerGate gate)
            {
                _Gate = gate;
            }

            public void Dispose()
            {
                ModelRunnerGate? gate = Interlocked.Exchange(ref _Gate, null);
                gate?.Release();
            }
        }

        #endregion
    }
}
