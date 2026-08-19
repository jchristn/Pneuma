namespace Pneuma.Sdk
{
    using System;
    using Pneuma.Sdk.Responses;

    /// <summary>
    /// Thrown when the Pneuma API returns a non-2xx HTTP status code. Carries the status code and the raw
    /// response body, and exposes the parsed <see cref="ErrorResponse"/> when the body was valid JSON.
    /// </summary>
    public class PneumaException : Exception
    {
        #region Public-Members

        /// <summary>HTTP status code returned by the server.</summary>
        public int StatusCode { get; private set; }

        /// <summary>Raw response body, if any.</summary>
        public string? ResponseBody { get; private set; }

        /// <summary>Parsed error response, when the body was valid JSON. May be null.</summary>
        public ErrorResponse? Error { get; private set; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new instance of the <see cref="PneumaException"/> class.
        /// </summary>
        /// <param name="statusCode">HTTP status code.</param>
        /// <param name="responseBody">Raw response body.</param>
        /// <param name="error">Parsed error response, or null.</param>
        public PneumaException(int statusCode, string? responseBody, ErrorResponse? error)
            : base(BuildMessage(statusCode, responseBody, error))
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
            Error = error;
        }

        #endregion

        #region Private-Methods

        private static string BuildMessage(int statusCode, string? responseBody, ErrorResponse? error)
        {
            if (error != null && !string.IsNullOrEmpty(error.Message))
                return "Pneuma API request failed with status " + statusCode + ": " + error.Error + " - " + error.Message;
            if (!string.IsNullOrEmpty(responseBody))
                return "Pneuma API request failed with status " + statusCode + ": " + responseBody;
            return "Pneuma API request failed with status " + statusCode + ".";
        }

        #endregion
    }
}
