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

        private static readonly HttpClient _Http = new HttpClient();

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

            CompletionClientBase client;
            switch (runner.Provider)
            {
                case ModelRunnerProviderEnum.OpenAI:
                case ModelRunnerProviderEnum.OpenAICompatible:
                    client = new OpenAiClient(runner.BaseUrl, apiKey ?? String.Empty, logging, _Http);
                    break;
                case ModelRunnerProviderEnum.Gemini:
                    client = new GeminiClient(runner.BaseUrl, apiKey ?? String.Empty, logging, _Http);
                    break;
                case ModelRunnerProviderEnum.Ollama:
                    client = new OllamaClient(runner.BaseUrl, apiKey ?? String.Empty, logging, _Http);
                    break;
                default:
                    throw new NotSupportedException("Unsupported model runner provider: " + runner.Provider);
            }

            if (!String.IsNullOrEmpty(runner.DefaultModel)) client.Model = runner.DefaultModel;
            return client;
        }

        #endregion
    }
}
