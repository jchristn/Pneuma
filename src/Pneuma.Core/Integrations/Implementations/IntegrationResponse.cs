namespace Pneuma.Core.Integrations.Implementations
{
    using System;

    /// <summary>
    /// Result of a resilient outbound integration request: the HTTP status code, the response body,
    /// and whether the status indicates success. Returned by the raw send path so callers can handle
    /// status-specific cases (for example treating 404 as a null result) without exceptions.
    /// </summary>
    public sealed class IntegrationResponse
    {
        #region Public-Members

        /// <summary>HTTP status code returned by the service.</summary>
        public int StatusCode { get; }

        /// <summary>Response body as text; never null (empty string when there is no body).</summary>
        public string Body { get; }

        /// <summary>True when <see cref="StatusCode"/> is in the 2xx success range.</summary>
        public bool IsSuccess { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initialize a new integration response.</summary>
        /// <param name="statusCode">HTTP status code.</param>
        /// <param name="body">Response body; null is normalized to an empty string.</param>
        /// <param name="isSuccess">Whether the status indicates success.</param>
        public IntegrationResponse(int statusCode, string? body, bool isSuccess)
        {
            StatusCode = statusCode;
            Body = body ?? String.Empty;
            IsSuccess = isSuccess;
        }

        #endregion
    }
}
