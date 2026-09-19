namespace Pneuma.Core.Integrations
{
    using System;
    using System.Net.Http;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ingestion.Models;
    using PolyPrompt.Auth;
    using PolyPrompt.Clients;
    using SyslogLogging;

    /// <summary>
    /// Builds a PolyPrompt completion client for a configured model runner.
    /// </summary>
    public static class ModelClientFactory
    {
        #region Private-Members

        // Connection pooling is shared through one handler (avoids socket exhaustion), but every client gets its
        // OWN HttpClient. PolyPrompt applies the endpoint's API key as an Authorization header on the client's
        // DefaultRequestHeaders; a single shared HttpClient would accumulate one key per endpoint and throw
        // "Authorization does not support multiple values" as soon as a second keyed endpoint was used.
        private static readonly SocketsHttpHandler _Handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a PolyPrompt client for a model runner, applying its default model. The returned client
        /// serves both completions and embeddings (subject to provider support: Anthropic has no embeddings
        /// API and Voyage AI has no completion API).
        /// </summary>
        /// <param name="runner">Model runner.</param>
        /// <param name="apiKey">Decrypted primary secret: the API key, Azure AD/Vertex bearer token, or (for Bedrock) the AWS secret access key. May be null for keyless local runners.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="awsSessionToken">Decrypted AWS session token for Bedrock temporary credentials, if any.</param>
        /// <returns>A configured client.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="runner"/> or <paramref name="logging"/> is null.</exception>
        /// <exception cref="NotSupportedException">Thrown when the provider is unknown.</exception>
        /// <exception cref="InvalidOperationException">Thrown when a provider is missing a required field (e.g. Azure deployment, Bedrock/Vertex region).</exception>
        public static CompletionClientBase Create(ModelRunner runner, string? apiKey, LoggingModule logging, string? awsSessionToken = null)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            if (logging == null) throw new ArgumentNullException(nameof(logging));

            // Fresh HttpClient per client (pooled through the shared handler) so each endpoint's Authorization
            // header is isolated from the others.
            HttpClient http = new HttpClient(_Handler, disposeHandler: false);
            string? endpointOrNull = String.IsNullOrWhiteSpace(runner.BaseUrl) ? null : runner.BaseUrl;

            CompletionClientBase client;
            try
            {
                switch (runner.Provider)
                {
                    case ModelRunnerProviderEnum.OpenAI:
                    case ModelRunnerProviderEnum.OpenAICompatible:
                        client = new OpenAiClient(runner.BaseUrl, apiKey ?? String.Empty, logging, http);
                        break;
                    case ModelRunnerProviderEnum.Gemini:
                        client = new GeminiClient(runner.BaseUrl, apiKey ?? String.Empty, logging, http);
                        break;
                    case ModelRunnerProviderEnum.Ollama:
                        client = new OllamaClient(runner.BaseUrl, apiKey ?? String.Empty, logging, http);
                        break;
                    case ModelRunnerProviderEnum.Anthropic:
                        client = new AnthropicClient(runner.BaseUrl, apiKey ?? String.Empty, logging, http);
                        break;
                    case ModelRunnerProviderEnum.VoyageAI:
                        client = new VoyageAiClient(runner.BaseUrl, apiKey ?? String.Empty, logging, http);
                        break;
                    case ModelRunnerProviderEnum.AzureOpenAI:
                        if (String.IsNullOrWhiteSpace(runner.Deployment)) throw new InvalidOperationException("Azure OpenAI runner '" + runner.Name + "' requires a deployment name.");
                        client = new AzureOpenAiClient(runner.BaseUrl, runner.Deployment, apiKey ?? String.Empty, runner.ApiVersion, logging, http);
                        break;
                    case ModelRunnerProviderEnum.Bedrock:
                        if (String.IsNullOrWhiteSpace(runner.Region)) throw new InvalidOperationException("Bedrock runner '" + runner.Name + "' requires an AWS region.");
                        if (String.IsNullOrWhiteSpace(runner.AccessKeyId)) throw new InvalidOperationException("Bedrock runner '" + runner.Name + "' requires an AWS access key id.");
                        if (String.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("Bedrock runner '" + runner.Name + "' requires an AWS secret access key.");
                        StaticAwsCredential awsCredential = new StaticAwsCredential(runner.AccessKeyId, apiKey, runner.Region, String.IsNullOrWhiteSpace(awsSessionToken) ? null : awsSessionToken);
                        client = new BedrockClient(awsCredential, runner.Region, logging, http, endpointOrNull);
                        break;
                    case ModelRunnerProviderEnum.VertexAI:
                        if (String.IsNullOrWhiteSpace(runner.Project)) throw new InvalidOperationException("Vertex AI runner '" + runner.Name + "' requires a project.");
                        if (String.IsNullOrWhiteSpace(runner.Region)) throw new InvalidOperationException("Vertex AI runner '" + runner.Name + "' requires a region.");
                        if (String.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("Vertex AI runner '" + runner.Name + "' requires a bearer token or service-account credential.");
                        ICredentialProvider vertexCredential = new StaticTokenCredential(apiKey);
                        client = new VertexAiClient(runner.Project, runner.Region, vertexCredential, endpointOrNull, logging, http);
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

            if (!String.IsNullOrEmpty(runner.DefaultModel)) client.Model = runner.DefaultModel;
            else if (!String.IsNullOrEmpty(runner.DefaultEmbeddingModel)) client.Model = runner.DefaultEmbeddingModel;

            // NOTE: we intentionally do NOT cap the per-call timeout at the endpoint's MaximumTimeoutMs. That
            // default (60s) is far too short for slow local completion models (e.g. classifying a large document
            // through a 4B model), and capping here canceled those calls mid-flight. Ingestion calls are already
            // bounded by the per-stage timeout (StageTimeoutSeconds, whose cancellation token is passed to the
            // call); query calls are bounded by the request lifecycle.
            return client;
        }

        #endregion
    }
}
