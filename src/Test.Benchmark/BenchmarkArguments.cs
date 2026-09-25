namespace Test.Benchmark
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// Command-line arguments: a command followed by <c>--name value</c> options and bare <c>--flag</c> switches.
    /// </summary>
    public class BenchmarkArguments
    {
        #region Public-Members

        /// <summary>
        /// The command (first positional argument), lower-cased.
        /// </summary>
        public string Command { get; private set; } = string.Empty;

        /// <summary>
        /// Every option and flag that was supplied, name to value (flags map to "true").
        /// </summary>
        public IReadOnlyDictionary<string, string> Options
        {
            get
            {
                return _Options;
            }
        }

        #endregion

        #region Private-Members

        private readonly Dictionary<string, string> _Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Parse command-line arguments.
        /// </summary>
        /// <param name="args">Raw arguments.</param>
        /// <returns>The parsed arguments.</returns>
        public static BenchmarkArguments Parse(string[] args)
        {
            BenchmarkArguments parsed = new BenchmarkArguments();
            if (args == null || args.Length == 0) return parsed;

            int start = 0;
            if (!args[0].StartsWith("--", StringComparison.Ordinal))
            {
                parsed.Command = args[0].ToLowerInvariant();
                start = 1;
            }

            for (int i = start; i < args.Length; i++)
            {
                string arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal)) continue;
                string name = arg.Substring(2);
                bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
                parsed._Options[name] = hasValue ? args[++i] : "true";
            }

            return parsed;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read a string option.
        /// </summary>
        /// <param name="name">Option name without dashes.</param>
        /// <param name="defaultValue">Value when absent.</param>
        /// <returns>The value.</returns>
        public string Get(string name, string defaultValue)
        {
            return _Options.TryGetValue(name, out string? value) ? value : defaultValue;
        }

        /// <summary>
        /// Read a string option, or null when absent.
        /// </summary>
        /// <param name="name">Option name without dashes.</param>
        /// <returns>The value or null.</returns>
        public string? GetOptional(string name)
        {
            return _Options.TryGetValue(name, out string? value) ? value : null;
        }

        /// <summary>
        /// Read an integer option.
        /// </summary>
        /// <param name="name">Option name without dashes.</param>
        /// <param name="defaultValue">Value when absent.</param>
        /// <returns>The value.</returns>
        /// <exception cref="ArgumentException">Thrown when the value is not an integer.</exception>
        public int GetInt(string name, int defaultValue)
        {
            if (!_Options.TryGetValue(name, out string? value)) return defaultValue;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                throw new ArgumentException("--" + name + " must be an integer (got '" + value + "').");
            return parsed;
        }

        /// <summary>
        /// Read a floating-point option.
        /// </summary>
        /// <param name="name">Option name without dashes.</param>
        /// <param name="defaultValue">Value when absent.</param>
        /// <returns>The value.</returns>
        /// <exception cref="ArgumentException">Thrown when the value is not a number.</exception>
        public double GetDouble(string name, double defaultValue)
        {
            if (!_Options.TryGetValue(name, out string? value)) return defaultValue;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                throw new ArgumentException("--" + name + " must be a number (got '" + value + "').");
            return parsed;
        }

        /// <summary>
        /// True when a flag was supplied (and not explicitly "false").
        /// </summary>
        /// <param name="name">Flag name without dashes.</param>
        /// <returns>True when set.</returns>
        public bool GetFlag(string name)
        {
            return _Options.TryGetValue(name, out string? value) && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Read a comma-separated list option.
        /// </summary>
        /// <param name="name">Option name without dashes.</param>
        /// <param name="defaultValue">Comma-separated default.</param>
        /// <returns>The trimmed, non-empty items.</returns>
        public List<string> GetList(string name, string defaultValue)
        {
            string raw = Get(name, defaultValue);
            List<string> items = new List<string>();
            foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) items.Add(part);
            return items;
        }

        #endregion
    }
}
