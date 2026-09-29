namespace Pneuma.Core.Integrations.Implementations
{
    using System;

    /// <summary>
    /// Thrown when fetched or submitted content exceeds its size limit. The limit is enforced while streaming, so the
    /// oversized content is never fully read. The fetch is never retried.
    /// </summary>
    public class ContentTooLargeException : Exception
    {
        #region Public-Members

        /// <summary>The size limit that was exceeded, in bytes.</summary>
        public long LimitBytes { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the exception.</summary>
        /// <param name="limitBytes">The limit, in bytes.</param>
        /// <param name="message">The operator-facing message.</param>
        public ContentTooLargeException(long limitBytes, string message) : base(message)
        {
            LimitBytes = limitBytes;
        }

        #endregion
    }
}
