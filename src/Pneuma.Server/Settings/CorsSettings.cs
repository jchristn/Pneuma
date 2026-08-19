namespace Pneuma.Server.Settings
{
    using System;

    /// <summary>
    /// CORS response header settings.
    /// </summary>
    public class CorsSettings
    {
        #region Public-Members

        /// <summary>Value for Access-Control-Allow-Origin.</summary>
        public string AllowOrigins { get; set; } = "*";

        /// <summary>Value for Access-Control-Allow-Methods.</summary>
        public string AllowMethods { get; set; } = "GET, POST, PUT, DELETE, OPTIONS, HEAD";

        /// <summary>Value for Access-Control-Allow-Headers.</summary>
        public string AllowHeaders { get; set; } = "Content-Type, Authorization, X-Api-Key, x-token, x-tenant-guid, x-email, x-password, x-access-key, x-secret-key";

        /// <summary>Value for Access-Control-Expose-Headers.</summary>
        public string ExposeHeaders { get; set; } = "*";

        /// <summary>Value for Access-Control-Max-Age in seconds.</summary>
        public int MaxAgeSeconds
        {
            get { return _MaxAgeSeconds; }
            set { _MaxAgeSeconds = Math.Clamp(value, 0, 86400); }
        }

        #endregion

        #region Private-Members

        private int _MaxAgeSeconds = 86400;

        #endregion
    }
}
