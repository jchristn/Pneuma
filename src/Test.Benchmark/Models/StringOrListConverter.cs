namespace Test.Benchmark.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads a JSON value that may be a single string or an array of strings (Ollama and OpenAI accept both for
    /// embedding input) into a list.
    /// </summary>
    public class StringOrListConverter : JsonConverter<List<string>>
    {
        #region Public-Methods

        /// <inheritdoc />
        public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            List<string> values = new List<string>();
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType == JsonTokenType.String)
            {
                values.Add(reader.GetString() ?? string.Empty);
                return values;
            }

            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected a string or an array of strings.");
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                values.Add(reader.TokenType == JsonTokenType.String ? reader.GetString() ?? string.Empty : string.Empty);
            }

            return values;
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (string item in value) writer.WriteStringValue(item);
            writer.WriteEndArray();
        }

        #endregion
    }
}
