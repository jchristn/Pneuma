namespace Pneuma.Core.Integrations.Implementations
{
    using System;

    /// <summary>
    /// Thrown when a model endpoint stays unavailable (rate limited, overloaded, or failing with a transient server
    /// error) after the shared retry policy has exhausted its attempts. Ingestion retries the job after a longer delay;
    /// query routes answer 503.
    /// </summary>
    public class ModelEndpointUnavailableException : Exception
    {
        #region Public-Members

        /// <summary>The last HTTP status code received from the endpoint, or 0 when no response was received.</summary>
        public int StatusCode { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the exception.</summary>
        /// <param name="statusCode">The last HTTP status code, or 0.</param>
        /// <param name="message">The operator-facing message.</param>
        public ModelEndpointUnavailableException(int statusCode, string message) : base(message)
        {
            StatusCode = statusCode;
        }

        #endregion
    }
}
