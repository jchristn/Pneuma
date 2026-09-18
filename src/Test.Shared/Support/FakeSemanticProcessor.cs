namespace Test.Shared.Support
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Models;

    /// <summary>In-memory semantic processor fake. Echoes the input as a single chunk plus a summary.</summary>
    public class FakeSemanticProcessor : ISemanticProcessor
    {
        /// <inheritdoc />
        public Task<SemanticProcessResult> ProcessAsync(string text, bool summarize, string? summarizationPrompt = null, string? embeddingEndpointId = null, string? completionEndpointId = null, CancellationToken token = default)
        {
            SemanticProcessResult result = new SemanticProcessResult
            {
                Chunks = new List<SemanticChunk> { new SemanticChunk { Text = text, Embeddings = new List<float> { 0.1f, 0.2f } } }
            };
            if (summarize)
            {
                result.Summary = "summary of: " + text;
                result.SummaryChunks = new List<SemanticChunk> { new SemanticChunk { Text = result.Summary } };
            }
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public Task<string> SummarizeAsync(string text, string? summarizationPrompt = null, string? completionEndpointId = null, CancellationToken token = default)
        {
            return Task.FromResult(string.IsNullOrWhiteSpace(text) ? string.Empty : "summary of: " + text);
        }

        /// <inheritdoc />
        public Task<List<SemanticChunk>> ChunkAsync(string text, ChunkingOptions? options = null, CancellationToken token = default)
        {
            List<SemanticChunk> chunks = new List<SemanticChunk>();
            if (!string.IsNullOrWhiteSpace(text)) chunks.Add(new SemanticChunk { Text = text });
            return Task.FromResult(chunks);
        }

        /// <inheritdoc />
        public Task<List<List<float>>> EmbedAsync(List<string> texts, string? embeddingEndpointId = null, CancellationToken token = default)
        {
            List<List<float>> vectors = new List<List<float>>();
            if (texts != null)
            {
                foreach (string text in texts) vectors.Add(new List<float> { 0.1f, 0.2f });
            }
            return Task.FromResult(vectors);
        }
    }
}
