namespace Pneuma.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Enums;

    /// <summary>
    /// Abstract base class for provider-specific database drivers. Exposes domain-specific
    /// method interfaces and low-level query execution.
    /// </summary>
    public abstract class DatabaseDriverBase : IDisposable, IAsyncDisposable
    {
        #region Public-Members

        /// <summary>Database provider type.</summary>
        public abstract DatabaseTypeEnum DatabaseType { get; }

        /// <summary>Account methods.</summary>
        public IAccountMethods Accounts { get; protected set; } = null!;

        /// <summary>Tenant methods.</summary>
        public ITenantMethods Tenants { get; protected set; } = null!;

        /// <summary>Administrator methods.</summary>
        public IAdministratorMethods Administrators { get; protected set; } = null!;

        /// <summary>User methods.</summary>
        public IUserMethods Users { get; protected set; } = null!;

        /// <summary>Credential methods.</summary>
        public ICredentialMethods Credentials { get; protected set; } = null!;

        /// <summary>Authentication session methods.</summary>
        public IAuthSessionMethods Sessions { get; protected set; } = null!;

        /// <summary>Role methods.</summary>
        public IRoleMethods Roles { get; protected set; } = null!;

        /// <summary>Permission methods.</summary>
        public IPermissionMethods Permissions { get; protected set; } = null!;

        /// <summary>Role-permission map methods.</summary>
        public IRolePermissionMapMethods RolePermissionMaps { get; protected set; } = null!;

        /// <summary>User role assignment methods.</summary>
        public IUserRoleAssignmentMethods UserRoleAssignments { get; protected set; } = null!;

        /// <summary>Legacy user role map methods.</summary>
        public IUserRoleMapMethods UserRoleMaps { get; protected set; } = null!;

        /// <summary>Credential scope assignment methods.</summary>
        public ICredentialScopeAssignmentMethods CredentialScopeAssignments { get; protected set; } = null!;

        /// <summary>Audit methods.</summary>
        public IAuditMethods Audit { get; protected set; } = null!;

        /// <summary>Request history methods.</summary>
        public IRequestHistoryMethods RequestHistory { get; protected set; } = null!;

        /// <summary>Subject methods.</summary>
        public ISubjectMethods Subjects { get; protected set; } = null!;

        /// <summary>Subject link methods.</summary>
        public ISubjectLinkMethods SubjectLinks { get; protected set; } = null!;

        /// <summary>Ingestion job methods.</summary>
        public IIngestionJobMethods IngestionJobs { get; protected set; } = null!;

        /// <summary>Ingestion job event methods.</summary>
        public IIngestionJobEventMethods IngestionJobEvents { get; protected set; } = null!;

        /// <summary>Model runner methods.</summary>
        public IModelRunnerMethods ModelRunners { get; protected set; } = null!;

        /// <summary>Prompt methods.</summary>
        public IPromptMethods Prompts { get; protected set; } = null!;

        /// <summary>Persisted chat-turn (history) methods.</summary>
        public IChatTurnMethods ChatTurns { get; protected set; } = null!;

        /// <summary>Chat feedback methods.</summary>
        public IChatFeedbackMethods ChatFeedback { get; protected set; } = null!;

        /// <summary>Chat-turn performance-event methods.</summary>
        public IChatTurnPerfEventMethods ChatTurnPerfEvents { get; protected set; } = null!;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Initialize the driver: open connectivity, run migrations, and seed first-boot data.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        public abstract Task InitializeAsync(CancellationToken token = default);

        /// <summary>
        /// Execute a single query and return the resulting table.
        /// </summary>
        /// <param name="query">Query text.</param>
        /// <param name="isTransaction">Whether to run within a transaction.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result table.</returns>
        public abstract Task<DataTable> ExecuteQueryAsync(string query, bool isTransaction = false, CancellationToken token = default);

        /// <summary>
        /// Execute multiple queries, optionally within a single transaction.
        /// </summary>
        /// <param name="queries">Query batch.</param>
        /// <param name="isTransaction">Whether to run within a transaction.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result table of the final query.</returns>
        public abstract Task<DataTable> ExecuteQueriesAsync(IEnumerable<string> queries, bool isTransaction = false, CancellationToken token = default);

        /// <summary>
        /// Close the driver and release resources.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        public abstract Task CloseAsync(CancellationToken token = default);

        /// <summary>Dispose the driver.</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Dispose the driver asynchronously.</summary>
        /// <returns>A task.</returns>
        public async ValueTask DisposeAsync()
        {
            await CloseAsync().ConfigureAwait(false);
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose managed resources.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
        }

        #endregion
    }
}
