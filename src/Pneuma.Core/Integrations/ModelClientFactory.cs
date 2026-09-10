namespace Pneuma.Core.Integrations
{
    using System;
    using System.Net.Http;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
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
        /// Create a completion client for a model runner, applying its default model.
        /// </summary>
        /// <param name="runner">Model runner.</param>
        /// <param name="apiKey">Decrypted API key, if any.</param>
        /// <param name="logging">Logging module.</param>
        /// <returns>A configured completion client.</returns>
        public static CompletionClientBase Create(ModelRunner runner, string? apiKey, LoggingModule logging)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            if (logging == null) throw new ArgumentNullException(nameof(logging));

            // Fresh HttpClient per client (pooled through the shared handler) so each endpoint's Authorization
            // header is isolated from the others.
            HttpClient http = new HttpClient(_Handler, disposeHandler: false);

            CompletionClientBase client;
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
                default:
                    http.Dispose();
                    throw new NotSupportedException("Unsupported model runner provider: " + runner.Provider);
            }

            if (!String.IsNullOrEmpty(runner.DefaultModel)) client.Model = runner.DefaultModel;
            return client;
        }

        #endregion
    }
}
