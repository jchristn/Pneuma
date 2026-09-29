namespace Test.Shared.Support
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// A semantic processor that behaves like <see cref="FakeSemanticProcessor"/> but returns an empty vector for the
    /// last text of every embedding batch, to exercise the "chunk without an embedding" path.
    /// </summary>
    public class PartialEmbeddingSemanticProcessor : ISemanticProcessor
    {
        #region Private-Members

        private readonly FakeSemanticProcessor _Inner = new FakeSemanticProcessor();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<SemanticProcessResult> ProcessAsync(string text, bool summarize, string? summarizationPrompt = null, string? embeddingEndpointId = null, string? completionEndpointId = null, CancellationToken token = default)
        {
            return _Inner.ProcessAsync(text, summarize, summarizationPrompt, embeddingEndpointId, completionEndpointId, token);
        }

        /// <inheritdoc />
        public Task<string> SummarizeAsync(string text, string? summarizationPrompt = null, string? completionEndpointId = null, CancellationToken token = default)
        {
            return _Inner.SummarizeAsync(text, summarizationPrompt, completionEndpointId, token);
        }

        /// <inheritdoc />
        public Task<List<SemanticChunk>> ChunkAsync(string text, ChunkingOptions? options = null, CancellationToken token = default)
        {
            return _Inner.ChunkAsync(text, options, token);
        }

        /// <inheritdoc />
        public async Task<List<List<float>>> EmbedAsync(List<string> texts, string? embeddingEndpointId = null, CancellationToken token = default)
        {
            List<List<float>> vectors = await _Inner.EmbedAsync(texts, embeddingEndpointId, token).ConfigureAwait(false);
            if (vectors.Count > 0) vectors[vectors.Count - 1] = new List<float>();
            return vectors;
        }

        #endregion
    }
}
