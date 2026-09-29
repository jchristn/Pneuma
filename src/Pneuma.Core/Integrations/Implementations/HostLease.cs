namespace Pneuma.Core.Integrations.Implementations
{
    using System;
    using System.Threading;

    /// <summary>A held slot in a <see cref="HostRequestLimiter"/> gate; disposing it frees the slot exactly once.</summary>
    public sealed class HostLease : IDisposable
    {
        #region Private-Members

        private SemaphoreSlim? _Gate;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate a lease over an already-acquired gate.</summary>
        /// <param name="gate">The gate the caller acquired.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="gate"/> is null.</exception>
        public HostLease(SemaphoreSlim gate)
        {
            _Gate = gate ?? throw new ArgumentNullException(nameof(gate));
        }

        #endregion

        #region Public-Methods

        /// <summary>Free the slot. Safe to call more than once.</summary>
        public void Dispose()
        {
            SemaphoreSlim? gate = Interlocked.Exchange(ref _Gate, null);
            gate?.Release();
        }

        #endregion
    }
}
