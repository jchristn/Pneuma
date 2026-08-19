namespace Pneuma.Server.Services
{
    using System;
    using Pneuma.Server.Settings;
    using Radiant;
    using SyslogLogging;

    /// <summary>
    /// Wraps a Radiant host to emit OTLP traces for handled requests. All operations are best-effort:
    /// telemetry failures never affect request handling.
    /// </summary>
    public class TelemetryService : IDisposable
    {
        #region Private-Members

        private readonly LoggingModule _Logging;
        private readonly RadiantHost? _Host;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the telemetry service from settings.</summary>
        /// <param name="settings">Telemetry settings.</param>
        /// <param name="logging">Logging module.</param>
        public TelemetryService(TelemetrySettings settings, LoggingModule logging)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            if (settings == null || !settings.Enabled) return;

            try
            {
                RadiantSettings radiant = new RadiantSettings(settings.ServiceName) { Enable = true };
                radiant.Otlp.Enable = true;
                radiant.Otlp.Endpoint = settings.OtlpEndpoint;
                // Match the exporter protocol to the configured endpoint's port. gRPC (4317) is the default
                // and avoids the OTLP/HTTP "you must append /v1/traces to an explicit endpoint" gotcha.
                radiant.Otlp.Protocol = String.Equals(settings.OtlpProtocol, "httpprotobuf", StringComparison.OrdinalIgnoreCase)
                    ? OtlpProtocolEnum.HttpProtobuf
                    : OtlpProtocolEnum.Grpc;
                _Host = RadiantHost.Start(radiant);
                _Logging.Info("[TelemetryService] OTLP traces enabled (" + radiant.Otlp.Protocol + ") → " + settings.OtlpEndpoint);
            }
            catch (Exception e)
            {
                _Host = null;
                _Logging.Warn("[TelemetryService] telemetry disabled (init failed): " + e.Message);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>Emit a server span for a completed request.</summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Request path.</param>
        /// <param name="statusCode">Response status code.</param>
        public void RecordRequest(string method, string path, int statusCode)
        {
            if (_Host == null) return;
            try
            {
                using (RadiantSpan span = _Host.StartSpan(method + " " + path, SpanKindEnum.Server))
                {
                    span.SetTag("http.request.method", method);
                    span.SetTag("http.route", path);
                    span.SetTag("http.response.status_code", statusCode);
                    if (statusCode >= 500) span.SetError("server error");
                    else span.SetOk(null);
                }
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        /// <summary>
        /// Start a new span for manual instrumentation. Returns null when telemetry is disabled or
        /// span creation fails, so callers can safely use the null-conditional operator.
        /// </summary>
        /// <param name="name">Span name.</param>
        /// <param name="kind">Span kind; defaults to <see cref="SpanKindEnum.Internal"/>.</param>
        /// <returns>The started span, or null when telemetry is unavailable.</returns>
        public RadiantSpan? StartSpan(string name, SpanKindEnum kind = SpanKindEnum.Internal)
        {
            if (_Host == null) return null;
            try
            {
                return _Host.StartSpan(name, kind);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Dispose the telemetry host.</summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            try { _Host?.Dispose(); } catch (Exception) { }
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
