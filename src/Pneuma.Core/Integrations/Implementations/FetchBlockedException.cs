namespace Pneuma.Core.Integrations.Implementations
{
    using System;

    /// <summary>
    /// Thrown when the fetch-safety policy refuses a URL: a disallowed scheme, or a host that resolves to a loopback,
    /// private, link-local, or metadata address that is not on an allow-list. The fetch is never retried.
    /// </summary>
    public class FetchBlockedException : Exception
    {
        #region Public-Members

        /// <summary>The URL that was refused.</summary>
        public string Url { get; }

        /// <summary>A short, stable reason code (for example "scheme", "private-address", "invalid-url").</summary>
        public string Reason { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the exception.</summary>
        /// <param name="url">The refused URL.</param>
        /// <param name="reason">A short, stable reason code.</param>
        /// <param name="message">The operator-facing message.</param>
        public FetchBlockedException(string url, string reason, string message) : base(message)
        {
            Url = url ?? String.Empty;
            Reason = reason ?? String.Empty;
        }

        #endregion
    }
}
