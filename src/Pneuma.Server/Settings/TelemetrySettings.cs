namespace Pneuma.Server.Settings
{
    /// <summary>
    /// Telemetry (Radiant / OTLP / Prometheus) settings.
    /// </summary>
    public class TelemetrySettings
    {
        #region Public-Members

        /// <summary>Whether telemetry is enabled.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Service name reported in traces and metrics.</summary>
        public string ServiceName { get; set; } = "pneuma-server";

        /// <summary>OTLP collector endpoint (Tempo). Use the gRPC port (4317) with the "grpc" protocol,
        /// or the HTTP port (4318) with the "httpprotobuf" protocol (in which case include the /v1/traces path).</summary>
        public string OtlpEndpoint { get; set; } = "http://127.0.0.1:4317";

        /// <summary>OTLP export protocol: "grpc" (default) or "httpprotobuf".</summary>
        public string OtlpProtocol { get; set; } = "grpc";

        /// <summary>Whether the Prometheus metrics endpoint is exposed.</summary>
        public bool PrometheusEnabled { get; set; } = true;

        #endregion
    }
}
