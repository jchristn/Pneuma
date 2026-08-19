namespace Pneuma.Server.Settings
{
    using System;

    /// <summary>
    /// Web server (Watson) settings.
    /// </summary>
    public class RestSettings
    {
        #region Public-Members

        /// <summary>Hostname to bind. Use 127.0.0.1 for loopback.</summary>
        public string Hostname { get; set; } = "127.0.0.1";

        /// <summary>Listening port.</summary>
        public int Port
        {
            get { return _Port; }
            set { _Port = Math.Clamp(value, 1, 65535); }
        }

        /// <summary>Whether SSL/TLS is enabled.</summary>
        public bool Ssl { get; set; } = false;

        #endregion

        #region Private-Members

        private int _Port = 8080;

        #endregion
    }
}
