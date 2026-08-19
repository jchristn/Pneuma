namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Integrations.Models;
    using SyslogLogging;

    /// <summary>
    /// Idempotent, best-effort startup initializer that ensures at least one embedding and one completion
    /// model endpoint exist in Partio. It only ever <em>creates</em> a default when none exist — it never
    /// updates or overwrites an existing endpoint — so operator edits made through the dashboard survive
    /// server and stack restarts. (Partio's own default-endpoint reconciliation is disabled by leaving
    /// <c>DefaultEmbeddingEndpoints</c>/<c>DefaultInferenceEndpoints</c> empty in partio.json, otherwise
    /// Partio would reset the seeded "default" embedding endpoint back to its configured values on boot.)
    /// </summary>
    public class PartioEndpointInitializer
    {
        #region Private-Members

        private const int _MaxAttempts = 10;
        private const int _RetryDelayMs = 3000;

        private readonly IPartioClient _Partio;
        private readonly string _OllamaBaseUrl;
        private readonly LoggingModule _Logging;
        private readonly string _Header = "[PartioEndpointInitializer] ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the initializer.</summary>
        /// <param name="partio">Partio client.</param>
        /// <param name="ollamaBaseUrl">Base URL of the local Ollama runner used for the seeded defaults.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public PartioEndpointInitializer(IPartioClient partio, string ollamaBaseUrl, LoggingModule logging)
        {
            _Partio = partio ?? throw new ArgumentNullException(nameof(partio));
            _OllamaBaseUrl = String.IsNullOrWhiteSpace(ollamaBaseUrl) ? "http://ollama:11434" : ollamaBaseUrl.TrimEnd('/');
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Ensure a default embedding and completion endpoint exist, retrying while Partio is still coming
        /// up. Best-effort — never throws; a persistent failure just leaves the operator to configure model
        /// runners through the dashboard.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        public async Task InitializeAsync(CancellationToken token = default)
        {
            for (int attempt = 1; attempt <= _MaxAttempts; attempt++)
            {
                try
                {
                    List<PartioEndpoint> embedding = await _Partio.ListEmbeddingEndpointsAsync(token).ConfigureAwait(false);
                    List<PartioEndpoint> completion = await _Partio.ListCompletionEndpointsAsync(token).ConfigureAwait(false);

                    if (embedding.Count == 0)
                    {
                        await _Partio.CreateEndpointAsync("embedding", BuildDefault("nomic-embed-text"), token).ConfigureAwait(false);
                        _Logging.Info(_Header + "seeded default embedding endpoint (nomic-embed-text @ " + _OllamaBaseUrl + ")");
                    }
                    if (completion.Count == 0)
                    {
                        await _Partio.CreateEndpointAsync("completion", BuildDefault("gemma3:4b"), token).ConfigureAwait(false);
                        _Logging.Info(_Header + "seeded default completion endpoint (gemma3:4b @ " + _OllamaBaseUrl + ")");
                    }

                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    if (attempt >= _MaxAttempts)
                    {
                        _Logging.Warn(_Header + "could not ensure default model endpoints after " + _MaxAttempts + " attempts (continuing): " + e.Message);
                        return;
                    }

                    try
                    {
                        await Task.Delay(_RetryDelayMs, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        #endregion

        #region Private-Methods

        private PartioEndpoint BuildDefault(string model)
        {
            return new PartioEndpoint
            {
                Name = model,
                Model = model,
                Endpoint = _OllamaBaseUrl,
                ApiFormat = "Ollama",
                Active = true
            };
        }

        #endregion
    }
}
