namespace Pneuma.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Process-wide concurrency gates (bulkheads) keyed by logical service name. Caps the number of
    /// concurrent outbound requests to any one downstream integration (DocumentAtom, Partio, RecallDB,
    /// LiteGraph) so a slow or flapping service cannot exhaust threads or overwhelm the dependency.
    /// The gate for a service adapts to the most recently supplied maximum concurrency.
    /// </summary>
    internal static class IntegrationConcurrencyGate
    {
        #region Private-Members

        private static readonly object _SyncRoot = new object();
        private static readonly Dictionary<string, GateState> _Gates = new Dictionary<string, GateState>(StringComparer.Ordinal);

        #endregion

        #region Internal-Methods

        internal static async Task<TResult> ExecuteAsync<TResult>(
            string serviceName,
            int maxConcurrency,
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(serviceName)) throw new ArgumentNullException(nameof(serviceName));
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            GateState gate = ResolveGate(serviceName.Trim());
            using (IDisposable lease = await gate.AcquireAsync(maxConcurrency, token).ConfigureAwait(false))
            {
                return await operation(token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static GateState ResolveGate(string serviceName)
        {
            lock (_SyncRoot)
            {
                if (_Gates.TryGetValue(serviceName, out GateState? existing)) return existing;
                GateState created = new GateState();
                _Gates[serviceName] = created;
                return created;
            }
        }

        #endregion

        #region Private-Types

        private sealed class GateState
        {
            private readonly object _SyncRoot = new object();
            private int _ActiveCount = 0;
            private int _MaxConcurrency = 1;
            private TaskCompletionSource<object?> _Signal = CreateSignal();

            public async Task<IDisposable> AcquireAsync(int maxConcurrency, CancellationToken token)
            {
                int normalizedMaxConcurrency = Math.Max(1, maxConcurrency);

                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    Task waitTask;
                    TaskCompletionSource<object?>? signalToComplete = null;
                    lock (_SyncRoot)
                    {
                        if (_MaxConcurrency != normalizedMaxConcurrency)
                        {
                            _MaxConcurrency = normalizedMaxConcurrency;
                            signalToComplete = _Signal;
                            _Signal = CreateSignal();
                        }

                        if (_ActiveCount < _MaxConcurrency)
                        {
                            _ActiveCount += 1;
                            signalToComplete?.TrySetResult(null);
                            return new GateLease(this);
                        }

                        waitTask = _Signal.Task;
                    }

                    signalToComplete?.TrySetResult(null);
                    await waitTask.WaitAsync(token).ConfigureAwait(false);
                }
            }

            public void Release()
            {
                TaskCompletionSource<object?> signalToComplete;
                lock (_SyncRoot)
                {
                    _ActiveCount = Math.Max(0, _ActiveCount - 1);
                    signalToComplete = _Signal;
                    _Signal = CreateSignal();
                }

                signalToComplete.TrySetResult(null);
            }

            private static TaskCompletionSource<object?> CreateSignal()
            {
                return new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        private sealed class GateLease : IDisposable
        {
            private GateState? _Gate;

            public GateLease(GateState gate)
            {
                _Gate = gate;
            }

            public void Dispose()
            {
                GateState? gate = Interlocked.Exchange(ref _Gate, null);
                gate?.Release();
            }
        }

        #endregion
    }
}
