namespace Pneuma.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Integrations;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Security;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;
    using TextChunker.Chunking;
    using TextChunker.Enums;

    /// <summary>
    /// Native <see cref="ISemanticProcessor"/> that chunks, embeds, and summarizes inside Pneuma, resolving
    /// model endpoints from the model-runner store and calling providers directly through PolyPrompt. Replaces the prior
    /// external processor. Chunking uses the TextChunker library, which counts tokens in the embedding model's own
    /// tokenizer when a model id is supplied (cl100k_base otherwise); embeddings are L2-normalized so cosine
    /// retrieval is stable.
    /// </summary>
    public class NativeSemanticProcessor : ISemanticProcessor
    {
        #region Private-Members

        private const int _EmbedBatchSize = 64;
        private const int _SummaryMaxTokens = 1024;

        private readonly DatabaseDriverBase _Database;
        private readonly Aes256Cipher _Cipher;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the native semantic processor.</summary>
        /// <param name="database">Database driver used to resolve model runners.</param>
        /// <param name="cipher">Cipher used to decrypt stored endpoint secrets.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public NativeSemanticProcessor(DatabaseDriverBase database, Aes256Cipher cipher, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<string> SummarizeAsync(string text, string? summarizationPrompt = null, string? completionEndpointId = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(text)) return String.Empty;

            ModelRunner? runner = await ResolveRunnerAsync(completionEndpointId, token).ConfigureAwait(false);
            if (runner == null)
            {
                _Logging.Warn("[NativeSemanticProcessor] no completion endpoint available for summarization");
                return String.Empty;
            }

            CompletionClientBase client = BuildClient(runner);
            string systemPrompt = String.IsNullOrWhiteSpace(summarizationPrompt) ? "Summarize the following content concisely." : summarizationPrompt!;
            ChatCompletionOptions options = new ChatCompletionOptions { Temperature = 0.1, MaxTokens = _SummaryMaxTokens, SystemPrompt = systemPrompt };
            ChatResponse response = await client.ChatAsync(text, options, token).ConfigureAwait(false);
            if (response == null || !response.Success) throw ModelResponseErrors.ToException("summarization", response?.Error);
            return String.IsNullOrWhiteSpace(response.Text) ? String.Empty : response.Text.Trim();
        }

        /// <inheritdoc />
        public async Task<List<SemanticChunk>> ChunkAsync(string text, ChunkingOptions? options = null, CancellationToken token = default)
        {
            List<SemanticChunk> chunks = new List<SemanticChunk>();
            if (String.IsNullOrWhiteSpace(text)) return chunks;

            ChunkingOptions effective = options ?? new ChunkingOptions();

            // Overlap must stay below the chunk size, or no window could advance.
            int overlap = effective.OverlapCount < effective.MaxTokens ? effective.OverlapCount : effective.MaxTokens / 4;
            string? header = String.IsNullOrWhiteSpace(effective.ContextHeader) ? null : effective.ContextHeader!.Trim() + "\n\n";
            TextChunker.Models.ChunkingOptions chunkerOptions = new TextChunker.Models.ChunkingOptions
            {
                Strategy = MapStrategy(effective.Strategy),
                MaxTokens = effective.MaxTokens,
                OverlapCount = overlap,
                ModelId = String.IsNullOrWhiteSpace(effective.ModelId) ? null : effective.ModelId,
                ComputeHashes = false,
                ComputeOffsets = true,
                // A small margin under the model's input limit, because the endpoint's tokenizer can count a few more
                // tokens than the local one; the header's tokens come out of the same budget.
                SafetyMarginPercentage = effective.SafetyMarginPercentage,
                SafetyMarginTokens = effective.SafetyMarginTokens,
                EffectiveInputBudget = effective.EffectiveInputBudget,
                ContextPrefix = header
            };

            Chunker chunker = new Chunker();
            await foreach (TextChunker.Models.Chunk chunk in chunker.ChunkText(text, chunkerOptions, token).ConfigureAwait(false))
            {
                if (String.IsNullOrWhiteSpace(chunk.Text)) continue;
                if (header == null)
                {
                    chunks.Add(new SemanticChunk { Text = chunk.Text });
                    continue;
                }

                // Store the chunk without its header; embed it with the header.
                string stored = chunk.StartOffset >= 0 && chunk.EndOffset > chunk.StartOffset && chunk.EndOffset <= text.Length
                    ? text.Substring(chunk.StartOffset, chunk.EndOffset - chunk.StartOffset)
                    : (chunk.Text.StartsWith(header, StringComparison.Ordinal) ? chunk.Text.Substring(header.Length) : chunk.Text);
                stored = stored.Trim();
                if (String.IsNullOrWhiteSpace(stored)) continue;
                string embedded = chunk.Text.StartsWith(header, StringComparison.Ordinal) ? chunk.Text : header + stored;
                chunks.Add(new SemanticChunk { Text = stored, EmbeddingText = embedded, Header = effective.ContextHeader });
            }

            return chunks;
        }

        /// <inheritdoc />
        public async Task<List<List<float>>> EmbedAsync(List<string> texts, string? embeddingEndpointId = null, CancellationToken token = default)
        {
            List<List<float>> result = new List<List<float>>();
            if (texts == null || texts.Count == 0) return result;

            ModelRunner? runner = await ResolveRunnerAsync(embeddingEndpointId, token).ConfigureAwait(false);
            if (runner == null) throw new InvalidOperationException("No embedding endpoint is available to embed the supplied texts.");

            CompletionClientBase client = BuildClient(runner);
            string? model = String.IsNullOrWhiteSpace(runner.DefaultEmbeddingModel) ? runner.DefaultModel : runner.DefaultEmbeddingModel;
            EmbeddingOptions embeddingOptions = new EmbeddingOptions { Model = model };

            for (int offset = 0; offset < texts.Count; offset += _EmbedBatchSize)
            {
                token.ThrowIfCancellationRequested();
                int count = Math.Min(_EmbedBatchSize, texts.Count - offset);
                List<string> batch = texts.GetRange(offset, count);
                // Transient failures (429, 5xx) are retried by the model client's TransientRetryHandler; what reaches here
                // failed for good, and the typed exception tells the pipeline whether to retry the job later.
                EmbeddingResponse response = await client.EmbedAsync(batch, embeddingOptions, token).ConfigureAwait(false);
                if (response == null || !response.Success) throw ModelResponseErrors.ToException("embedding", response?.Error);
                if (response.Embeddings.Count != batch.Count) throw new InvalidOperationException("Embedding provider returned " + response.Embeddings.Count + " vectors for " + batch.Count + " inputs.");

                List<List<float>> ordered = OrderVectors(response.Embeddings, batch.Count);
                foreach (List<float> vector in ordered) result.Add(vector);
            }
            return result;
        }

        /// <inheritdoc />
        public async Task<SemanticProcessResult> ProcessAsync(string text, bool summarize, string? summarizationPrompt = null, string? embeddingEndpointId = null, string? completionEndpointId = null, CancellationToken token = default)
        {
            SemanticProcessResult result = new SemanticProcessResult();
            if (String.IsNullOrWhiteSpace(text)) return result;

            List<SemanticChunk> chunks = await ChunkAsync(text, null, token).ConfigureAwait(false);
            await EmbedChunksAsync(chunks, embeddingEndpointId, token).ConfigureAwait(false);
            result.Chunks = chunks;

            if (summarize && !String.IsNullOrWhiteSpace(completionEndpointId))
            {
                string summary = await SummarizeAsync(text, summarizationPrompt, completionEndpointId, token).ConfigureAwait(false);
                if (!String.IsNullOrWhiteSpace(summary))
                {
                    result.Summary = summary;
                    List<SemanticChunk> summaryChunks = await ChunkAsync(summary, null, token).ConfigureAwait(false);
                    await EmbedChunksAsync(summaryChunks, embeddingEndpointId, token).ConfigureAwait(false);
                    result.SummaryChunks = summaryChunks;
                }
            }
            return result;
        }

        #endregion

        #region Private-Methods

        private async Task EmbedChunksAsync(List<SemanticChunk> chunks, string? embeddingEndpointId, CancellationToken token)
        {
            if (chunks.Count == 0) return;
            List<string> texts = new List<string>();
            foreach (SemanticChunk chunk in chunks) texts.Add(chunk.Text);
            List<List<float>> vectors = await EmbedAsync(texts, embeddingEndpointId, token).ConfigureAwait(false);
            for (int i = 0; i < chunks.Count && i < vectors.Count; i++) chunks[i].Embeddings = vectors[i];
        }

        private async Task<ModelRunner?> ResolveRunnerAsync(string? endpointId, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(endpointId)) return null;
            return await _Database.ModelRunners.ReadAsync(endpointId!, token).ConfigureAwait(false);
        }

        private CompletionClientBase BuildClient(ModelRunner runner)
        {
            string? apiKey = DecryptOrNull(runner.AuthMaterialEncrypted);
            string? sessionToken = DecryptOrNull(runner.SessionTokenEncrypted);
            return ModelClientFactory.Create(runner, apiKey, _Logging, sessionToken);
        }

        private string? DecryptOrNull(string? payload)
        {
            if (String.IsNullOrEmpty(payload)) return null;
            try { return _Cipher.Decrypt(payload); }
            catch (Exception exception)
            {
                _Logging.Warn("[NativeSemanticProcessor] failed to decrypt endpoint secret: " + exception.Message);
                return null;
            }
        }

        private static ChunkStrategyEnum MapStrategy(string strategy)
        {
            if (String.Equals(strategy, "SentenceBased", StringComparison.OrdinalIgnoreCase)) return ChunkStrategyEnum.SentenceBased;
            if (String.Equals(strategy, "ParagraphBased", StringComparison.OrdinalIgnoreCase)) return ChunkStrategyEnum.ParagraphBased;
            if (String.Equals(strategy, "Recursive", StringComparison.OrdinalIgnoreCase)) return ChunkStrategyEnum.Recursive;
            return ChunkStrategyEnum.FixedTokenCount;
        }

        private static List<List<float>> OrderVectors(List<EmbeddingResult> embeddings, int count)
        {
            List<List<float>> ordered = new List<List<float>>(new List<float>[count]);
            for (int i = 0; i < embeddings.Count; i++)
            {
                EmbeddingResult item = embeddings[i];
                int index = (item.Index >= 0 && item.Index < count) ? item.Index : i;
                ordered[index] = NormalizeL2(item.Embedding);
            }
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i] == null) ordered[i] = new List<float>();
            }
            return ordered;
        }

        private static List<float> NormalizeL2(float[] vector)
        {
            List<float> result = new List<float>(vector.Length);
            double magnitude = 0.0;
            for (int i = 0; i < vector.Length; i++) magnitude += (double)vector[i] * vector[i];
            magnitude = Math.Sqrt(magnitude);
            if (magnitude <= 0.0)
            {
                for (int i = 0; i < vector.Length; i++) result.Add(vector[i]);
                return result;
            }
            for (int i = 0; i < vector.Length; i++) result.Add((float)(vector[i] / magnitude));
            return result;
        }

        #endregion
    }
}
