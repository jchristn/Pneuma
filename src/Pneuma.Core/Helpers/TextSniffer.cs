namespace Pneuma.Core.Helpers
{
    using System;
    using System.Text;

    /// <summary>
    /// Decides whether raw bytes are plain text. Used as a fallback when the type detector reports an unknown
    /// type for content that is in fact valid UTF-8 text (for example markdown with box-drawing characters,
    /// which the detector mistakes for binary).
    /// </summary>
    public static class TextSniffer
    {
        #region Public-Methods

        /// <summary>
        /// True when the bytes decode as strict UTF-8, contain no NUL, and have almost no control characters
        /// other than tab, carriage return, line feed, and form feed.
        /// </summary>
        /// <param name="data">The bytes.</param>
        /// <returns>True when the content is text.</returns>
        public static bool IsLikelyText(byte[]? data)
        {
            if (data == null || data.Length == 0) return false;
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(data);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            int control = 0;
            foreach (char c in text)
            {
                if (c == '\0') return false;
                if (Char.IsControl(c) && c != '\t' && c != '\r' && c != '\n' && c != '\f') control++;
            }

            return control <= text.Length / 100;
        }

        #endregion
    }
}
