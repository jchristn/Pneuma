namespace Pneuma.Core.Integrations.Implementations
{
    using System;
    using System.Diagnostics;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Observability;

    /// <summary>
    /// Shared resilient transport for outbound integration clients (DocumentAtom, Partio, Verbex,
    /// LiteGraph). Provides transient-aware retry with per-attempt timeout, a per-service concurrency
    /// bulkhead, uniform structured failures via <see cref="IntegrationClientException"/>, and per-call
    /// telemetry through <see cref="PneumaMetrics.RecordIntegration"/>. Write operations opt out of retry
    /// so a non-idempotent create is never duplicated.
    /// </summary>
    public abstract class IntegrationClientBase : IDisposable
    {
        #region Private-Members

        private readonly HttpClient _Client;
        private readonly bool _OwnsClient;

        #endregion

        #region Public-Members

        /// <summary>Logical service name used for telemetry labels and the concurrency gate key.</summary>
        public string ServiceName { get; }

        /// <summary>
        /// Per-attempt request timeout in milliseconds. Default 100000; minimum 1000; maximum 600000.
        /// </summary>
        public int TimeoutMilliseconds { get; }

        /// <summary>
        /// Maximum concurrent outbound requests to this service. Default 8; minimum 1; maximum 1024.
        /// </summary>
        public int MaxConcurrentRequests { get; }

        /// <summary>
        /// Number of retries for transient failures on non-write requests. Default 2; minimum 0;
        /// maximum 10. Writes always use zero retries regardless of this value.
        /// </summary>
        public int RetryCount { get; }

        /// <summary>
        /// Fixed delay between retry attempts in milliseconds. Default 500; minimum 50; maximum 30000.
        /// </summary>
        public int RetryDelayMilliseconds { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initialize the resilient client base.</summary>
        /// <param name="serviceName">Logical service name; required.</param>
        /// <param name="timeoutMilliseconds">Per-attempt timeout in ms (clamped 1000..600000).</param>
        /// <param name="maxConcurrentRequests">Max concurrent requests (clamped 1..1024).</param>
        /// <param name="retryCount">Retry count for transient failures (clamped 0..10).</param>
        /// <param name="retryDelayMilliseconds">Delay between retries in ms (clamped 50..30000).</param>
        /// <param name="handler">Optional message handler for tests; when supplied the base owns and disposes a dedicated <see cref="HttpClient"/>, otherwise the shared process client is used.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceName"/> is null or whitespace.</exception>
        protected IntegrationClientBase(
            string serviceName,
            int timeoutMilliseconds = 100000,
            int maxConcurrentRequests = 8,
            int retryCount = 2,
            int retryDelayMilliseconds = 500,
            HttpMessageHandler? handler = null)
        {
            if (String.IsNullOrWhiteSpace(serviceName)) throw new ArgumentNullException(nameof(serviceName));

            ServiceName = serviceName.Trim();
            TimeoutMilliseconds = Math.Clamp(timeoutMilliseconds, 1000, 600000);
            MaxConcurrentRequests = Math.Clamp(maxConcurrentRequests, 1, 1024);
            RetryCount = Math.Clamp(retryCount, 0, 10);
            RetryDelayMilliseconds = Math.Clamp(retryDelayMilliseconds, 50, 30000);

            if (handler == null)
            {
                _Client = IntegrationHttp.Client;
                _OwnsClient = false;
            }
            else
            {
                _Client = new HttpClient(handler, disposeHandler: false);
                _OwnsClient = true;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>Dispose the owned HTTP client, if any.</summary>
        public void Dispose()
        {
            if (_OwnsClient) _Client.Dispose();
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Send a request through the concurrency gate with retry and per-attempt timeout, returning
        /// the raw status and body without throwing on a non-success status. Use this when the caller
        /// handles specific statuses (for example treating 404 as a null result).
        /// </summary>
        /// <param name="operation">Low-cardinality operation label for telemetry.</param>
        /// <param name="requestFactory">Factory that builds a fresh request per attempt (content is not reusable across retries).</param>
        /// <param name="isWrite">True for non-idempotent writes; forces zero retries.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="retryCountOverride">Optional per-call retry override for non-write requests.</param>
        /// <returns>The response status, body, and success flag.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="requestFactory"/> is null.</exception>
        protected async Task<IntegrationResponse> SendRawResilientAsync(
            string operation,
            Func<HttpRequestMessage> requestFactory,
            bool isWrite,
            CancellationToken token,
            int? retryCountOverride = null)
        {
            if (requestFactory == null) throw new ArgumentNullException(nameof(requestFactory));

            return await IntegrationConcurrencyGate.ExecuteAsync(
                ServiceName,
                MaxConcurrentRequests,
                operationToken => SendCoreAsync(operation, requestFactory, isWrite, operationToken, retryCountOverride),
                token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send a request and return the response body, throwing <see cref="IntegrationClientException"/>
        /// on any non-success status.
        /// </summary>
        /// <param name="operation">Low-cardinality operation label for telemetry.</param>
        /// <param name="requestFactory">Factory that builds a fresh request per attempt.</param>
        /// <param name="isWrite">True for non-idempotent writes; forces zero retries.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="retryCountOverride">Optional per-call retry override for non-write requests.</param>
        /// <returns>The response body text.</returns>
        /// <exception cref="IntegrationClientException">Thrown when the service returns a non-success status.</exception>
        protected async Task<string> SendResilientAsync(
            string operation,
            Func<HttpRequestMessage> requestFactory,
            bool isWrite,
            CancellationToken token,
            int? retryCountOverride = null)
        {
            IntegrationResponse response = await SendRawResilientAsync(operation, requestFactory, isWrite, token, retryCountOverride).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                throw new IntegrationClientException(ServiceName, operation, response.StatusCode, Truncate(response.Body, 512));
            }

            return response.Body;
        }

        /// <summary>
        /// Probe a request for basic connectivity, classifying the service as reachable-and-healthy,
        /// reachable-but-erroring, or unreachable. Never throws for a non-success status.
        /// </summary>
        /// <param name="operation">Operation label for telemetry.</param>
        /// <param name="requestFactory">Factory that builds the probe request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The health result.</returns>
        protected async Task<IntegrationHealthResult> ProbeResilientAsync(
            string operation,
            Func<HttpRequestMessage> requestFactory,
            CancellationToken token)
        {
            try
            {
                IntegrationResponse response = await SendRawResilientAsync(operation, requestFactory, false, token).ConfigureAwait(false);
                return new IntegrationHealthResult(ServiceName, true, response.IsSuccess, response.StatusCode, String.Empty);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new IntegrationHealthResult(ServiceName, false, false, 0, exception.Message);
            }
        }

        /// <summary>Truncate a diagnostic string to a maximum length.</summary>
        /// <param name="value">Value to truncate; null yields an empty string.</param>
        /// <param name="maxLength">Maximum length.</param>
        /// <returns>The truncated value.</returns>
        protected static string Truncate(string? value, int maxLength)
        {
            string safe = value ?? String.Empty;
            if (maxLength < 0) maxLength = 0;
            return safe.Length > maxLength ? safe.Substring(0, maxLength) : safe;
        }

        #endregion

        #region Private-Methods

        private async Task<IntegrationResponse> SendCoreAsync(
            string operation,
            Func<HttpRequestMessage> requestFactory,
            bool isWrite,
            CancellationToken token,
            int? retryCountOverride)
        {
            int effectiveRetry = isWrite ? 0 : Math.Clamp(retryCountOverride ?? RetryCount, 0, 10);
            Stopwatch stopwatch = Stopwatch.StartNew();
            string outcome = "ok";
            Exception? lastException = null;

            try
            {
                for (int attempt = 1; attempt <= effectiveRetry + 1; attempt++)
                {
                    token.ThrowIfCancellationRequested();

                    using (CancellationTokenSource timeoutSource = new CancellationTokenSource(TimeoutMilliseconds))
                    using (CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutSource.Token))
                    {
                        HttpRequestMessage request = requestFactory();
                        try
                        {
                            HttpResponseMessage response = await _Client.SendAsync(request, linkedSource.Token).ConfigureAwait(false);
                            if (IsTransientStatusCode(response.StatusCode) && attempt <= effectiveRetry)
                            {
                                response.Dispose();
                            }
                            else
                            {
                                string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                                int statusCode = (int)response.StatusCode;
                                bool success = response.IsSuccessStatusCode;
                                response.Dispose();
                                outcome = success ? "ok" : "error";
                                return new IntegrationResponse(statusCode, body, success);
                            }
                        }
                        catch (OperationCanceledException exception) when (!token.IsCancellationRequested && attempt <= effectiveRetry)
                        {
                            lastException = exception;
                        }
                        catch (HttpRequestException exception) when (attempt <= effectiveRetry)
                        {
                            lastException = exception;
                        }
                        finally
                        {
                            request.Dispose();
                        }
                    }

                    if (attempt <= effectiveRetry)
                    {
                        await Task.Delay(RetryDelayMilliseconds, token).ConfigureAwait(false);
                    }
                }

                outcome = "error";
                throw lastException ?? new InvalidOperationException("The outbound request to " + ServiceName + " failed without a captured exception.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                outcome = "cancelled";
                throw;
            }
            catch
            {
                outcome = "error";
                throw;
            }
            finally
            {
                stopwatch.Stop();
                PneumaMetrics.RecordIntegration(ServiceName, operation, outcome, stopwatch.Elapsed.TotalSeconds);
            }
        }

        private static bool IsTransientStatusCode(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.RequestTimeout ||
                   statusCode == (HttpStatusCode)429 ||
                   statusCode == HttpStatusCode.BadGateway ||
                   statusCode == HttpStatusCode.ServiceUnavailable ||
                   statusCode == HttpStatusCode.GatewayTimeout ||
                   (int)statusCode >= 500;
        }

        #endregion
    }
}
