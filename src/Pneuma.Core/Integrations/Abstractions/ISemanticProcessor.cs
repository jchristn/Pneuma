namespace Pneuma.Core.Integrations.Abstractions
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// Provider-neutral semantic processor. <see cref="ProcessAsync"/> rolls chunking, embedding, and
    /// summarization into one call; <see cref="SummarizeAsync"/>, <see cref="ChunkAsync"/>, and
    /// <see cref="EmbedAsync"/> expose the same operations as discrete steps so a pipeline can run and time
    /// each independently. Backed today by Partio.
    /// </summary>
    public interface ISemanticProcessor
    {
        /// <summary>Summarize a cell of text through a completion endpoint. Returns the summary, or empty when none was produced.</summary>
        /// <param name="text">Cell text.</param>
        /// <param name="summarizationPrompt">Optional summarization prompt template; when null the backend default is used.</param>
        /// <param name="completionEndpointId">Optional completion endpoint id; when null the endpoint is resolved server-side.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The summary text (empty when none was produced).</returns>
        Task<string> SummarizeAsync(string text, string? summarizationPrompt = null, string? completionEndpointId = null, CancellationToken token = default);

        /// <summary>Chunk a cell of text into text chunks, without embedding them.</summary>
        /// <param name="text">Cell text.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The produced chunks (no embeddings).</returns>
        Task<List<PartioChunk>> ChunkAsync(string text, CancellationToken token = default);

        /// <summary>Embed a batch of texts, returning one vector per input in the same order.</summary>
        /// <param name="texts">Texts to embed.</param>
        /// <param name="embeddingEndpointId">Optional embedding endpoint id; when null the endpoint is resolved server-side.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One embedding vector per input, in order.</returns>
        Task<List<List<float>>> EmbedAsync(List<string> texts, string? embeddingEndpointId = null, CancellationToken token = default);

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
