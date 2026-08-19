namespace Pneuma.Core.Serialization
{
    using System.Text.Json;

    /// <summary>
    /// Centralized JSON serialization settings and helpers. Emits camelCase property names, string
    /// enums, and ignores null values; inbound deserialization is case-insensitive.
    /// </summary>
    public static class Json
    {
        #region Public-Members

        /// <summary>Shared serializer options.</summary>
        public static JsonSerializerOptions Options { get; } = BuildOptions();

        #endregion

        #region Public-Methods

        /// <summary>Serialize an object to JSON.</summary>
        /// <typeparam name="T">Object type.</typeparam>
        /// <param name="value">Value to serialize.</param>
        /// <returns>JSON string.</returns>
        public static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, Options);
        }

        /// <summary>Deserialize JSON to an object.</summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="json">JSON string.</param>
        /// <returns>Deserialized value, or default when the string is null/empty.</returns>
        public static T? Deserialize<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return default;
            return JsonSerializer.Deserialize<T>(json, Options);
        }

        #endregion

        #region Private-Methods

        private static JsonSerializerOptions BuildOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            };
            return options;
        }

        #endregion
    }
}
