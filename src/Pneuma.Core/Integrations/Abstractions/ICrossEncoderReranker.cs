namespace Pneuma.Core.Integrations.Abstractions
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A dedicated cross-encoder reranking service: scores how well each passage answers a query, so retrieved
    /// passages can be reordered by relevance before answering. This is an alternative to listwise LLM
    /// reranking — a cross-encoder is faster and cheaper per rerank. Implementations target the common
    /// <c>/rerank</c> HTTP contract (query + documents in, per-document relevance scores out).
    /// </summary>
    public interface ICrossEncoderReranker
    {
        /// <summary>Whether a cross-encoder endpoint is configured and usable.</summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Score each passage's relevance to the query. Returns one score per passage, aligned by index
        /// (higher is more relevant), or null when unavailable or the call fails so the caller can fall back.
        /// </summary>
        /// <param name="query">The query.</param>
        /// <param name="passages">The candidate passages, in order.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One relevance score per passage (index-aligned), or null on failure/unavailability.</returns>
        Task<IReadOnlyList<double>?> ScoreAsync(string query, IReadOnlyList<string> passages, CancellationToken token = default);
    }
}
