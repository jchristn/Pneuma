namespace Pneuma.Server.Settings
{
    using System;

    /// <summary>
    /// Logging settings.
    /// </summary>
    public class LoggingSettings
    {
        #region Public-Members

        /// <summary>Whether to log to the console.</summary>
        public bool ConsoleLogging { get; set; } = true;

        /// <summary>Whether to log to files.</summary>
        public bool FileLogging { get; set; } = true;

        /// <summary>Directory for log files.</summary>
        public string LogDirectory { get; set; } = "logs";

        /// <summary>Base log filename.</summary>
        public string LogFilename { get; set; } = "pneuma.log";

        /// <summary>Minimum severity to emit (0=Debug .. 5=Emergency).</summary>
        public int MinimumSeverity
        {
            get { return _MinimumSeverity; }
            set { _MinimumSeverity = Math.Clamp(value, 0, 7); }
        }

        /// <summary>Whether to log each HTTP request line.</summary>
        public bool LogHttpRequests { get; set; } = false;

        #endregion

        #region Private-Members

        private int _MinimumSeverity = 1;

        #endregion
    }
}
