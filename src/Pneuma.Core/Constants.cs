namespace Pneuma.Core
{
    /// <summary>
    /// Application-wide constants, including entity identifier prefixes.
    /// </summary>
    public static class Constants
    {
        #region Identifier-Prefixes

        /// <summary>Account identifier prefix.</summary>
        public const string AccountPrefix = "acc_";

        /// <summary>Tenant identifier prefix.</summary>
        public const string TenantPrefix = "ten_";

        /// <summary>Administrator identifier prefix.</summary>
        public const string AdminPrefix = "adm_";

        /// <summary>User identifier prefix.</summary>
        public const string UserPrefix = "usr_";

        /// <summary>Credential identifier prefix.</summary>
        public const string CredentialPrefix = "crd_";

        /// <summary>Authentication session identifier prefix.</summary>
        public const string SessionPrefix = "ses_";

        /// <summary>Role identifier prefix.</summary>
        public const string RolePrefix = "rol_";

        /// <summary>Permission identifier prefix.</summary>
        public const string PermissionPrefix = "per_";

        /// <summary>Role assignment / mapping identifier prefix.</summary>
        public const string AssignmentPrefix = "asn_";

        /// <summary>Subject identifier prefix.</summary>
        public const string SubjectPrefix = "sub_";

        /// <summary>Subject link identifier prefix.</summary>
        public const string LinkPrefix = "lnk_";

        /// <summary>Ingestion job identifier prefix.</summary>
        public const string JobPrefix = "job_";

        /// <summary>Ingestion job event identifier prefix.</summary>
        public const string JobEventPrefix = "jev_";

        /// <summary>Model runner identifier prefix.</summary>
        public const string ModelRunnerPrefix = "mr_";

        /// <summary>Prompt identifier prefix.</summary>
        public const string PromptPrefix = "prm_";

        /// <summary>Source identifier prefix.</summary>
        public const string SourcePrefix = "src_";

        /// <summary>Request history entry identifier prefix.</summary>
        public const string RequestHistoryPrefix = "req_";

        /// <summary>Audit record identifier prefix.</summary>
        public const string AuditPrefix = "aud_";

        /// <summary>RecallDB collection identifier prefix.</summary>
        public const string CollectionPrefix = "col_";

        #endregion

        #region General

        /// <summary>Total identifier length, including the prefix, k-sortable, and random parts
        /// (format: {prefix}{ksortable}_{random}).</summary>
        public const int IdLength = 32;

        /// <summary>API version path segment.</summary>
        public const string ApiVersion = "v1.0";

        #endregion
    }
}
