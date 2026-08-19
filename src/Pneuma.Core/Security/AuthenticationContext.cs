namespace Pneuma.Core.Security
{
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;

    /// <summary>
    /// The authentication portion of a request context, populated after credential validation.
    /// </summary>
    public class AuthenticationContext
    {
        #region Public-Members

        /// <summary>Authentication result.</summary>
        public AuthenticationResultEnum Result { get; set; } = AuthenticationResultEnum.Invalid;

        /// <summary>Authentication scheme used.</summary>
        public AuthSchemeEnum? Scheme { get; set; } = null;

        /// <summary>Resolved principal type.</summary>
        public PrincipalTypeEnum? PrincipalType { get; set; } = null;

        /// <summary>Resolved principal identifier.</summary>
        public string? PrincipalId { get; set; } = null;

        /// <summary>Session identifier, if a session was used or created.</summary>
        public string? SessionId { get; set; } = null;

        /// <summary>Authenticated user, if applicable.</summary>
        public User? User { get; set; } = null;

        /// <summary>Authenticated administrator, if applicable.</summary>
        public Administrator? Administrator { get; set; } = null;

        /// <summary>Credential used for authentication, if applicable.</summary>
        public Credential? Credential { get; set; } = null;

        /// <summary>Resolved tenant, if applicable.</summary>
        public Tenant? Tenant { get; set; } = null;

        /// <summary>Optional failure message for diagnostics (never leaked to clients).</summary>
        public string? Message { get; set; } = null;

        #endregion
    }
}
