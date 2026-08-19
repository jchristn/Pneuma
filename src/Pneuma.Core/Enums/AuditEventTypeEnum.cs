namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Security-relevant event types recorded in the audit stream.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AuditEventTypeEnum
    {
        /// <summary>Authentication succeeded.</summary>
        AuthSuccess,
        /// <summary>Authentication failed.</summary>
        AuthFailure,
        /// <summary>A session token was issued.</summary>
        SessionIssued,
        /// <summary>A session token was refreshed.</summary>
        SessionRefreshed,
        /// <summary>A session token was revoked.</summary>
        SessionRevoked,
        /// <summary>A credential was used to authenticate.</summary>
        CredentialUsed,
        /// <summary>Authorization was denied.</summary>
        AuthorizationDenied,
        /// <summary>Authorization was bypassed through an approved administrative path.</summary>
        AuthorizationBypass,
        /// <summary>A role, permission, or assignment changed.</summary>
        RoleChanged
    }
}
