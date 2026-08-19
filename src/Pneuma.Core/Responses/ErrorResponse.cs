namespace Pneuma.Core.Responses
{
    using System;

    /// <summary>
    /// Standard error response body.
    /// </summary>
    public class ErrorResponse
    {
        #region Public-Members

        /// <summary>Short machine-readable error code.</summary>
        public string Error { get; set; } = "Error";

        /// <summary>Human-readable message.</summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>Optional additional context.</summary>
        public string? Context { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate an empty error response.</summary>
        public ErrorResponse()
        {
        }

        /// <summary>Instantiate an error response.</summary>
        /// <param name="error">Error code.</param>
        /// <param name="message">Message.</param>
        /// <param name="context">Optional context.</param>
        public ErrorResponse(string error, string message, string? context = null)
        {
            Error = error;
            Message = message ?? String.Empty;
            Context = context;
        }

        #endregion
    }
}
