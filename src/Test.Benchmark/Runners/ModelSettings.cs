namespace Test.Benchmark.Runners
{
    using System;

    /// <summary>
    /// A model endpoint named on the command line (embedding model, inference model, or judge).
    /// </summary>
    public class ModelSettings
    {
        #region Public-Members

        /// <summary>
        /// ollama or openai wire format.
        /// </summary>
        public string Format { get; set; } = "ollama";

        /// <summary>
        /// Base URL.
        /// </summary>
        public string Url { get; set; } = "http://127.0.0.1:11434";

        /// <summary>
        /// Model name.
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Optional API key.
        /// </summary>
        public string? ApiKey { get; set; } = null;

        /// <summary>
        /// Ollama reasoning control for direct completions (false, low, medium, high).
        /// </summary>
        public string Think { get; set; } = "false";

        /// <summary>
        /// Pneuma provider name for this format.
        /// </summary>
        public string Provider
        {
            get
            {
                return string.Equals(Format, "openai", StringComparison.OrdinalIgnoreCase) ? "OpenAICompatible" : "Ollama";
            }
        }

        /// <summary>
        /// A one-line description for reports.
        /// </summary>
        public string Description
        {
            get
            {
                return Format + ":" + Model + " @ " + Url;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Read <c>--{prefix}-model</c>, <c>--{prefix}-url</c>, <c>--{prefix}-format</c>, and <c>--{prefix}-api-key</c>.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <param name="prefix">Option prefix (embedding, inference, judge).</param>
        /// <param name="defaultModel">Default model.</param>
        /// <param name="fallback">Settings to inherit URL and format from when not given.</param>
        /// <returns>The settings.</returns>
        public static ModelSettings FromArguments(BenchmarkArguments args, string prefix, string defaultModel, ModelSettings? fallback)
        {
            return new ModelSettings
            {
                Model = args.Get(prefix + "-model", defaultModel),
                Url = args.Get(prefix + "-url", fallback?.Url ?? "http://127.0.0.1:11434").TrimEnd('/'),
                Format = args.Get(prefix + "-format", fallback?.Format ?? "ollama").ToLowerInvariant(),
                ApiKey = args.GetOptional(prefix + "-api-key") ?? fallback?.ApiKey,
                Think = args.Get(prefix + "-think", "false")
            };
        }

        #endregion
    }
}
