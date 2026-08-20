namespace Pneuma.Core.Database.Sqlite
{
    using System;
    using System.Globalization;

    /// <summary>
    /// Escapes values for inclusion in handwritten SQLite statements.
    /// </summary>
    internal static class Sanitizer
    {
        /// <summary>
        /// Escape a string as a quoted SQL literal, or return NULL for null input.
        /// </summary>
        /// <param name="value">Value to escape.</param>
        /// <returns>A quoted, escaped SQL literal.</returns>
        internal static string Str(string? value)
        {
            if (value == null) return "NULL";
            return "'" + value.Replace("'", "''") + "'";
        }

        /// <summary>Format a nullable UTC timestamp as an ISO-8601 SQL literal.</summary>
        /// <param name="value">Value to format.</param>
        /// <returns>Quoted literal or NULL.</returns>
        internal static string Ts(DateTime? value)
        {
            if (value == null) return "NULL";
            return "'" + value.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + "'";
        }

        /// <summary>Format a UTC timestamp as an ISO-8601 SQL literal.</summary>
        /// <param name="value">Value to format.</param>
        /// <returns>Quoted literal.</returns>
        internal static string Ts(DateTime value)
        {
            return "'" + value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + "'";
        }

        /// <summary>Format a boolean as a 0/1 integer literal.</summary>
        /// <param name="value">Value to format.</param>
        /// <returns>"1" or "0".</returns>
        internal static string Bit(bool value)
        {
            return value ? "1" : "0";
        }

        /// <summary>Format an integer as an invariant numeric literal.</summary>
        /// <param name="value">Value to format.</param>
        /// <returns>The integer as an unquoted literal.</returns>
        internal static string Num(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Format a double as an invariant numeric literal.</summary>
        /// <param name="value">Value to format.</param>
        /// <returns>The double as an unquoted literal.</returns>
        internal static string Num(double value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
