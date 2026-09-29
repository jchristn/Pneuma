namespace Pneuma.Core.Integrations.Implementations
{
    using System;
    using System.Globalization;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Turns the error text of an unsuccessful model response into a typed exception, so callers can tell a
    /// rate-limited or overloaded endpoint (retry later) from a rejected request (do not retry) and from an input that
    /// exceeded the model's context window (retry with a smaller input). Stateless and thread-safe.
    /// </summary>
    public static class ModelResponseErrors
    {
        #region Private-Members

        private static readonly Regex _StatusPattern = new Regex(@"\b(4\d\d|5\d\d)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>Build the exception that describes an unsuccessful model response.</summary>
        /// <param name="operation">What the call was doing, for the message (for example "classification").</param>
        /// <param name="error">The response's error text; null or empty when the endpoint gave none.</param>
        /// <returns>A <see cref="ModelEndpointUnavailableException"/>, <see cref="ModelRequestRejectedException"/>, or <see cref="InvalidOperationException"/>.</returns>
        public static Exception ToException(string operation, string? error)
        {
            string op = String.IsNullOrWhiteSpace(operation) ? "model request" : operation;
            string text = String.IsNullOrWhiteSpace(error) ? "no response" : error!;
            string message = "The " + op + " request failed: " + text;

            if (IsContextLength(text)) return new ModelRequestRejectedException(StatusOf(text), true, message);

            // An explicit status decides first: 408, 429, and 5xx are transient, any other 4xx is a rejection even when the
            // body mentions capacity. Only a response without a status is judged by its wording.
            int status = StatusOf(text);
            if (status == 408 || status == 429 || status >= 500) return new ModelEndpointUnavailableException(status, message);
            if (status >= 400) return new ModelRequestRejectedException(status, false, message);
            if (IsTransientText(text)) return new ModelEndpointUnavailableException(status, message);
            return new InvalidOperationException(message);
        }

        /// <summary>True when the error text says the input exceeded the model's context length.</summary>
        /// <param name="error">The error text.</param>
        /// <returns>True for a context-length rejection.</returns>
        public static bool IsContextLength(string? error)
        {
            if (String.IsNullOrEmpty(error)) return false;
            return error.IndexOf("context length", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("maximum context", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("context window", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("too many tokens", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("input is too long", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("exceeds the maximum", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion

        #region Private-Methods

        private static int StatusOf(string text)
        {
            Match match = _StatusPattern.Match(text);
            if (!match.Success) return 0;
            return Int32.Parse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static bool IsTransientText(string text)
        {
            return text.IndexOf("TooManyRequests", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("at capacity", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("connection refused", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("No connection could be made", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion
    }
}
