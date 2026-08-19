namespace Test.Shared.Support
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Test message handler that delays each response by a fixed interval (honoring the cancellation
    /// token, so a per-attempt timeout can cancel it) and tracks the peak number of concurrently
    /// in-flight requests. Used to exercise the resilient client's per-attempt timeout and its
    /// per-service concurrency gate without a live downstream service.
    /// </summary>
    public sealed class DelayingHttpMessageHandler : HttpMessageHandler
    {
        #region Private-Members

        private readonly int _DelayMilliseconds;
        private readonly Func<HttpResponseMessage> _ResponseFactory;
        private int _CallCount;
        private int _CurrentInFlight;
        private int _MaxInFlight;
        private readonly object _Lock = new object();

        #endregion

        #region Public-Members

        /// <summary>Number of times the handler has been invoked.</summary>
        public int CallCount
        {
            get { return _CallCount; }
        }

        /// <summary>Peak number of requests observed in flight at the same time.</summary>
        public int MaxInFlight
        {
            get { return _MaxInFlight; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initialize the handler.</summary>
        /// <param name="delayMilliseconds">Delay applied to each response before it returns.</param>
        /// <param name="responseFactory">Factory producing the response to return once the delay elapses.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="responseFactory"/> is null.</exception>
        public DelayingHttpMessageHandler(int delayMilliseconds, Func<HttpResponseMessage> responseFactory)
        {
            _DelayMilliseconds = delayMilliseconds;
            _ResponseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
        }

        #endregion

        #region Protected-Methods

        /// <summary>Track concurrency, wait the configured delay, then return the factory-produced response.</summary>
        /// <param name="request">Request message.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The factory-produced response.</returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _CallCount);
            int current = Interlocked.Increment(ref _CurrentInFlight);
            lock (_Lock)
            {
                if (current > _MaxInFlight) _MaxInFlight = current;
            }

            try
            {
                await Task.Delay(_DelayMilliseconds, cancellationToken).ConfigureAwait(false);
                return _ResponseFactory();
            }
            finally
            {
                Interlocked.Decrement(ref _CurrentInFlight);
            }
        }

        #endregion
    }
}
