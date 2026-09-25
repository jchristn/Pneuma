namespace Test.Benchmark.Models
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Writes Ollama's <c>think</c> option: "true"/"false" as booleans, and a level ("low", "medium", "high") as a
    /// string (reasoning models such as gpt-oss accept only levels).
    /// </summary>
    public class ThinkConverter : JsonConverter<string>
    {
        #region Public-Methods

        /// <inheritdoc />
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.True) return "true";
            if (reader.TokenType == JsonTokenType.False) return "false";
            return reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) writer.WriteBooleanValue(true);
            else if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) writer.WriteBooleanValue(false);
            else writer.WriteStringValue(value);
        }

        #endregion
    }
}
