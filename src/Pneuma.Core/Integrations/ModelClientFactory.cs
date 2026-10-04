namespace Pneuma.Core.Integrations
{
    using System;
    using System.Net.Http;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations.Implementations;
    using Pneuma.Core.Models;
    using PolyPrompt.Auth;
    using PolyPrompt.Clients;
    using SyslogLogging;

    /// <summary>
    /// Builds PolyPrompt completion and embedding clients for a configured model runner.
    /// </summary>
    public static class ModelClientFactory
    {
        #region Private-Members

        // Connection pooling is shared through one handler (avoids socket exhaustion). Each client still gets its own
        // HttpClient because the retry handler in front of the shared handler is per runner (retry count, endpoint
        // concurrency slot). PolyPrompt attaches credentials to each request, so keys never leak between clients.
        // PolyPrompt enforces its own per-request timeout (TimeoutMs, 120 s by default). Calls are meant to be bounded
        // by the caller's cancellation token instead, so the client timeout is set to at least the default stage
        // timeout; an endpoint configured with a longer MaximumTimeoutMs keeps its longer value.
        private const int _MinimumCallTimeoutMs = 30 * 60 * 1000;

        private static readonly SocketsHttpHandler _Handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a PolyPrompt completion client (chat, tool chat, generation) for a model runner, applying its
        /// default model.
        /// </summary>
        /// <param name="runner">Model runner.</param>
        /// <param name="apiKey">Decrypted primary secret: the API key, Azure AD/Vertex bearer token, or (for Bedrock) the AWS secret access key. May be null for keyless local runners.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="awsSessionToken">Decrypted AWS session token for Bedrock temporary credentials, if any.</param>
        /// <returns>A configured completion client.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="runner"/> or <paramref name="logging"/> is null.</exception>
        /// <exception cref="NotSupportedException">Thrown when the provider is unknown or has no completion API (Voyage AI).</exception>
        /// <exception cref="InvalidOperationException">Thrown when a provider is missing a required field (e.g. Azure deployment, Bedrock/Vertex region).</exception>
        public static CompletionClientBase Create(ModelRunner runner, string? apiKey, LoggingModule logging, string? awsSessionToken = null)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            if (logging == null) throw new ArgumentNullException(nameof(logging));

            HttpClient http = CreateHttpClient(runner);
            string? endpointOrNull = String.IsNullOrWhiteSpace(runner.BaseUrl) ? null : runner.BaseUrl;

            CompletionClientBase client;
            try
            {
                switch (runner.Provider)
                {
                    case ModelRunnerProviderEnum.OpenAI:
                    case ModelRunnerProviderEnum.OpenAICompatible:
                        client = new OpenAiCompletionClient(runner.BaseUrl, apiKey ?? String.Empty, logging, http);
                        break;
                    case ModelRunnerProviderEnum.Gemini:
                        client = new GeminiCompletionClient(runner.BaseUrl, apiKey ?? String.Empty, logging, http);
                        break;
                    case ModelRunnerProviderEnum.Ollama:
                        client = new OllamaCompletionClient(runner.BaseUrl, apiKey ?? String.Empty, logging, http);
                        break;
                    case ModelRunnerProviderEnum.Anthropic:
                        client = new AnthropicCompletionClient(runner.BaseUrl, apiKey ?? String.Empty, logging, http);
                        break;
                    case ModelRunnerProviderEnum.VoyageAI:
                        throw new NotSupportedException("Voyage AI runner '" + runner.Name + "' has no completion API.");
                    case ModelRunnerProviderEnum.AzureOpenAI:
                        client = new AzureOpenAiCompletionClient(runner.BaseUrl, RequireDeployment(runner), apiKey ?? String.Empty, runner.ApiVersion, logging, http);
                        break;
                    case ModelRunnerProviderEnum.Bedrock:
                        client = new BedrockCompletionClient(BuildAwsCredential(runner, apiKey, awsSessionToken), runner.Region!, endpointOrNull, logging, http);
                        break;
                    case ModelRunnerProviderEnum.VertexAI:
                        client = new VertexAiCompletionClient(runner.Project!, runner.Region!, BuildVertexCredential(runner, apiKey), endpointOrNull, logging, http);
                        break;
                    default:
                        throw new NotSupportedException("Unsupported model runner provider: " + runner.Provider);
                }
            }
            catch
            {
                http.Dispose();
                throw;
            }

            if (!String.IsNullOrWhiteSpace(runner.DefaultModel)) client.Model = runner.DefaultModel;
            else if (!String.IsNullOrWhiteSpace(runner.DefaultEmbeddingModel)) client.Model = runner.DefaultEmbeddingModel;

            // Without this, PolyPrompt's 120 s default cancels slow local completions (a 4B model classifying a
            // document under load), and the cancellation surfaced as a misleading "stage timed out after 1800 s".
            // NOTE: the per-call timeout is intentionally NOT capped at the endpoint's MaximumTimeoutMs (60 s by
            // default), which is far too short for slow local completion models. Ingestion calls are bounded by the
            // per-stage timeout (StageTimeoutSeconds, whose cancellation token is passed to the call); query calls
            // are bounded by the request lifecycle.
            client.TimeoutMs = Math.Max(runner.MaximumTimeoutMs, _MinimumCallTimeoutMs);
            return client;
        }

        /// <summary>
        /// Create a PolyPrompt embedding client for a model runner, applying its default embedding model (or, when
        /// none is set, its default model).
        /// </summary>
        /// <param name="runner">Model runner.</param>
        /// <param name="apiKey">Decrypted primary secret: the API key, Azure AD/Vertex bearer token, or (for Bedrock) the AWS secret access key. May be null for keyless local runners.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="awsSessionToken">Decrypted AWS session token for Bedrock temporary credentials, if any.</param>
        /// <returns>A configured embedding client.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="runner"/> or <paramref name="logging"/> is null.</exception>
        /// <exception cref="NotSupportedException">Thrown when the provider is unknown or has no embeddings API (Anthropic).</exception>
        /// <exception cref="InvalidOperationException">Thrown when a provider is missing a required field (e.g. Azure deployment, Bedrock/Vertex region).</exception>
        public static EmbeddingClientBase CreateEmbedding(ModelRunner runner, string? apiKey, LoggingModule logging, string? awsSessionToken = null)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            if (logging == null) throw new ArgumentNullException(nameof(logging));

            HttpClient http = CreateHttpClient(runner);
            string? endpointOrNull = String.IsNullOrWhiteSpace(runner.BaseUrl) ? null : runner.BaseUrl;

            EmbeddingClientBase client;
            try
            {
                switch (runner.Provider)
                {
                    case ModelRunnerProviderEnum.OpenAI:
                    case ModelRunnerProviderEnum.OpenAICompatible:
                        client = new OpenAiEmbeddingClient(runner.BaseUrl, apiKey ?? String.Empty, logging, http);
                        break;
                    case ModelRunnerProviderEnum.Gemini:
                        client = new GeminiEmbeddingClient(runner.BaseUrl, apiKey ?? String.Empty, logging, http);
                        break;
                    case ModelRunnerProviderEnum.Ollama:
                        client = new OllamaEmbeddingClient(runner.BaseUrl, apiKey ?? String.Empty, logging, http);
                        break;
                    case ModelRunnerProviderEnum.Anthropic:
                        throw new NotSupportedException("Anthropic runner '" + runner.Name + "' has no embeddings API.");
                    case ModelRunnerProviderEnum.VoyageAI:
                        client = new VoyageAiEmbeddingClient(runner.BaseUrl, apiKey ?? String.Empty, logging, http);
                        break;
                    case ModelRunnerProviderEnum.AzureOpenAI:
                        client = new AzureOpenAiEmbeddingClient(runner.BaseUrl, RequireDeployment(runner), apiKey ?? String.Empty, runner.ApiVersion, logging, http);
                        break;
                    case ModelRunnerProviderEnum.Bedrock:
                        client = new BedrockEmbeddingClient(BuildAwsCredential(runner, apiKey, awsSessionToken), runner.Region!, endpointOrNull, logging, http);
                        break;
                    case ModelRunnerProviderEnum.VertexAI:
                        client = new VertexAiEmbeddingClient(runner.Project!, runner.Region!, BuildVertexCredential(runner, apiKey), endpointOrNull, logging, http);
                        break;
                    default:
                        throw new NotSupportedException("Unsupported model runner provider: " + runner.Provider);
                }
            }
            catch
            {
                http.Dispose();
                throw;
            }

            // PolyPrompt 3 embedding clients have their own default model (e.g. all-minilm on Ollama), so always set
            // the runner's model explicitly rather than relying on the provider default.
            if (!String.IsNullOrWhiteSpace(runner.DefaultEmbeddingModel)) client.Model = runner.DefaultEmbeddingModel;
            else if (!String.IsNullOrWhiteSpace(runner.DefaultModel)) client.Model = runner.DefaultModel;

            client.TimeoutMs = Math.Max(runner.MaximumTimeoutMs, _MinimumCallTimeoutMs);
            return client;
        }

        #endregion

        #region Private-Methods

        private static HttpClient CreateHttpClient(ModelRunner runner)
        {
            // Every request goes through the retry handler, which retries the endpoint's transient failures and holds
            // a slot on the endpoint's process-wide concurrency limit. The retry handler wraps the shared handler, so
            // neither is disposed with the client.
            TransientRetryHandler retry = new TransientRetryHandler(_Handler, runner.MaxRetries, String.IsNullOrWhiteSpace(runner.Name) ? runner.Id : runner.Name,
                EndpointConcurrencyLimiter.Shared, runner.Id, runner.MaxConcurrentRequests);
            return new HttpClient(retry, disposeHandler: false);
        }

        private static string RequireDeployment(ModelRunner runner)
        {
            if (String.IsNullOrWhiteSpace(runner.Deployment)) throw new InvalidOperationException("Azure OpenAI runner '" + runner.Name + "' requires a deployment name.");
            return runner.Deployment;
        }

        private static StaticAwsCredential BuildAwsCredential(ModelRunner runner, string? apiKey, string? awsSessionToken)
        {
            if (String.IsNullOrWhiteSpace(runner.Region)) throw new InvalidOperationException("Bedrock runner '" + runner.Name + "' requires an AWS region.");
            if (String.IsNullOrWhiteSpace(runner.AccessKeyId)) throw new InvalidOperationException("Bedrock runner '" + runner.Name + "' requires an AWS access key id.");
            if (String.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("Bedrock runner '" + runner.Name + "' requires an AWS secret access key.");
            return new StaticAwsCredential(runner.AccessKeyId, apiKey, runner.Region, String.IsNullOrWhiteSpace(awsSessionToken) ? null : awsSessionToken);
        }

        private static ICredentialProvider BuildVertexCredential(ModelRunner runner, string? apiKey)
        {
            if (String.IsNullOrWhiteSpace(runner.Project)) throw new InvalidOperationException("Vertex AI runner '" + runner.Name + "' requires a project.");
            if (String.IsNullOrWhiteSpace(runner.Region)) throw new InvalidOperationException("Vertex AI runner '" + runner.Name + "' requires a region.");
            if (String.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("Vertex AI runner '" + runner.Name + "' requires a bearer token or service-account credential.");
            return new StaticTokenCredential(apiKey);
        }

        #endregion
    }
}
