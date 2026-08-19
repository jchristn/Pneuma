namespace Pneuma.Sdk.Responses
{
    using System;

    /// <summary>
    /// Standard error response body returned by the Pneuma API on non-2xx responses.
    /// </summary>
    public class ErrorResponse
    {
        /// <summary>Short machine-readable error code.</summary>
        public string Error { get; set; } = "Error";

        /// <summary>Human-readable message.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Optional additional context.</summary>
        public string? Context { get; set; } = null;
    }
}
