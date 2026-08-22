namespace Pneuma.Core.Database
{
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// Serializes small enumerable values to and from the JSON text used for list columns
    /// (for example permission resource/operation types and job identifier lists).
    /// </summary>
    public static class JsonColumn
    {
        /// <summary>Serialize a string list to a JSON array.</summary>
        /// <param name="values">Values.</param>
        /// <returns>JSON array text.</returns>
        public static string FromStrings(IEnumerable<string> values)
        {
            return JsonSerializer.Serialize(values);
        }

        /// <summary>Serialize a string-keyed string dictionary to a JSON object.</summary>
        /// <param name="values">Values.</param>
        /// <returns>JSON object text.</returns>
        public static string FromDictionary(IDictionary<string, string> values)
        {
            return JsonSerializer.Serialize(values ?? new Dictionary<string, string>());
        }

        /// <summary>Serialize an enum list to a JSON array of names.</summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="values">Values.</param>
        /// <returns>JSON array text.</returns>
        public static string FromEnums<T>(IEnumerable<T> values) where T : System.Enum
        {
            List<string> names = new List<string>();
            foreach (T value in values) names.Add(value.ToString() ?? string.Empty);
            return JsonSerializer.Serialize(names);
        }
    }
}
