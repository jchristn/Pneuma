namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Responses;
    using SyslogLogging;

    /// <summary>
    /// Background monitor that health-checks the model endpoints defined in Partio. Endpoints are
    /// enumerated from Partio on a fixed interval, deduplicated by base URL, and each unique base URL is
    /// probed once per tick; the result is shared by every endpoint on that host. State (uptime, history,
    /// consecutive counts, latency) is accumulated in memory with healthy/unhealthy hysteresis and is not
    /// persisted. Consumers project the per-base-URL state onto individual endpoints via <see cref="BuildStatus"/>.
    /// </summary>
    public class ModelHealthMonitor
    {
        #region Private-Members

        private const int _RefreshIntervalMs = 15000;
        private const int _ProbeTimeoutMs = 3000;
        private const int _HealthyThreshold = 2;
        private const int _UnhealthyThreshold = 2;
        private const int _MaxHistoryRecords = 500;
        private static readonly TimeSpan _HistoryRetention = TimeSpan.FromHours(24);

        private readonly IPartioClient _Partio;
        private readonly LoggingModule _Logging;
        private readonly HttpClient _Http;
        private readonly ConcurrentDictionary<string, BaseUrlHealthState> _States = new ConcurrentDictionary<string, BaseUrlHealthState>(StringComparer.OrdinalIgnoreCase);
        private Task? _Loop;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the model health monitor.</summary>
        /// <param name="partio">Partio client used to enumerate the configured model endpoints.</param>
        /// <param name="logging">Logging module.</param>
        public ModelHealthMonitor(IPartioClient partio, LoggingModule logging)
        {
            _Partio = partio ?? throw new ArgumentNullException(nameof(partio));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Http = new HttpClient();
            _Http.Timeout = TimeSpan.FromSeconds(30);
        }

        #endregion

        #region Public-Methods

        /// <summary>Start the background probe loop.</summary>
        /// <param name="token">Cancellation token that stops the loop.</param>
        public void Start(CancellationToken token)
        {
            _Loop = Task.Run(() => RunAsync(token), token);
            _Logging.Info("[ModelHealthMonitor] started; probing model endpoint base URLs every " + (_RefreshIntervalMs / 1000) + "s");
        }

        /// <summary>
        /// Project the current per-base-URL health state onto a single model endpoint. When the endpoint's
        /// base URL has not yet been probed, the returned status carries no timestamps (a "pending" state).
        /// </summary>
        /// <param name="endpointId">Partio endpoint id.</param>
        /// <param name="name">Endpoint name.</param>
        /// <param name="type">Endpoint type ("Embedding" or "Completion").</param>
        /// <param name="baseUrl">The endpoint's base URL.</param>
        /// <returns>The projected health status.</returns>
        public ModelEndpointHealthDto BuildStatus(string endpointId, string? name, string type, string? baseUrl)
        {
            ModelEndpointHealthDto dto = new ModelEndpointHealthDto
            {
                EndpointId = endpointId ?? String.Empty,
                EndpointName = name,
                Type = type ?? String.Empty,
                BaseUrl = baseUrl
            };

            string key = NormalizeBaseUrl(baseUrl);
            if (String.IsNullOrEmpty(key) || !_States.TryGetValue(key, out BaseUrlHealthState? state) || state == null)
            {
                return dto;
            }

            lock (state.Sync)
            {
                DateTime now = DateTime.UtcNow;
                double upMs = state.TotalUptimeMs;
                double downMs = state.TotalDowntimeMs;
                if (state.LastStateChangeUtc.HasValue)
                {
                    double slice = (now - state.LastStateChangeUtc.Value).TotalMilliseconds;
                    if (slice < 0) slice = 0;
                    if (state.IsHealthy) upMs += slice;
                    else downMs += slice;
                }
                double total = upMs + downMs;

                dto.IsHealthy = state.IsHealthy;
                dto.StatusCode = state.LastStatusCode;
                dto.LatencyMs = state.LastLatencyMs;
                dto.FirstCheckUtc = state.FirstCheckUtc;
                dto.LastCheckUtc = state.LastCheckUtc;
                dto.LastHealthyUtc = state.LastHealthyUtc;
                dto.LastUnhealthyUtc = state.LastUnhealthyUtc;
                dto.LastStateChangeUtc = state.LastStateChangeUtc;
                dto.TotalUptimeMs = upMs;
                dto.TotalDowntimeMs = downMs;
                dto.UptimePercentage = total > 0 ? Math.Round(upMs / total * 100.0, 2) : (state.IsHealthy ? 100.0 : 0.0);
                dto.ConsecutiveSuccesses = state.ConsecutiveSuccesses;
                dto.ConsecutiveFailures = state.ConsecutiveFailures;
                dto.LastError = state.LastError;
                dto.History = new List<EndpointHealthRecord>(state.History);
            }

            return dto;
        }

        #endregion

        #region Private-Methods

        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await RefreshAndProbeAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    _Logging.Warn("[ModelHealthMonitor] probe cycle error: " + e.Message);
                }

                try
                {
                    await Task.Delay(_RefreshIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _Logging.Info("[ModelHealthMonitor] stopped");
        }

        private async Task RefreshAndProbeAsync(CancellationToken token)
        {
            HashSet<string> active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectBaseUrls(await _Partio.ListEmbeddingEndpointsAsync(token).ConfigureAwait(false), active);
            CollectBaseUrls(await _Partio.ListCompletionEndpointsAsync(token).ConfigureAwait(false), active);

            // Drop state for base URLs no longer referenced by any active endpoint.
            foreach (string existing in new List<string>(_States.Keys))
            {
                if (!active.Contains(existing)) _States.TryRemove(existing, out BaseUrlHealthState? _);
            }

            List<Task> probes = new List<Task>();
            foreach (string baseUrl in active)
            {
                BaseUrlHealthState state = _States.GetOrAdd(baseUrl, key => new BaseUrlHealthState { BaseUrl = key });
                probes.Add(ProbeAndUpdateAsync(state, token));
            }
            await Task.WhenAll(probes).ConfigureAwait(false);
        }

        private static void CollectBaseUrls(List<PartioEndpoint> endpoints, HashSet<string> into)
        {
            if (endpoints == null) return;
            foreach (PartioEndpoint endpoint in endpoints)
            {
                if (endpoint == null || !endpoint.Active) continue;
                string key = NormalizeBaseUrl(endpoint.Endpoint);
                if (!String.IsNullOrEmpty(key)) into.Add(key);
            }
        }

        private async Task ProbeAndUpdateAsync(BaseUrlHealthState state, CancellationToken token)
        {
            ProbeResult result = await ProbeAsync(state.BaseUrl, token).ConfigureAwait(false);
            UpdateState(state, result);
        }

        private async Task<ProbeResult> ProbeAsync(string baseUrl, CancellationToken token)
        {
            ProbeResult result = new ProbeResult();
            Stopwatch sw = Stopwatch.StartNew();
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(_ProbeTimeoutMs);
                try
                {
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, baseUrl))
                    using (HttpResponseMessage response = await _Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        sw.Stop();
                        int statusCode = (int)response.StatusCode;
                        result.StatusCode = statusCode;
                        result.LatencyMs = sw.Elapsed.TotalMilliseconds;
                        // Any HTTP response below 500 means the host is reachable and serving; 5xx is treated as unhealthy.
                        result.Success = statusCode < 500;
                        result.Error = result.Success ? null : "Base URL returned HTTP " + statusCode + ".";
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    sw.Stop();
                    result.Success = false;
                    result.LatencyMs = sw.Elapsed.TotalMilliseconds;
                    result.Error = "Health check timed out after " + _ProbeTimeoutMs + "ms.";
                }
                catch (Exception e)
                {
                    sw.Stop();
                    result.Success = false;
                    result.LatencyMs = sw.Elapsed.TotalMilliseconds;
                    result.Error = e.InnerException?.Message ?? e.Message;
                }
            }
            return result;
        }

        private static void UpdateState(BaseUrlHealthState state, ProbeResult result)
        {
            lock (state.Sync)
            {
                DateTime now = DateTime.UtcNow;
                if (!state.FirstCheckUtc.HasValue)
                {
                    state.FirstCheckUtc = now;
                    state.LastStateChangeUtc = now;
                }
                state.LastCheckUtc = now;
                state.LastStatusCode = result.StatusCode;
                state.LastLatencyMs = result.LatencyMs;

                state.History.Add(new EndpointHealthRecord { TimestampUtc = now, Success = result.Success });
                DateTime cutoff = now - _HistoryRetention;
                state.History.RemoveAll(r => r.TimestampUtc < cutoff);
                if (state.History.Count > _MaxHistoryRecords)
                {
                    state.History.RemoveRange(0, state.History.Count - _MaxHistoryRecords);
                }

                if (result.Success)
                {
                    state.ConsecutiveSuccesses++;
                    state.ConsecutiveFailures = 0;
                    state.LastError = null;
                    if (!state.IsHealthy && state.ConsecutiveSuccesses >= _HealthyThreshold)
                    {
                        AccumulateSince(state, now);
                        state.IsHealthy = true;
                        state.LastHealthyUtc = now;
                        state.LastStateChangeUtc = now;
                    }
                }
                else
                {
                    state.ConsecutiveFailures++;
                    state.ConsecutiveSuccesses = 0;
                    state.LastError = result.Error;
                    if (state.IsHealthy && state.ConsecutiveFailures >= _UnhealthyThreshold)
                    {
                        AccumulateSince(state, now);
                        state.IsHealthy = false;
                        state.LastUnhealthyUtc = now;
                        state.LastStateChangeUtc = now;
                    }
                }
            }
        }

        private static void AccumulateSince(BaseUrlHealthState state, DateTime now)
        {
            if (!state.LastStateChangeUtc.HasValue) return;
            double slice = (now - state.LastStateChangeUtc.Value).TotalMilliseconds;
            if (slice < 0) slice = 0;
            if (state.IsHealthy) state.TotalUptimeMs += slice;
            else state.TotalDowntimeMs += slice;
        }

        private static string NormalizeBaseUrl(string? baseUrl)
        {
            if (String.IsNullOrWhiteSpace(baseUrl)) return String.Empty;
            return baseUrl.Trim().TrimEnd('/');
        }

        #endregion

        #region Private-Types

        private class ProbeResult
        {
            public bool Success { get; set; } = false;
            public int? StatusCode { get; set; } = null;
            public double? LatencyMs { get; set; } = null;
            public string? Error { get; set; } = null;
        }

        #endregion
    }
}
