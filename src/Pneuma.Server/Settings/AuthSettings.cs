namespace Pneuma.Server.Settings
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Authentication settings.
    /// </summary>
    public class AuthSettings
    {
        #region Public-Members

        /// <summary>Token issuer label.</summary>
        public string Issuer { get; set; } = "pneuma";

        /// <summary>Signing key used to encrypt session tokens and secret material. Override via environment.</summary>
        public string TokenSigningKey { get; set; } = "pneuma-development-signing-key-change-me-please-0001";

        /// <summary>Session token lifetime in minutes.</summary>
        public int TokenLifetimeMinutes
        {
            get { return _TokenLifetimeMinutes; }
            set { _TokenLifetimeMinutes = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>System administrator API keys accepted via x-api-key.</summary>
        public List<string> AdminApiKeys { get; set; } = new List<string> { "pneumaadmin" };

        #endregion

        #region Private-Members

        private int _TokenLifetimeMinutes = 60;

        #endregion
    }
}
