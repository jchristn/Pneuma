namespace Pneuma.Core.Integrations.Implementations
{
    using System;

    /// <summary>
    /// Thrown when a model endpoint rejects a request with a client error that retrying cannot fix, such as an input
    /// longer than the model's context window. <see cref="IsContextLength"/> tells the caller whether a smaller input
    /// could succeed.
    /// </summary>
    public class ModelRequestRejectedException : Exception
    {
        #region Public-Members

        /// <summary>The HTTP status code the endpoint returned, or 0 when unknown.</summary>
        public int StatusCode { get; }

        /// <summary>True when the rejection says the input exceeded the model's context length.</summary>
        public bool IsContextLength { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the exception.</summary>
        /// <param name="statusCode">The HTTP status code, or 0.</param>
        /// <param name="isContextLength">True when the input exceeded the model's context length.</param>
        /// <param name="message">The operator-facing message.</param>
        public ModelRequestRejectedException(int statusCode, bool isContextLength, string message) : base(message)
        {
            StatusCode = statusCode;
            IsContextLength = isContextLength;
        }

        #endregion
    }
}
