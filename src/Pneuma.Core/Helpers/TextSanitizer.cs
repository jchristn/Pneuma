namespace Pneuma.Core.Helpers
{
    using System;
    using System.Text;

    /// <summary>
    /// Cleans text before it is stored: a lone UTF-16 surrogate (common in text extracted from damaged files or cut by
    /// JavaScript string slicing) becomes U+FFFD, and NUL and other C0 control characters other than tab, line feed,
    /// and carriage return are removed. Valid surrogate pairs (emoji) are kept. Stateless and thread-safe.
    /// </summary>
    public static class TextSanitizer
    {
        #region Public-Methods

        /// <summary>Clean a string.</summary>
        /// <param name="text">The text; null is returned unchanged.</param>
        /// <returns>The cleaned text, or null when the input was null.</returns>
        public static string? Clean(string? text)
        {
            if (text == null) return null;
            if (!NeedsCleaning(text)) return text;

            StringBuilder builder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (Char.IsHighSurrogate(c))
                {
                    if (i + 1 < text.Length && Char.IsLowSurrogate(text[i + 1]))
                    {
                        builder.Append(c).Append(text[i + 1]);
                        i++;
                    }
                    else
                    {
                        builder.Append('�');
                    }

                    continue;
                }

                if (Char.IsLowSurrogate(c))
                {
                    builder.Append('�');
                    continue;
                }

                if (c < ' ' && c != '\t' && c != '\n' && c != '\r') continue;
                builder.Append(c);
            }

            return builder.ToString();
        }

        #endregion

        #region Private-Methods

        private static bool NeedsCleaning(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < ' ' && c != '\t' && c != '\n' && c != '\r') return true;
                if (Char.IsHighSurrogate(c))
                {
                    if (i + 1 >= text.Length || !Char.IsLowSurrogate(text[i + 1])) return true;
                    i++;
                    continue;
                }

                if (Char.IsLowSurrogate(c)) return true;
            }

            return false;
        }

        #endregion
    }
}
