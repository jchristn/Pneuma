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

        /// <summary>Per-subject prompt override identifier prefix.</summary>
        public const string SubjectPromptPrefix = "sp_";

        /// <summary>Source identifier prefix.</summary>
        public const string SourcePrefix = "src_";

        /// <summary>Request history entry identifier prefix.</summary>
        public const string RequestHistoryPrefix = "req_";

        /// <summary>Audit record identifier prefix.</summary>
        public const string AuditPrefix = "aud_";

        /// <summary>RecallDB collection identifier prefix.</summary>
        public const string CollectionPrefix = "col_";

        /// <summary>Persisted chat-turn identifier prefix.</summary>
        public const string ChatTurnPrefix = "trn_";

        /// <summary>Chat feedback identifier prefix.</summary>
        public const string ChatFeedbackPrefix = "fbk_";

        /// <summary>Chat-turn performance-event identifier prefix.</summary>
        public const string PerfEventPrefix = "perf_";

        /// <summary>Chat thread (conversation) identifier prefix.</summary>
        public const string ChatThreadPrefix = "thr_";

        /// <summary>Chat tool-call identifier prefix.</summary>
        public const string ChatToolCallPrefix = "tcall_";

        /// <summary>Evaluation ground-truth fact identifier prefix.</summary>
        public const string EvalFactPrefix = "efact_";

        /// <summary>Evaluation run identifier prefix.</summary>
        public const string EvalRunPrefix = "erun_";

        /// <summary>Evaluation result identifier prefix.</summary>
        public const string EvalResultPrefix = "eres_";

        /// <summary>Ingestion job attempt identifier prefix.</summary>
        public const string JobAttemptPrefix = "jatt_";

        /// <summary>Crawl plan identifier prefix.</summary>
        public const string CrawlPlanPrefix = "cpl_";

        /// <summary>Crawl operation identifier prefix.</summary>
        public const string CrawlOperationPrefix = "cop_";

        /// <summary>Crawl object identifier prefix (one object a crawl plan has seen).</summary>
        public const string CrawlObjectPrefix = "cob_";

        /// <summary>Crawl operation object identifier prefix (one object's outcome within one operation).</summary>
        public const string CrawlOperationObjectPrefix = "coo_";

        /// <summary>Ontology identifier prefix (a tenant's governed ontology).</summary>
        public const string OntologyPrefix = "ont_";

        /// <summary>Ontology version identifier prefix.</summary>
        public const string OntologyVersionPrefix = "onv_";

        /// <summary>Ontology rule identifier prefix.</summary>
        public const string OntologyRulePrefix = "orl_";

        /// <summary>Ontology violation identifier prefix (one rule violation found in a subject's content).</summary>
        public const string OntologyViolationPrefix = "ovl_";

        /// <summary>Ontology operation identifier prefix (a background validate, retag, or drift check run).</summary>
        public const string OntologyOperationPrefix = "oop_";

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
