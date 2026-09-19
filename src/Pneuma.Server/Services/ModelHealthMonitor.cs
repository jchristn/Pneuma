namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Models;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using SyslogLogging;

    /// <summary>
    /// Background monitor that health-checks the configured model runners. Runners are enumerated from the
    /// native store on a fixed interval and each ACTIVE endpoint with health checks enabled is probed on its
    /// own configured cadence, using its own probe URL, method, timeout, expected status, thresholds, and
    /// (optionally) its API key as a bearer token. State (uptime, history, consecutive counts, latency) is
    /// keyed by endpoint id, accumulated in memory with healthy/unhealthy hysteresis, and is not persisted.
    /// Consumers project the per-endpoint state via <see cref="BuildStatus"/>.
    /// </summary>
    public class ModelHealthMonitor
    {
        #region Private-Members

        private const int _RefreshIntervalMs = 15000;
        private const int _DefaultProbeTimeoutMs = 3000;
        private const int _DefaultIntervalMs = 15000;
        private const int _DefaultHealthyThreshold = 2;
        private const int _DefaultUnhealthyThreshold = 2;
        private const int _MaxHistoryRecords = 500;
        private static readonly TimeSpan _HistoryRetention = TimeSpan.FromHours(24);

        private readonly DatabaseDriverBase _Db;
        private readonly Aes256Cipher _Cipher;
        private readonly LoggingModule _Logging;
        private readonly HttpClient _Http;
        private readonly ConcurrentDictionary<string, EndpointHealthState> _States = new ConcurrentDictionary<string, EndpointHealthState>(StringComparer.Ordinal);
        private Task? _Loop;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the model health monitor.</summary>
        /// <param name="db">Database driver used to enumerate the configured model runners.</param>
        /// <param name="cipher">Cipher used to decrypt an endpoint's API key when it is sent on health checks.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public ModelHealthMonitor(DatabaseDriverBase db, Aes256Cipher cipher, LoggingModule logging)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
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
            _Logging.Info("[ModelHealthMonitor] started; evaluating model endpoint health every " + (_RefreshIntervalMs / 1000) + "s");
        }

        /// <summary>
        /// Project the current per-endpoint health state onto a single model endpoint. When the endpoint has
        /// not yet been probed, the returned status carries no timestamps (a "pending" state); when health
        /// checks are disabled the status reflects that and is never marked unhealthy.
        /// </summary>
        /// <param name="endpointId">Endpoint (model runner) id.</param>
        /// <param name="name">Endpoint name.</param>
        /// <param name="type">Endpoint type ("Embedding" or "Completion").</param>
        /// <param name="baseUrl">The endpoint's base URL.</param>
        /// <param name="healthCheckEnabled">Whether background health checks are enabled for the endpoint.</param>
        /// <returns>The projected health status.</returns>
        public ModelEndpointHealthDto BuildStatus(string endpointId, string? name, string type, string? baseUrl, bool healthCheckEnabled)
        {
            ModelEndpointHealthDto dto = new ModelEndpointHealthDto
            {
                EndpointId = endpointId ?? String.Empty,
                EndpointName = name,
                Type = type ?? String.Empty,
                BaseUrl = baseUrl,
                HealthCheckEnabled = healthCheckEnabled
            };

            if (String.IsNullOrEmpty(endpointId) || !_States.TryGetValue(endpointId!, out EndpointHealthState? state) || state == null)
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

        /// <summary>
        /// Run a single health probe of an endpoint immediately, out of band with the periodic loop, and return
        /// its updated status. Lets an operator start a check for a newly-defined endpoint instead of waiting for
        /// the next scheduled cycle.
        /// </summary>
        /// <param name="runner">The model runner to probe.</param>
        /// <param name="type">Display type ("Embedding" or "Completion").</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The endpoint's health status after the probe.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="runner"/> is null.</exception>
        public async Task<ModelEndpointHealthDto> ProbeNowAsync(ModelRunner runner, string type, CancellationToken token = default)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));

            EndpointHealthState state = _States.GetOrAdd(runner.Id, key => new EndpointHealthState { EndpointId = key });
            lock (state.Sync)
            {
                state.HealthyThreshold = runner.HealthyThreshold > 0 ? runner.HealthyThreshold : _DefaultHealthyThreshold;
                state.UnhealthyThreshold = runner.UnhealthyThreshold > 0 ? runner.UnhealthyThreshold : _DefaultUnhealthyThreshold;
            }

            ProbePlan plan = BuildPlan(runner);
            await ProbeAndUpdateAsync(plan, token).ConfigureAwait(false);
            return BuildStatus(runner.Id, runner.Name, type, runner.BaseUrl, runner.HealthCheckEnabled);
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
            // Probe every endpoint across all tenants, not just global (tenant-null) ones — dashboard-created
            // endpoints are tenant-scoped and must be health-monitored too.
            List<ModelRunner> runners = await _Db.ModelRunners.EnumerateAllAsync(token).ConfigureAwait(false);

            HashSet<string> active = new HashSet<string>(StringComparer.Ordinal);
            List<ProbePlan> plans = new List<ProbePlan>();
            DateTime now = DateTime.UtcNow;

            foreach (ModelRunner runner in runners)
            {
                if (runner == null || !runner.Active || !runner.HealthCheckEnabled) continue;
                if (String.IsNullOrEmpty(runner.Id)) continue;
                active.Add(runner.Id);

                EndpointHealthState state = _States.GetOrAdd(runner.Id, key => new EndpointHealthState { EndpointId = key });
                int intervalMs = runner.HealthCheckIntervalMs > 0 ? runner.HealthCheckIntervalMs : _DefaultIntervalMs;
                lock (state.Sync)
                {
                    state.HealthyThreshold = runner.HealthyThreshold > 0 ? runner.HealthyThreshold : _DefaultHealthyThreshold;
                    state.UnhealthyThreshold = runner.UnhealthyThreshold > 0 ? runner.UnhealthyThreshold : _DefaultUnhealthyThreshold;
                    if (state.LastCheckUtc.HasValue && (now - state.LastCheckUtc.Value).TotalMilliseconds < intervalMs) continue;
                }

                plans.Add(BuildPlan(runner));
            }

            // Drop state for endpoints no longer active / health-check-enabled.
            foreach (string existing in new List<string>(_States.Keys))
            {
                if (!active.Contains(existing)) _States.TryRemove(existing, out EndpointHealthState? _);
            }

            List<Task> probes = new List<Task>();
            foreach (ProbePlan plan in plans) probes.Add(ProbeAndUpdateAsync(plan, token));
            await Task.WhenAll(probes).ConfigureAwait(false);
        }

        private ProbePlan BuildPlan(ModelRunner runner)
        {
            string url = String.IsNullOrWhiteSpace(runner.HealthCheckUrl) ? runner.BaseUrl : runner.HealthCheckUrl!;
            HttpMethod method = String.Equals(runner.HealthCheckMethod, "HEAD", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Head : HttpMethod.Get;
            string? bearer = null;
            if (runner.HealthCheckUseAuth && !String.IsNullOrEmpty(runner.AuthMaterialEncrypted))
            {
                try { bearer = _Cipher.Decrypt(runner.AuthMaterialEncrypted!); }
                catch (Exception) { bearer = null; }
            }

            return new ProbePlan
            {
                EndpointId = runner.Id,
                Url = url,
                Method = method,
                TimeoutMs = runner.HealthCheckTimeoutMs > 0 ? runner.HealthCheckTimeoutMs : _DefaultProbeTimeoutMs,
                ExpectedStatusCode = runner.HealthCheckExpectedStatusCode,
                BearerToken = bearer
            };
        }

        private async Task ProbeAndUpdateAsync(ProbePlan plan, CancellationToken token)
        {
            ProbeResult result = await ProbeAsync(plan, token).ConfigureAwait(false);
            if (_States.TryGetValue(plan.EndpointId, out EndpointHealthState? state) && state != null) UpdateState(state, result);
        }

        private async Task<ProbeResult> ProbeAsync(ProbePlan plan, CancellationToken token)
        {
            ProbeResult result = new ProbeResult();
            Stopwatch sw = Stopwatch.StartNew();
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(plan.TimeoutMs);
                try
                {
                    using (HttpRequestMessage request = new HttpRequestMessage(plan.Method, plan.Url))
                    {
                        if (!String.IsNullOrEmpty(plan.BearerToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", plan.BearerToken);
                        using (HttpResponseMessage response = await _Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                        {
                            sw.Stop();
                            int statusCode = (int)response.StatusCode;
                            result.StatusCode = statusCode;
                            result.LatencyMs = sw.Elapsed.TotalMilliseconds;
                            // Healthy when the response is 2xx, or matches the endpoint's explicitly configured
                            // expected status (e.g. an endpoint whose root returns 401 but is otherwise serving).
                            bool ok = (statusCode >= 200 && statusCode < 300) || (plan.ExpectedStatusCode > 0 && statusCode == plan.ExpectedStatusCode);
                            result.Success = ok;
                            result.Error = ok ? null : "Endpoint returned HTTP " + statusCode + ".";
                        }
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    sw.Stop();
                    result.Success = false;
                    result.LatencyMs = sw.Elapsed.TotalMilliseconds;
                    result.Error = "Health check timed out after " + plan.TimeoutMs + "ms.";
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

        private static void UpdateState(EndpointHealthState state, ProbeResult result)
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
                    if (!state.IsHealthy && state.ConsecutiveSuccesses >= state.HealthyThreshold)
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
                    if (state.IsHealthy && state.ConsecutiveFailures >= state.UnhealthyThreshold)
                    {
                        AccumulateSince(state, now);
                        state.IsHealthy = false;
                        state.LastUnhealthyUtc = now;
                        state.LastStateChangeUtc = now;
                    }
                }
            }
        }

        private static void AccumulateSince(EndpointHealthState state, DateTime now)
        {
            if (!state.LastStateChangeUtc.HasValue) return;
            double slice = (now - state.LastStateChangeUtc.Value).TotalMilliseconds;
            if (slice < 0) slice = 0;
            if (state.IsHealthy) state.TotalUptimeMs += slice;
            else state.TotalDowntimeMs += slice;
        }

        #endregion

        #region Private-Types

        private class ProbePlan
        {
            public string EndpointId { get; set; } = String.Empty;
            public string Url { get; set; } = String.Empty;
            public HttpMethod Method { get; set; } = HttpMethod.Get;
            public int TimeoutMs { get; set; } = _DefaultProbeTimeoutMs;
            public int ExpectedStatusCode { get; set; } = 200;
            public string? BearerToken { get; set; } = null;
        }

        private class ProbeResult
        {
            public bool Success { get; set; } = false;
            public int? StatusCode { get; set; } = null;
            public double? LatencyMs { get; set; } = null;
            public string? Error { get; set; } = null;
        }

        private class EndpointHealthState
        {
            public object Sync { get; } = new object();
            public string EndpointId { get; set; } = String.Empty;
            public int HealthyThreshold { get; set; } = _DefaultHealthyThreshold;
            public int UnhealthyThreshold { get; set; } = _DefaultUnhealthyThreshold;
            public bool IsHealthy { get; set; } = false;
            public int? LastStatusCode { get; set; } = null;
            public double? LastLatencyMs { get; set; } = null;
            public DateTime? FirstCheckUtc { get; set; } = null;
            public DateTime? LastCheckUtc { get; set; } = null;
            public DateTime? LastHealthyUtc { get; set; } = null;
            public DateTime? LastUnhealthyUtc { get; set; } = null;
            public DateTime? LastStateChangeUtc { get; set; } = null;
            public double TotalUptimeMs { get; set; } = 0;
            public double TotalDowntimeMs { get; set; } = 0;
            public int ConsecutiveSuccesses { get; set; } = 0;
            public int ConsecutiveFailures { get; set; } = 0;
            public string? LastError { get; set; } = null;
            public List<EndpointHealthRecord> History { get; } = new List<EndpointHealthRecord>();
        }

        #endregion
    }
}
