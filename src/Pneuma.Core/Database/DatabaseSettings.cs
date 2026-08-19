namespace Pneuma.Core.Database
{
    using System;
    using Pneuma.Core.Enums;

    /// <summary>
    /// Provider-neutral database configuration.
    /// </summary>
    public class DatabaseSettings
    {
        #region Public-Members

        /// <summary>Database provider.</summary>
        public DatabaseTypeEnum Type { get; set; } = DatabaseTypeEnum.Sqlite;

        /// <summary>SQLite file path (SQLite only).</summary>
        public string Filename { get; set; } = "pneuma.db";

        /// <summary>Server hostname (server providers).</summary>
        public string? Hostname { get; set; } = null;

        /// <summary>Server port (server providers).</summary>
        public int Port
        {
            get { return _Port; }
            set { _Port = value < 0 ? 0 : value; }
        }

        /// <summary>Database name (server providers).</summary>
        public string? DatabaseName { get; set; } = null;

        /// <summary>Username (server providers).</summary>
        public string? Username { get; set; } = null;

        /// <summary>Password (server providers).</summary>
        public string? Password { get; set; } = null;

        /// <summary>Schema name (PostgreSQL / SQL Server).</summary>
        public string? Schema { get; set; } = null;

        /// <summary>Maximum pooled connections.</summary>
        public int MaxConnections
        {
            get { return _MaxConnections; }
            set { _MaxConnections = Math.Clamp(value, 1, 512); }
        }

        /// <summary>Command timeout in seconds.</summary>
        public int CommandTimeoutSeconds
        {
            get { return _CommandTimeoutSeconds; }
            set { _CommandTimeoutSeconds = Math.Clamp(value, 1, 600); }
        }

        #endregion

        #region Private-Members

        private int _Port = 0;
        private int _MaxConnections = 32;
        private int _CommandTimeoutSeconds = 30;

        #endregion
    }
}
