namespace Pneuma.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Observability;

    /// <summary>
    /// Retries a model endpoint's transient failures under every PolyPrompt call (embeddings, chat, completion, rerank)
    /// and bounds how many requests are in flight to the endpoint at once. Transient means 408, 429, 502, 503, 504, or a
    /// 500 whose body names one of those or says the endpoint is at capacity (a proxy wrapping an upstream failure).
    /// The wait honors <c>Retry-After</c> and otherwise uses exponential backoff with full jitter. Client errors (400,
    /// 401, 403, 404, 422) and network failures are never retried. After the last attempt the final response is
    /// returned unchanged, so the caller sees the real status. The request body is buffered once so it can be resent.
    /// </summary>
    public class TransientRetryHandler : DelegatingHandler
    {
        #region Public-Members

        /// <summary>Retries after the first attempt. Minimum 0, maximum 10.</summary>
        public int MaxRetries { get; }

        /// <summary>Base backoff before the first retry; doubles each retry (before jitter). Default 1 second.</summary>
        public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>Largest backoff between retries, including a server's <c>Retry-After</c>. Default 30 seconds.</summary>
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

        #endregion

        #region Private-Members

        private static readonly HashSet<int> _TransientStatuses = new HashSet<int> { 408, 429, 502, 503, 504 };
        private readonly string _RunnerLabel;
        private readonly EndpointConcurrencyLimiter? _Limiter;
        private readonly string _LimiterKey;
        private readonly int _MaxConcurrent;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the handler.</summary>
        /// <param name="inner">The handler that sends requests (shared, not disposed by this handler's owner).</param>
        /// <param name="maxRetries">Retries after the first attempt; clamped to [0, 10].</param>
        /// <param name="runnerLabel">Low-cardinality label for metrics (the runner name or id).</param>
        /// <param name="limiter">Per-endpoint concurrency limiter; null disables limiting.</param>
        /// <param name="limiterKey">The key the limiter tracks this endpoint under (the runner id).</param>
        /// <param name="maxConcurrent">Most requests in flight to this endpoint; values below 1 become 1.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is null.</exception>
        public TransientRetryHandler(HttpMessageHandler inner, int maxRetries, string runnerLabel, EndpointConcurrencyLimiter? limiter = null, string? limiterKey = null, int maxConcurrent = 1)
            : base(inner ?? throw new ArgumentNullException(nameof(inner)))
        {
            MaxRetries = Math.Clamp(maxRetries, 0, 10);
            _RunnerLabel = String.IsNullOrWhiteSpace(runnerLabel) ? "(unknown)" : runnerLabel;
            _Limiter = limiter;
            _LimiterKey = limiterKey ?? _RunnerLabel;
            _MaxConcurrent = Math.Max(1, maxConcurrent);
        }

        #endregion

        #region Public-Methods

        /// <summary>True when a status is worth retrying on its own (408, 429, 502, 503, 504).</summary>
        /// <param name="status">HTTP status code.</param>
        /// <returns>True when transient.</returns>
        public static bool IsTransientStatus(int status)
        {
            return _TransientStatuses.Contains(status);
        }

        /// <summary>
        /// Parse a <c>Retry-After</c> value (seconds, or an HTTP date) into a delay. Returns null when absent or invalid.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <returns>The delay, or null.</returns>
        public static TimeSpan? RetryAfter(HttpResponseMessage response)
        {
            if (response?.Headers?.RetryAfter == null) return null;
            if (response.Headers.RetryAfter.Delta != null) return response.Headers.RetryAfter.Delta.Value;
            if (response.Headers.RetryAfter.Date != null)
            {
                TimeSpan until = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                return until > TimeSpan.Zero ? until : TimeSpan.Zero;
            }

            return null;
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content != null) await request.Content.LoadIntoBufferAsync().ConfigureAwait(false);

            HttpRequestMessage current = request;
            for (int attempt = 0; ; attempt++)
            {
                HttpResponseMessage response = await SendOnceAsync(current, cancellationToken).ConfigureAwait(false);
                if (attempt >= MaxRetries || !await IsRetryableAsync(response, cancellationToken).ConfigureAwait(false)) return response;

                TimeSpan delay = Delay(attempt, RetryAfter(response));
                PneumaMetrics.RecordModelRetry(_RunnerLabel, ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
                response.Dispose();
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                current = await CloneAsync(request).ConfigureAwait(false);
            }
        }

        private async Task<HttpResponseMessage> SendOnceAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (_Limiter == null) return await base.SendAsync(request, token).ConfigureAwait(false);
            using (IDisposable lease = await _Limiter.AcquireAsync(_LimiterKey, _MaxConcurrent, _RunnerLabel, token).ConfigureAwait(false))
            {
                return await base.SendAsync(request, token).ConfigureAwait(false);
            }
        }

        private static async Task<bool> IsRetryableAsync(HttpResponseMessage response, CancellationToken token)
        {
            int status = (int)response.StatusCode;
            if (IsTransientStatus(status)) return true;
            if (status != (int)HttpStatusCode.InternalServerError || response.Content == null) return false;

            // A proxy or gateway may wrap an upstream rate limit in a 500; read the (buffered) body to tell.
            await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            return body.Contains("429", StringComparison.Ordinal)
                || body.Contains("502", StringComparison.Ordinal)
                || body.Contains("503", StringComparison.Ordinal)
                || body.Contains("504", StringComparison.Ordinal)
                || body.IndexOf("at capacity", StringComparison.OrdinalIgnoreCase) >= 0
                || body.IndexOf("overloaded", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private TimeSpan Delay(int attempt, TimeSpan? retryAfter)
        {
            if (retryAfter != null) return retryAfter.Value > MaxDelay ? MaxDelay : retryAfter.Value;
            double ceilingMs = Math.Min(MaxDelay.TotalMilliseconds, BaseDelay.TotalMilliseconds * Math.Pow(2, attempt));
            // Full jitter spreads retries from many workers so they do not hit the endpoint in lockstep; the floor keeps
            // a retry from firing immediately.
            double ms = Math.Max(BaseDelay.TotalMilliseconds / 2, Random.Shared.NextDouble() * ceilingMs);
            return TimeSpan.FromMilliseconds(ms);
        }

        private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage original)
        {
            HttpRequestMessage clone = new HttpRequestMessage(original.Method, original.RequestUri)
            {
                Version = original.Version,
                VersionPolicy = original.VersionPolicy
            };

            foreach (KeyValuePair<string, IEnumerable<string>> header in original.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            foreach (KeyValuePair<string, object?> option in original.Options) clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);

            if (original.Content != null)
            {
                byte[] body = await original.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                ByteArrayContent content = new ByteArrayContent(body);
                foreach (KeyValuePair<string, IEnumerable<string>> header in original.Content.Headers) content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                clone.Content = content;
            }

            return clone;
        }

        #endregion
    }
}
