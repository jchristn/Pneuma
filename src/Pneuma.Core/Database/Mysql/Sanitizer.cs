namespace Pneuma.Core.Database.Mysql
{
    using System;

    /// <summary>
    /// Escapes values for inclusion in handwritten MySQL statements.
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
            return "'" + value.Replace("\\", "\\\\").Replace("'", "''") + "'";
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
    }
}
