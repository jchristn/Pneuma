namespace Test.Benchmark.Models
{
    using System;
    using System.Globalization;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads an enum value sent either as a string or as its integer (older Pneuma builds serialized some enums as
    /// numbers) into a string, and writes an all-digit value back as a number so either kind of build accepts it.
    /// </summary>
    public class FlexibleEnumConverter : JsonConverter<string>
    {
        #region Public-Methods

        /// <inheritdoc />
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType == JsonTokenType.Number) return reader.GetInt64().ToString(CultureInfo.InvariantCulture);
            if (reader.TokenType == JsonTokenType.String) return reader.GetString();
            throw new JsonException("Expected a string or number enum value.");
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)) writer.WriteNumberValue(number);
            else writer.WriteStringValue(value);
        }

        #endregion
    }
}
