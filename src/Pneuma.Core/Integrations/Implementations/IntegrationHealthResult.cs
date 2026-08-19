namespace Pneuma.Core.Integrations.Implementations
{
    using System;

    /// <summary>
    /// Outcome of a lightweight connectivity probe against a downstream integration service. Used by
    /// startup diagnostics to distinguish reachable-and-healthy, reachable-but-erroring, and
    /// unreachable services.
    /// </summary>
    public sealed class IntegrationHealthResult
    {
        #region Public-Members

        /// <summary>Logical service name that was probed.</summary>
        public string ServiceName { get; }

        /// <summary>True when the service answered at all (a response was received, regardless of status).</summary>
        public bool Reachable { get; }

        /// <summary>True when the probe returned a success status code.</summary>
        public bool Success { get; }

        /// <summary>HTTP status code returned by the probe; zero when the service was unreachable.</summary>
        public int StatusCode { get; }

        /// <summary>Error message when the probe failed to complete; empty otherwise.</summary>
        public string ErrorMessage { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initialize a new integration health result.</summary>
        /// <param name="serviceName">Logical service name.</param>
        /// <param name="reachable">Whether the service was reachable.</param>
        /// <param name="success">Whether the probe returned a success status.</param>
        /// <param name="statusCode">HTTP status code; zero when unreachable.</param>
        /// <param name="errorMessage">Error message; null is normalized to an empty string.</param>
        public IntegrationHealthResult(string serviceName, bool reachable, bool success, int statusCode, string? errorMessage)
        {
            ServiceName = serviceName ?? String.Empty;
            Reachable = reachable;
            Success = success;
            StatusCode = statusCode;
            ErrorMessage = errorMessage ?? String.Empty;
        }

        #endregion
    }
}
