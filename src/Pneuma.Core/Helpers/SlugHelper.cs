namespace Pneuma.Core.Helpers
{
    using System;
    using System.Text;

    /// <summary>
    /// Produces URL/graph-friendly slugs from display text (lowercase, dashes for spaces).
    /// </summary>
    public static class SlugHelper
    {
        #region Public-Methods

        /// <summary>
        /// Convert a display name to a slug: lowercased, non-alphanumeric runs collapsed to single dashes,
        /// with leading and trailing dashes trimmed. Returns an empty string for null/blank input.
        /// </summary>
        /// <param name="value">The source display text.</param>
        /// <returns>The slugified value.</returns>
        public static string Slugify(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return String.Empty;

            StringBuilder sb = new StringBuilder(value.Length);
            bool lastWasDash = false;
            foreach (char c in value.Trim().ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(c);
                    lastWasDash = false;
                }
                else
                {
                    if (!lastWasDash && sb.Length > 0)
                    {
                        sb.Append('-');
                        lastWasDash = true;
                    }
                }
            }

            string result = sb.ToString();
            return result.TrimEnd('-');
        }

        #endregion
    }
}
