namespace Pneuma.Core.Integrations.Implementations
{
    using System;

    /// <summary>
    /// Uniform exception raised when an outbound integration request to a downstream service
    /// (DocumentAtom, Partio, RecallDB, LiteGraph) returns a non-success status after any retries are
    /// exhausted. Carries structured context — the logical service name, the operation label, the
    /// HTTP status code, and a truncated response body — so failures are diagnosable without leaking
    /// full payloads.
    /// </summary>
    public class IntegrationClientException : Exception
    {
        #region Public-Members

        /// <summary>Logical service name (for example "documentatom", "partio", "recalldb", "litegraph").</summary>
        public string ServiceName { get; }

        /// <summary>Low-cardinality operation label (typically the normalized request path).</summary>
        public string Operation { get; }

        /// <summary>HTTP status code returned by the service.</summary>
        public int StatusCode { get; }

        /// <summary>Truncated response body captured for diagnostics.</summary>
        public string ResponseBody { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initialize a new integration client exception.</summary>
        /// <param name="serviceName">Logical service name.</param>
        /// <param name="operation">Operation label.</param>
        /// <param name="statusCode">HTTP status code.</param>
        /// <param name="responseBody">Truncated response body; null is normalized to an empty string.</param>
        public IntegrationClientException(string serviceName, string operation, int statusCode, string? responseBody)
            : base(BuildMessage(serviceName, operation, statusCode, responseBody))
        {
            ServiceName = serviceName ?? String.Empty;
            Operation = operation ?? String.Empty;
            StatusCode = statusCode;
            ResponseBody = responseBody ?? String.Empty;
        }

        #endregion

        #region Private-Methods

        private static string BuildMessage(string serviceName, string operation, int statusCode, string? responseBody)
        {
            string safeService = String.IsNullOrEmpty(serviceName) ? "(unknown)" : serviceName;
            string safeOperation = String.IsNullOrEmpty(operation) ? "(unknown)" : operation;
            string body = responseBody ?? String.Empty;
            return safeService + " request '" + safeOperation + "' failed with status " + statusCode + ": " + body;
        }

        #endregion
    }
}
