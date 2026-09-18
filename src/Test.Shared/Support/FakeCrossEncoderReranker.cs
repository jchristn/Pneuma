namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Abstractions;

    /// <summary>
    /// Test cross-encoder reranker that returns scores from a supplied scoring function, so a test can drive a
    /// deterministic reordering (or a null result to exercise the fallback path).
    /// </summary>
    public class FakeCrossEncoderReranker : ICrossEncoderReranker
    {
        private readonly Func<string, IReadOnlyList<string>, IReadOnlyList<double>?> _Score;

        /// <summary>Instantiate with a scoring function mapping (query, passages) to a per-passage score list (or null).</summary>
        /// <param name="score">The scoring function.</param>
        public FakeCrossEncoderReranker(Func<string, IReadOnlyList<string>, IReadOnlyList<double>?> score)
        {
            _Score = score;
        }

        /// <inheritdoc />
        public bool IsConfigured { get { return true; } }

        /// <inheritdoc />
        public Task<IReadOnlyList<double>?> ScoreAsync(string query, IReadOnlyList<string> passages, CancellationToken token = default)
        {
            return Task.FromResult(_Score(query, passages));
        }
    }
}
