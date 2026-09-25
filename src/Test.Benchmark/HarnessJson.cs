namespace Test.Benchmark
{
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Shared JSON options: camelCase, case-insensitive reads, string enums, nulls omitted on write (so a request
    /// never overwrites a server default with an explicit null), and readable output for reports.
    /// </summary>
    public static class HarnessJson
    {
        #region Public-Members

        /// <summary>
        /// Options for API requests/responses and dataset files.
        /// </summary>
        public static readonly JsonSerializerOptions Options = Build(false);

        /// <summary>
        /// Indented options for reports and datasets written to disk.
        /// </summary>
        public static readonly JsonSerializerOptions Indented = Build(true);

        #endregion

        #region Private-Methods

        private static JsonSerializerOptions Build(bool indented)
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = indented,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        #endregion
    }
}
