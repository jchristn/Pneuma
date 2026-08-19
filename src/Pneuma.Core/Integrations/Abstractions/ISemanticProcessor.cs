namespace Pneuma.Core.Integrations.Abstractions
{
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// Provider-neutral semantic processor: turns a cell of text into chunks and embeddings and,
    /// optionally, a summary (and chunks of that summary). Rolls chunking, embedding, and
    /// summarization into one call. Backed today by Partio.
    /// </summary>
    public interface ISemanticProcessor
    {
        /// <summary>
        /// Process a cell of text: chunk and embed it, and (when a completion endpoint is available)
        /// summarize it and chunk the summary.
        /// </summary>
        /// <param name="text">Cell text.</param>
        /// <param name="summarize">Whether to summarize.</param>
        /// <param name="summarizationPrompt">Optional summarization prompt template; when null the backend default is used.</param>
        /// <param name="embeddingEndpointId">Optional embedding endpoint id; when null the endpoint is resolved server-side.</param>
        /// <param name="completionEndpointId">Optional completion endpoint id; when null the endpoint is resolved server-side.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The process result.</returns>
        Task<PartioProcessResult> ProcessAsync(string text, bool summarize, string? summarizationPrompt = null, string? embeddingEndpointId = null, string? completionEndpointId = null, CancellationToken token = default);
    }
}
