namespace Pneuma.Core.Helpers
{
    /// <summary>
    /// Generates application identifiers as k-sortable, prefixed strings.
    /// </summary>
    public static class IdGenerator
    {
        #region Private-Members

        // Fully qualified to avoid ambiguity with this class, which shares the name "IdGenerator".
        private static readonly PrettyId.IdGenerator _Generator = new PrettyId.IdGenerator();
        // Total ID length (prefix + k-sortable + random), passed as GenerateKSortable's maxLen.
        private static readonly int _MaxIdLength = Constants.IdLength;

        #endregion

        #region Public-Methods

        /// <summary>Generate an account identifier.</summary>
        /// <returns>Account identifier.</returns>
        public static string GenerateAccountId() => Generate(Constants.AccountPrefix);

        /// <summary>Generate a tenant identifier.</summary>
        /// <returns>Tenant identifier.</returns>
        public static string GenerateTenantId() => Generate(Constants.TenantPrefix);

        /// <summary>Generate an administrator identifier.</summary>
        /// <returns>Administrator identifier.</returns>
        public static string GenerateAdminId() => Generate(Constants.AdminPrefix);

        /// <summary>Generate a user identifier.</summary>
        /// <returns>User identifier.</returns>
        public static string GenerateUserId() => Generate(Constants.UserPrefix);

        /// <summary>Generate a credential identifier.</summary>
        /// <returns>Credential identifier.</returns>
        public static string GenerateCredentialId() => Generate(Constants.CredentialPrefix);

        /// <summary>Generate an authentication session identifier.</summary>
        /// <returns>Session identifier.</returns>
        public static string GenerateSessionId() => Generate(Constants.SessionPrefix);

        /// <summary>Generate a role identifier.</summary>
        /// <returns>Role identifier.</returns>
        public static string GenerateRoleId() => Generate(Constants.RolePrefix);

        /// <summary>Generate a permission identifier.</summary>
        /// <returns>Permission identifier.</returns>
        public static string GeneratePermissionId() => Generate(Constants.PermissionPrefix);

        /// <summary>Generate a role assignment identifier.</summary>
        /// <returns>Assignment identifier.</returns>
        public static string GenerateAssignmentId() => Generate(Constants.AssignmentPrefix);

        /// <summary>Generate a subject identifier.</summary>
        /// <returns>Subject identifier.</returns>
        public static string GenerateSubjectId() => Generate(Constants.SubjectPrefix);

        /// <summary>Generate a subject link identifier.</summary>
        /// <returns>Link identifier.</returns>
        public static string GenerateLinkId() => Generate(Constants.LinkPrefix);

        /// <summary>Generate an ingestion job identifier.</summary>
        /// <returns>Job identifier.</returns>
        public static string GenerateJobId() => Generate(Constants.JobPrefix);

        /// <summary>Generate an ingestion job event identifier.</summary>
        /// <returns>Job event identifier.</returns>
        public static string GenerateJobEventId() => Generate(Constants.JobEventPrefix);

        /// <summary>Generate a model runner identifier.</summary>
        /// <returns>Model runner identifier.</returns>
        public static string GenerateModelRunnerId() => Generate(Constants.ModelRunnerPrefix);

        /// <summary>Generate a prompt identifier.</summary>
        /// <returns>Prompt identifier.</returns>
        public static string GeneratePromptId() => Generate(Constants.PromptPrefix);

        /// <summary>Generate a source identifier.</summary>
        /// <returns>Source identifier.</returns>
        public static string GenerateSourceId() => Generate(Constants.SourcePrefix);

        /// <summary>Generate a request history entry identifier.</summary>
        /// <returns>Request history identifier.</returns>
        public static string GenerateRequestHistoryId() => Generate(Constants.RequestHistoryPrefix);

        /// <summary>Generate an audit record identifier.</summary>
        /// <returns>Audit identifier.</returns>
        public static string GenerateAuditId() => Generate(Constants.AuditPrefix);

        /// <summary>Generate a RecallDB collection identifier.</summary>
        /// <returns>Collection identifier.</returns>
        public static string GenerateCollectionId() => Generate(Constants.CollectionPrefix);

        #endregion

        #region Private-Methods

        private static string Generate(string prefix)
        {
            return _Generator.GenerateKSortable(prefix, _MaxIdLength);
        }

        #endregion
    }
}
