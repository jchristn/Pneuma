namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Implementations;
    using SyslogLogging;

    /// <summary>
    /// Probes every configured external integration (DocumentAtom, Partio, RecallDB, LiteGraph) at
    /// startup and logs a formatted, secret-safe connectivity report so a misconfigured or unreachable
    /// dependency surfaces in seconds rather than at first ingestion. A probe failure is logged as a
    /// warning; when fail-fast is enabled an unreachable service throws instead.
    /// </summary>
    public class ExternalServiceDiagnosticsService
    {
        #region Private-Members

        private readonly List<IServiceProbe> _Probes;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the diagnostics service.</summary>
        /// <param name="probes">Integration probes to run.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public ExternalServiceDiagnosticsService(IEnumerable<IServiceProbe> probes, LoggingModule logging)
        {
            if (probes == null) throw new ArgumentNullException(nameof(probes));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _Probes = new List<IServiceProbe>(probes);
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>Probe every dependency concurrently, log a report, and return the results.</summary>
        /// <param name="failFast">When true, throws if any dependency is unreachable.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The health results, one per probe.</returns>
        /// <exception cref="InvalidOperationException">Thrown when <paramref name="failFast"/> is true and a dependency is unreachable.</exception>
        public async Task<List<IntegrationHealthResult>> RunAsync(bool failFast, CancellationToken token = default)
        {
            List<Task<IntegrationHealthResult>> tasks = new List<Task<IntegrationHealthResult>>();
            foreach (IServiceProbe probe in _Probes)
            {
                tasks.Add(ProbeSafeAsync(probe, token));
            }

            IntegrationHealthResult[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
            List<IntegrationHealthResult> list = new List<IntegrationHealthResult>(results);

            _Logging.Info(FormatReport(list));

            List<string> unreachable = new List<string>();
            foreach (IntegrationHealthResult result in list)
            {
                if (!result.Reachable)
                {
                    _Logging.Warn("[Diagnostics] " + result.ServiceName + " is unreachable: " + result.ErrorMessage);
                    unreachable.Add(result.ServiceName);
                }
            }

            if (failFast && unreachable.Count > 0)
            {
                throw new InvalidOperationException("Required external services are unreachable: " + String.Join(", ", unreachable) + ".");
            }

            return list;
        }

        #endregion

        #region Private-Methods

        private static async Task<IntegrationHealthResult> ProbeSafeAsync(IServiceProbe probe, CancellationToken token)
        {
            try
            {
                return await probe.ProbeAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new IntegrationHealthResult(probe.ServiceName, false, false, 0, exception.Message);
            }
        }

        private static string FormatReport(List<IntegrationHealthResult> results)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[Diagnostics] external service connectivity:");
            foreach (IntegrationHealthResult result in results)
            {
                string status;
                if (!result.Reachable) status = "UNREACHABLE";
                else if (result.Success) status = "OK";
                else status = "REACHABLE (status " + result.StatusCode + ")";

                sb.Append("\n  - ").Append(result.ServiceName.PadRight(14)).Append(status);
                if (!result.Reachable && !String.IsNullOrEmpty(result.ErrorMessage))
                {
                    sb.Append(" — ").Append(result.ErrorMessage);
                }
            }
            return sb.ToString();
        }

        #endregion
    }
}
