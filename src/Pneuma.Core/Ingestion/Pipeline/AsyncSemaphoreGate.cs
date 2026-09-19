namespace Pneuma.Core.Ingestion.Pipeline
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A minimal, fully-owned async concurrency gate backing a single stage or the job pool. It wraps a
    /// <see cref="SemaphoreSlim"/> — whose <see cref="SemaphoreSlim.WaitAsync(CancellationToken)"/> is guaranteed
    /// to consume <b>no</b> permit when the wait is cancelled — and hands back an <b>idempotent</b> releaser that
    /// releases exactly once no matter how many times it is disposed. This replaces the previous opaque
    /// third-party limiter so the acquire/release contract is auditable end-to-end and cannot silently leak a
    /// permit: a cancelled acquire never held one, and every successful acquire's release is guarded against
    /// double-dispose and against releasing a swapped-out (disposed) instance.
    /// </summary>
    internal sealed class AsyncSemaphoreGate
    {
        #region Private-Members

        private readonly SemaphoreSlim _Semaphore;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the gate with the given maximum concurrency (clamped to at least 1).</summary>
        /// <param name="maxConcurrency">Maximum concurrent holders.</param>
        internal AsyncSemaphoreGate(int maxConcurrency)
        {
            int max = maxConcurrency < 1 ? 1 : maxConcurrency;
            _Semaphore = new SemaphoreSlim(max, max);
        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Acquire one permit, waiting if none is free. Returns a releaser to dispose when the work completes.
        /// If the wait is cancelled, throws <see cref="OperationCanceledException"/> and holds no permit.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An idempotent releaser handle.</returns>
        internal async Task<IDisposable> AcquireAsync(CancellationToken token)
        {
            await _Semaphore.WaitAsync(token).ConfigureAwait(false);
            return new Releaser(_Semaphore);
        }

        #endregion

        #region Nested-Types

        private sealed class Releaser : IDisposable
        {
            private SemaphoreSlim? _Semaphore;

            internal Releaser(SemaphoreSlim semaphore)
            {
                _Semaphore = semaphore;
            }

            public void Dispose()
            {
                // Release exactly once: the first Dispose swaps the field to null so a second Dispose is a no-op.
                // Tolerate a semaphore that was disposed (a swapped-out gate) or already at full count so a release
                // can never throw out of a finally block.
                SemaphoreSlim? semaphore = Interlocked.Exchange(ref _Semaphore, null);
                if (semaphore == null) return;
                try
                {
                    semaphore.Release();
                }
                catch (ObjectDisposedException)
                {
                }
                catch (SemaphoreFullException)
                {
                }
            }
        }

        #endregion
    }
}
