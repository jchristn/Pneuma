namespace Test.Shared.Support
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Interfaces;

    /// <summary>A content fetcher that always throws the exception built by its factory, counting its calls.</summary>
    public class ThrowingContentFetcher : IContentFetcher
    {
        #region Public-Members

        /// <summary>Number of fetches attempted.</summary>
        public int Calls => Volatile.Read(ref _Calls);

        #endregion

        #region Private-Members

        private readonly Func<Exception> _Factory;
        private int _Calls = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the fetcher.</summary>
        /// <param name="factory">Builds the exception to throw on each fetch.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is null.</exception>
        public ThrowingContentFetcher(Func<Exception> factory)
        {
            _Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<byte[]> FetchAsync(string url, CancellationToken token = default)
        {
            Interlocked.Increment(ref _Calls);
            throw _Factory();
        }

        #endregion
    }
}
