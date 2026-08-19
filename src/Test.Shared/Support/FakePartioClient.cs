namespace Test.Shared.Support
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Integrations.Models;

    /// <summary>In-memory Partio fake. Echoes the input as a single chunk plus a summary.</summary>
    public class FakePartioClient : IPartioClient
    {
        /// <inheritdoc />
        public Task<PartioProcessResult> ProcessAsync(string text, bool summarize, string? summarizationPrompt = null, string? embeddingEndpointId = null, string? completionEndpointId = null, CancellationToken token = default)
        {
            PartioProcessResult result = new PartioProcessResult
            {
                Chunks = new List<PartioChunk> { new PartioChunk { Text = text, Embeddings = new List<float> { 0.1f, 0.2f } } }
            };
            if (summarize)
            {
                result.Summary = "summary of: " + text;
                result.SummaryChunks = new List<PartioChunk> { new PartioChunk { Text = result.Summary } };
            }
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public Task<List<PartioEndpoint>> ListEmbeddingEndpointsAsync(CancellationToken token = default)
        {
            List<PartioEndpoint> endpoints = new List<PartioEndpoint>
            {
                new PartioEndpoint { Id = "default", Name = "fake-embed", Model = "fake-embed", Active = true }
            };
            return Task.FromResult(endpoints);
        }

        /// <inheritdoc />
        public Task<List<PartioEndpoint>> ListCompletionEndpointsAsync(CancellationToken token = default)
        {
            List<PartioEndpoint> endpoints = new List<PartioEndpoint>
            {
                // A dead URL so ingestion tests' classification fails fast (connection refused) and
                // degrades to an empty subgraph deterministically instead of hitting a real model.
                new PartioEndpoint { Id = "default", Name = "fake-complete", Model = "fake-complete", ApiFormat = "Ollama", Endpoint = "http://127.0.0.1:1", Active = true }
            };
            return Task.FromResult(endpoints);
        }

        /// <inheritdoc />
        public Task<PartioEndpoint?> ReadEndpointAsync(string type, string id, CancellationToken token = default)
        {
            PartioEndpoint? endpoint = new PartioEndpoint { Id = id, Name = "fake-" + type, Model = "fake-" + type, ApiFormat = "Ollama", Endpoint = "http://127.0.0.1:11434", Active = true };
            return Task.FromResult<PartioEndpoint?>(endpoint);
        }

        /// <inheritdoc />
        public Task<PartioEndpoint> CreateEndpointAsync(string type, PartioEndpoint endpoint, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(endpoint.Id)) endpoint.Id = "fake-" + type;
            return Task.FromResult(endpoint);
        }

        /// <inheritdoc />
        public Task<PartioEndpoint> UpdateEndpointAsync(string type, string id, PartioEndpoint endpoint, CancellationToken token = default)
        {
            endpoint.Id = id;
            return Task.FromResult(endpoint);
        }

        /// <inheritdoc />
        public Task DeleteEndpointAsync(string type, string id, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }
    }
}
