namespace Test.Shared.Support
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Test message handler that returns a response chosen by a caller-supplied factory and counts how
    /// many times it was invoked. Used to exercise the resilient integration client base (retry,
    /// no-retry-on-write, timeout) without a live downstream service.
    /// </summary>
    public sealed class SequencedHttpMessageHandler : HttpMessageHandler
    {
        #region Private-Members

        private readonly Func<int, HttpResponseMessage> _Factory;
        private int _CallCount;

        #endregion

        #region Public-Members

        /// <summary>Number of times the handler has been invoked.</summary>
        public int CallCount
        {
            get { return _CallCount; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initialize the handler with a per-attempt response factory.</summary>
        /// <param name="factory">Factory receiving the 1-based attempt number and returning the response to send.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is null.</exception>
        public SequencedHttpMessageHandler(Func<int, HttpResponseMessage> factory)
        {
            _Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        #endregion

        #region Protected-Methods

        /// <summary>Return the next factory-produced response and increment the invocation count.</summary>
        /// <param name="request">Request message.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The factory-produced response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int attempt = Interlocked.Increment(ref _CallCount);
            return Task.FromResult(_Factory(attempt));
        }

        #endregion
    }
}
