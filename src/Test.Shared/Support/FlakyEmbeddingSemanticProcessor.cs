namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// A semantic processor that behaves like <see cref="FakeSemanticProcessor"/> but fails the first N embedding
    /// calls with a transient error, to exercise retries that re-run every stage.
    /// </summary>
    public class FlakyEmbeddingSemanticProcessor : ISemanticProcessor
    {
        #region Private-Members

        private readonly FakeSemanticProcessor _Inner = new FakeSemanticProcessor();
        private int _FailuresRemaining;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the processor.</summary>
        /// <param name="failFirst">Embedding calls to fail before succeeding. Minimum 0.</param>
        public FlakyEmbeddingSemanticProcessor(int failFirst)
        {
            _FailuresRemaining = Math.Max(0, failFirst);
        }

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
        public Task<List<List<float>>> EmbedAsync(List<string> texts, string? embeddingEndpointId = null, CancellationToken token = default)
        {
            if (Interlocked.Decrement(ref _FailuresRemaining) >= 0) throw new InvalidOperationException("simulated transient embedding failure");
            return _Inner.EmbedAsync(texts, embeddingEndpointId, token);
        }

        #endregion
    }
}
