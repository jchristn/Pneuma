namespace Pneuma.Core.Security
{
    using Pneuma.Core.Enums;

    /// <summary>
    /// The authorization portion of a request context, populated after permission evaluation.
    /// </summary>
    public class AuthorizationContext
    {
        #region Public-Members

        /// <summary>Authorization result.</summary>
        public AuthorizationResultEnum Result { get; set; } = AuthorizationResultEnum.DeniedImplicit;

        /// <summary>Requested resource type.</summary>
        public ResourceTypeEnum? ResourceType { get; set; } = null;

        /// <summary>Requested operation type.</summary>
        public OperationTypeEnum? Operation { get; set; } = null;

        /// <summary>Reason authorization was bypassed, when applicable (audited).</summary>
        public string? BypassReason { get; set; } = null;

        /// <summary>Reason authorization was denied, when applicable.</summary>
        public string? DenialReason { get; set; } = null;

        #endregion
    }
}
