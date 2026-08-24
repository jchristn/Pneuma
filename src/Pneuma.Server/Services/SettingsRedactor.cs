namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Server.Settings;

    /// <summary>
    /// Shared secret redaction for <see cref="AppSettings"/>. Masking a settings snapshot on read and
    /// restoring a still-masked value on write are defined here in one place so the settings REST route and
    /// the MCP settings tool cannot drift on which fields are secret.
    /// </summary>
    public static class SettingsRedactor
    {
        #region Public-Members

        /// <summary>The placeholder written in place of a stored secret on read.</summary>
        public const string SecretMask = "********";

        #endregion

        #region Public-Methods

        /// <summary>Mask every secret field on the supplied settings instance in place.</summary>
        /// <param name="s">The settings instance to mask (typically a deep copy of the live settings).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="s"/> is null.</exception>
        public static void Mask(AppSettings s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (!String.IsNullOrEmpty(s.Auth.TokenSigningKey)) s.Auth.TokenSigningKey = SecretMask;
            if (s.Auth.AdminApiKeys != null && s.Auth.AdminApiKeys.Count > 0) s.Auth.AdminApiKeys = new List<string> { SecretMask };
            if (!String.IsNullOrEmpty(s.Database.Password)) s.Database.Password = SecretMask;
            if (!String.IsNullOrEmpty(s.Integrations.RecallDb.BearerToken)) s.Integrations.RecallDb.BearerToken = SecretMask;
            if (!String.IsNullOrEmpty(s.Integrations.Partio.BearerToken)) s.Integrations.Partio.BearerToken = SecretMask;
            if (!String.IsNullOrEmpty(s.Integrations.LiteGraph.BearerToken)) s.Integrations.LiteGraph.BearerToken = SecretMask;
            if (!String.IsNullOrEmpty(s.S3.AccessKey)) s.S3.AccessKey = SecretMask;
            if (!String.IsNullOrEmpty(s.S3.SecretKey)) s.S3.SecretKey = SecretMask;
            if (!String.IsNullOrEmpty(s.Seed.AdminPassword)) s.Seed.AdminPassword = SecretMask;
        }

        /// <summary>
        /// Restore any still-masked secret on <paramref name="incoming"/> from the corresponding value on
        /// <paramref name="current"/>, so a client that submits an unchanged masked value preserves the stored
        /// secret rather than overwriting it with the mask.
        /// </summary>
        /// <param name="incoming">The submitted settings (mutated in place).</param>
        /// <param name="current">The live settings that hold the real secrets.</param>
        /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
        public static void Restore(AppSettings incoming, AppSettings current)
        {
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (incoming.Auth.TokenSigningKey == SecretMask) incoming.Auth.TokenSigningKey = current.Auth.TokenSigningKey;
            if (incoming.Auth.AdminApiKeys != null && incoming.Auth.AdminApiKeys.Count == 1 && incoming.Auth.AdminApiKeys[0] == SecretMask)
                incoming.Auth.AdminApiKeys = current.Auth.AdminApiKeys;
            if (incoming.Database.Password == SecretMask) incoming.Database.Password = current.Database.Password;
            if (incoming.Integrations.RecallDb.BearerToken == SecretMask) incoming.Integrations.RecallDb.BearerToken = current.Integrations.RecallDb.BearerToken;
            if (incoming.Integrations.Partio.BearerToken == SecretMask) incoming.Integrations.Partio.BearerToken = current.Integrations.Partio.BearerToken;
            if (incoming.Integrations.LiteGraph.BearerToken == SecretMask) incoming.Integrations.LiteGraph.BearerToken = current.Integrations.LiteGraph.BearerToken;
            if (incoming.S3.AccessKey == SecretMask) incoming.S3.AccessKey = current.S3.AccessKey;
            if (incoming.S3.SecretKey == SecretMask) incoming.S3.SecretKey = current.S3.SecretKey;
            if (incoming.Seed.AdminPassword == SecretMask) incoming.Seed.AdminPassword = current.Seed.AdminPassword;
        }

        #endregion
    }
}
