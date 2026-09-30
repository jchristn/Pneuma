namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations;
    using Pneuma.Core.Integrations.Implementations;
    using Pneuma.Core.Models;
    using Pneuma.Core.Security;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Makes the wizard's model calls. With a progress callback the reply is streamed, so the caller can show how much
    /// the model has written; the call then times out only after <see cref="WizardSettings.TimeoutSeconds"/> without
    /// output (or three times that in all), so a long but steady ontology draft is not cut off. Without a callback, or
    /// when the endpoint cannot stream, it is one request that must finish within the timeout.
    /// </summary>
    public class WizardModelCaller
    {
        #region Private-Members

        private const long _ReportIntervalMs = 400;
        private readonly Aes256Cipher _Cipher;
        private readonly WizardSettings _Settings;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="cipher">Cipher for model endpoint keys.</param>
        /// <param name="settings">Wizard limits.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public WizardModelCaller(Aes256Cipher cipher, WizardSettings settings, LoggingModule logging)
        {
            _Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>Ask the model and return its reply text.</summary>
        /// <param name="runner">Completion endpoint.</param>
        /// <param name="system">System prompt.</param>
        /// <param name="user">User message.</param>
        /// <param name="attempt">Attempt number (for progress reports).</param>
        /// <param name="watch">The step's stopwatch (for progress reports).</param>
        /// <param name="progress">Progress callback, or null for a single non-streaming request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The reply text.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the call times out or is cancelled.</exception>
        public async Task<string> CompleteAsync(ModelRunner runner, string system, string user, int attempt, Stopwatch watch, Func<WizardProgress, Task>? progress, CancellationToken token)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            if (watch == null) throw new ArgumentNullException(nameof(watch));
            CompletionClientBase client = ModelClientFactory.Create(runner, DecryptKey(runner), _Logging);
            if (progress != null)
            {
                string? streamed = await StreamAsync(client, system, user, attempt, watch, progress, token).ConfigureAwait(false);
                if (streamed != null) return streamed;
            }

            ChatCompletionOptions options = new ChatCompletionOptions { Temperature = 0.4, MaxTokens = 4096, SystemPrompt = system };
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(_Settings.TimeoutSeconds));
                ChatResponse response = await client.ChatAsync(user, options, timeout.Token).ConfigureAwait(false);
                if (response == null || !response.Success || String.IsNullOrWhiteSpace(response.Text)) throw ModelResponseErrors.ToException("subject wizard", response?.Error);
                return response.Text;
            }
        }

        #endregion

        #region Private-Methods

        // Streams the reply, reporting its length as it grows. Returns null when the endpoint cannot stream, so the caller
        // falls back to a single request.
        private async Task<string?> StreamAsync(CompletionClientBase client, string system, string user, int attempt, Stopwatch watch, Func<WizardProgress, Task> progress, CancellationToken token)
        {
            ToolChatRequest request = new ToolChatRequest
            {
                Messages = new List<ChatMessage> { ChatMessage.System(system), ChatMessage.User(user) },
                Tools = new List<ToolDefinition>(),
                ToolChoice = "none",
                Temperature = 0.4,
                MaxTokens = 4096
            };
            TimeSpan idle = TimeSpan.FromSeconds(_Settings.TimeoutSeconds);
            using (CancellationTokenSource cap = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (CancellationTokenSource quiet = CancellationTokenSource.CreateLinkedTokenSource(cap.Token))
            {
                cap.CancelAfter(TimeSpan.FromSeconds(_Settings.TimeoutSeconds * 3));
                quiet.CancelAfter(idle);
                ToolChatStreamingResponse response = await client.ToolChatStreamingAsync(request, quiet.Token).ConfigureAwait(false);
                if (response == null || !response.Success)
                {
                    _Logging.Debug("[WizardModelCaller] streaming unavailable, falling back to one request: " + response?.Error);
                    return null;
                }
                StringBuilder text = new StringBuilder();
                long lastReport = -_ReportIntervalMs;
                await foreach (ToolChatStreamingChunk chunk in response.Chunks.WithCancellation(quiet.Token).ConfigureAwait(false))
                {
                    if (String.IsNullOrEmpty(chunk.Text)) continue;
                    text.Append(chunk.Text);
                    quiet.CancelAfter(idle);
                    if (watch.ElapsedMilliseconds - lastReport >= _ReportIntervalMs)
                    {
                        lastReport = watch.ElapsedMilliseconds;
                        await progress(new WizardProgress { Phase = "writing", Attempt = attempt, Characters = text.Length, ElapsedMs = watch.ElapsedMilliseconds }).ConfigureAwait(false);
                    }
                }
                string reply = text.Length > 0 ? text.ToString() : (response.Text ?? String.Empty);
                if (String.IsNullOrWhiteSpace(reply)) throw ModelResponseErrors.ToException("subject wizard", response.Error ?? "the model returned no text");
                return reply;
            }
        }

        private string? DecryptKey(ModelRunner runner)
        {
            if (String.IsNullOrEmpty(runner.AuthMaterialEncrypted)) return null;
            try { return _Cipher.Decrypt(runner.AuthMaterialEncrypted); }
            catch (Exception) { return null; }
        }

        #endregion
    }
}
