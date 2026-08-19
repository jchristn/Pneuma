namespace Pneuma.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Text.Json;

    /// <summary>
    /// Reads and coerces values from a <see cref="DataRow"/> across providers, whose ADO drivers
    /// may return different CLR types for the same logical column.
    /// </summary>
    public static class RowReader
    {
        #region Public-Methods

        /// <summary>Read a required string column.</summary>
        /// <param name="row">Data row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>String value, or empty when null.</returns>
        public static string GetString(DataRow row, string column)
        {
            object value = row[column];
            if (value == null || value == DBNull.Value) return String.Empty;
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty;
        }

        /// <summary>Read a nullable string column.</summary>
        /// <param name="row">Data row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>String value, or null.</returns>
        public static string? GetNullableString(DataRow row, string column)
        {
            object value = row[column];
            if (value == null || value == DBNull.Value) return null;
            string s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty;
            return s;
        }

        /// <summary>Read a boolean column (accepts bool, integer, or "0"/"1"/"true"/"false").</summary>
        /// <param name="row">Data row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Boolean value.</returns>
        public static bool GetBool(DataRow row, string column)
        {
            object value = row[column];
            if (value == null || value == DBNull.Value) return false;
            if (value is bool b) return b;
            if (value is long l) return l != 0;
            if (value is int i) return i != 0;
            if (value is short sh) return sh != 0;
            if (value is byte by) return by != 0;
            string s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
            if (s == "1") return true;
            if (s == "0") return false;
            return bool.TryParse(s, out bool parsed) && parsed;
        }

        /// <summary>Read an integer column.</summary>
        /// <param name="row">Data row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Integer value.</returns>
        public static int GetInt(DataRow row, string column)
        {
            object value = row[column];
            if (value == null || value == DBNull.Value) return 0;
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        /// <summary>Read a long column.</summary>
        /// <param name="row">Data row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Long value.</returns>
        public static long GetLong(DataRow row, string column)
        {
            object value = row[column];
            if (value == null || value == DBNull.Value) return 0;
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        /// <summary>Read a double column.</summary>
        /// <param name="row">Data row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Double value.</returns>
        public static double GetDouble(DataRow row, string column)
        {
            object value = row[column];
            if (value == null || value == DBNull.Value) return 0;
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        /// <summary>Read a required UTC timestamp column (accepts DateTime or ISO-8601 string).</summary>
        /// <param name="row">Data row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>UTC DateTime.</returns>
        public static DateTime GetDateTime(DataRow row, string column)
        {
            DateTime? value = GetNullableDateTime(row, column);
            return value ?? DateTime.UtcNow;
        }

        /// <summary>Read a nullable UTC timestamp column (accepts DateTime or ISO-8601 string).</summary>
        /// <param name="row">Data row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>UTC DateTime, or null.</returns>
        public static DateTime? GetNullableDateTime(DataRow row, string column)
        {
            object value = row[column];
            if (value == null || value == DBNull.Value) return null;
            if (value is DateTime dt) return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            string s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty;
            if (String.IsNullOrWhiteSpace(s)) return null;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
            return null;
        }

        /// <summary>Read an enum column stored as its string name.</summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="row">Data row.</param>
        /// <param name="column">Column name.</param>
        /// <param name="fallback">Value returned when parsing fails.</param>
        /// <returns>Parsed enum value or the fallback.</returns>
        public static T GetEnum<T>(DataRow row, string column, T fallback) where T : struct, Enum
        {
            string s = GetNullableString(row, column) ?? String.Empty;
            if (Enum.TryParse<T>(s, true, out T parsed)) return parsed;
            return fallback;
        }

        /// <summary>Read a nullable enum column stored as its string name.</summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="row">Data row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Parsed enum value, or null.</returns>
        public static T? GetNullableEnum<T>(DataRow row, string column) where T : struct, Enum
        {
            string? s = GetNullableString(row, column);
            if (String.IsNullOrWhiteSpace(s)) return null;
            if (Enum.TryParse<T>(s, true, out T parsed)) return parsed;
            return null;
        }

        /// <summary>Read a JSON array column into a string list.</summary>
        /// <param name="row">Data row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>String list (empty when null/invalid).</returns>
        public static List<string> GetStringList(DataRow row, string column)
        {
            string? s = GetNullableString(row, column);
            if (String.IsNullOrWhiteSpace(s)) return new List<string>();
            try
            {
                List<string>? list = JsonSerializer.Deserialize<List<string>>(s);
                return list ?? new List<string>();
            }
            catch (JsonException)
            {
                return new List<string>();
            }
        }

        /// <summary>Read a JSON array of enum names into an enum list.</summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="row">Data row.</param>
        /// <param name="column">Column name.</param>
        /// <returns>Enum list (empty when null/invalid).</returns>
        public static List<T> GetEnumList<T>(DataRow row, string column) where T : struct, Enum
        {
            List<T> result = new List<T>();
            foreach (string item in GetStringList(row, column))
            {
                if (Enum.TryParse<T>(item, true, out T parsed)) result.Add(parsed);
            }
            return result;
        }

        #endregion
    }
}
