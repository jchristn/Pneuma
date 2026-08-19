namespace Pneuma.Server.Routes
{
    using System;

    /// <summary>
    /// Thrown when a request body is present but cannot be deserialized into the expected type (for
    /// example an unknown enum value or a type mismatch). Surfaced by the route exception handler as a
    /// <c>400 BadRequest</c> so callers get an accurate reason instead of a misleading downstream error.
    /// </summary>
    public class RequestBodyException : Exception
    {
        /// <summary>Instantiate the exception with a human-readable reason.</summary>
        /// <param name="message">Reason the body could not be parsed.</param>
        public RequestBodyException(string message) : base(message)
        {
        }
    }
}
