namespace Test.Shared.Support
{
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Interfaces;

    /// <summary>Content fetcher that returns fixed bytes, for deterministic ingestion tests.</summary>
    public class FakeContentFetcher : IContentFetcher
    {
        private readonly byte[] _Bytes;

        /// <summary>Instantiate with the given text as the fetched content.</summary>
        /// <param name="content">Content to return for any URL.</param>
        public FakeContentFetcher(string content = "Example Subject was founded in 2010 and is headquartered in Example City.")
        {
            _Bytes = Encoding.UTF8.GetBytes(content);
        }

        /// <inheritdoc />
        public Task<byte[]> FetchAsync(string url, CancellationToken token = default)
        {
            return Task.FromResult(_Bytes);
        }
    }
}
