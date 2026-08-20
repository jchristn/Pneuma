namespace Pneuma.Core.Database.Sqlite
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database.Sqlite.Implementations;
    using Pneuma.Core.Database.Sqlite.Queries;
    using Pneuma.Core.Enums;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// SQLite database driver. Serializes access with a semaphore, as SQLite does not tolerate
    /// concurrent writers.
    /// </summary>
    public class SqliteDatabaseDriver : DatabaseDriverBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override DatabaseTypeEnum DatabaseType { get { return DatabaseTypeEnum.Sqlite; } }

        #endregion

        #region Private-Members

        private readonly DatabaseSettings _Settings;
        private readonly string _ConnectionString;
        private readonly SemaphoreSlim _Semaphore = new SemaphoreSlim(1, 1);
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the SQLite driver.
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public SqliteDatabaseDriver(DatabaseSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _Settings = settings;

            SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder
            {
                DataSource = String.IsNullOrWhiteSpace(settings.Filename) ? "pneuma.db" : settings.Filename,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            };
            _ConnectionString = builder.ToString();

            Accounts = new AccountMethods(this);
            Tenants = new TenantMethods(this);
            Administrators = new AdministratorMethods(this);
            Users = new UserMethods(this);
            Credentials = new CredentialMethods(this);
            Sessions = new AuthSessionMethods(this);
            Roles = new RoleMethods(this);
            Permissions = new PermissionMethods(this);
            RolePermissionMaps = new RolePermissionMapMethods(this);
            UserRoleAssignments = new UserRoleAssignmentMethods(this);
            UserRoleMaps = new UserRoleMapMethods(this);
            CredentialScopeAssignments = new CredentialScopeAssignmentMethods(this);
            Audit = new AuditMethods(this);
            RequestHistory = new RequestHistoryMethods(this);
            Subjects = new SubjectMethods(this);
            SubjectLinks = new SubjectLinkMethods(this);
            IngestionJobs = new IngestionJobMethods(this);
            IngestionJobEvents = new IngestionJobEventMethods(this);
            ModelRunners = new ModelRunnerMethods(this);
            Prompts = new PromptMethods(this);
            ChatTurns = new ChatTurnMethods(this);
            ChatFeedback = new ChatFeedbackMethods(this);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task InitializeAsync(CancellationToken token = default)
        {
            await ApplyMigrationsAsync(token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task<DataTable> ExecuteQueryAsync(string query, bool isTransaction = false, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(query)) throw new ArgumentNullException(nameof(query));

            await _Semaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                DataTable table = new DataTable();
                using (SqliteConnection connection = new SqliteConnection(_ConnectionString))
                {
                    await connection.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = query;
                        command.CommandTimeout = _Settings.CommandTimeoutSeconds;
                        using (SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            table.Load(reader);
                        }
                    }
                }
                return table;
            }
            finally
            {
                _Semaphore.Release();
            }
        }

        /// <inheritdoc />
        public override async Task<DataTable> ExecuteQueriesAsync(IEnumerable<string> queries, bool isTransaction = false, CancellationToken token = default)
        {
            if (queries == null) throw new ArgumentNullException(nameof(queries));

            await _Semaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                DataTable table = new DataTable();
                using (SqliteConnection connection = new SqliteConnection(_ConnectionString))
                {
                    await connection.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteTransaction transaction = isTransaction ? connection.BeginTransaction() : null!)
                    {
                        foreach (string query in queries)
                        {
                            if (String.IsNullOrEmpty(query)) continue;
                            using (SqliteCommand command = connection.CreateCommand())
                            {
                                if (transaction != null) command.Transaction = transaction;
                                command.CommandText = query;
                                command.CommandTimeout = _Settings.CommandTimeoutSeconds;
                                using (SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                                {
                                    table = new DataTable();
                                    table.Load(reader);
                                }
                            }
                        }
                        if (transaction != null) await transaction.CommitAsync(token).ConfigureAwait(false);
                    }
                }
                return table;
            }
            finally
            {
                _Semaphore.Release();
            }
        }

        /// <inheritdoc />
        public override Task CloseAsync(CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                _Semaphore.Dispose();
            }
            _Disposed = true;
            base.Dispose(disposing);
        }

        #endregion

        #region Private-Methods

        private async Task ApplyMigrationsAsync(CancellationToken token)
        {
            await ExecuteQueryAsync(
                "CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY, description TEXT, appliedutc TEXT NOT NULL);",
                false, token).ConfigureAwait(false);

            DataTable applied = await ExecuteQueryAsync("SELECT version FROM schema_migrations;", false, token).ConfigureAwait(false);
            HashSet<int> appliedVersions = new HashSet<int>();
            foreach (DataRow row in applied.Rows)
            {
                appliedVersions.Add(RowReader.GetInt(row, "version"));
            }

            foreach (SchemaMigration migration in SqliteSchema.Migrations)
            {
                if (appliedVersions.Contains(migration.Version)) continue;

                await ExecuteQueriesAsync(migration.Statements, true, token).ConfigureAwait(false);

                string record =
                    "INSERT INTO schema_migrations (version, description, appliedutc) VALUES (" +
                    migration.Version + ", " + Sanitizer.Str(migration.Description) + ", " +
                    Sanitizer.Ts(DateTime.UtcNow) + ");";
                await ExecuteQueryAsync(record, false, token).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
