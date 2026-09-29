namespace Pneuma.Server.Services
{
    using System;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Masks the values of secret-named JSON string properties (passwords, API keys, bearer and session tokens, secret
    /// keys) in a captured request body, so request history never stores them. Works on the raw text, so a truncated
    /// or malformed body is still masked where it can be.
    /// </summary>
    public static class JsonBodyRedactor
    {
        #region Public-Members

        /// <summary>The placeholder written in place of a secret value.</summary>
        public const string Mask = "***redacted***";

        #endregion

        #region Private-Members

        private static readonly Regex _SecretProperty = new Regex(
            "(\"(?:password|currentPassword|newPassword|secretKey|secretAccessKey|bearerToken|apiKey|sessionToken|clientSecret|secret)\"\\s*:\\s*)\"(?:[^\"\\\\]|\\\\.)*\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(500));

        #endregion

        #region Public-Methods

        /// <summary>Mask secret values in a JSON body.</summary>
        /// <param name="body">The body, or null.</param>
        /// <returns>The body with secret values replaced by <see cref="Mask"/>.</returns>
        public static string? Redact(string? body)
        {
            if (String.IsNullOrEmpty(body)) return body;
            try
            {
                return _SecretProperty.Replace(body, "$1\"" + Mask + "\"");
            }
            catch (RegexMatchTimeoutException)
            {
                // A pathological body is not stored at all rather than stored unmasked.
                return Mask;
            }
        }

        #endregion
    }
}
