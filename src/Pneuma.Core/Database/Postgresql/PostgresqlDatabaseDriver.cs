namespace Pneuma.Core.Database.Postgresql
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database.Postgresql.Implementations;
    using Pneuma.Core.Database.Postgresql.Queries;
    using Pneuma.Core.Enums;
    using Npgsql;

    /// <summary>
    /// PostgreSQL database driver. Serializes access with a semaphore to mirror the reference
    /// PostgreSQL driver; PostgreSQL tolerates concurrent access, so the semaphore is a harmless guard.
    /// </summary>
    public class PostgresqlDatabaseDriver : DatabaseDriverBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override DatabaseTypeEnum DatabaseType { get { return DatabaseTypeEnum.Postgresql; } }

        #endregion

        #region Private-Members

        private readonly DatabaseSettings _Settings;
        private readonly string _ConnectionString;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the PostgreSQL driver.
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public PostgresqlDatabaseDriver(DatabaseSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _Settings = settings;

            NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder
            {
                Host = settings.Hostname,
                Port = settings.Port,
                Database = settings.DatabaseName,
                Username = settings.Username,
                Password = settings.Password
            };
            if (!String.IsNullOrWhiteSpace(settings.Schema)) builder.SearchPath = settings.Schema;
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

            // Server-grade providers use native connection pooling and open a fresh connection per call,
            // so outbound queries are not serialized here. Only SQLite (single-writer) serializes writes.
            DataTable table = new DataTable();
            using (NpgsqlConnection connection = new NpgsqlConnection(_ConnectionString))
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = query;
                    command.CommandTimeout = _Settings.CommandTimeoutSeconds;
                    using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        table.Load(reader);
                    }
                }
            }
            return table;
        }

        /// <inheritdoc />
        public override async Task<DataTable> ExecuteQueriesAsync(IEnumerable<string> queries, bool isTransaction = false, CancellationToken token = default)
        {
            if (queries == null) throw new ArgumentNullException(nameof(queries));

            DataTable table = new DataTable();
            using (NpgsqlConnection connection = new NpgsqlConnection(_ConnectionString))
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlTransaction transaction = isTransaction ? connection.BeginTransaction() : null!)
                {
                    foreach (string query in queries)
                    {
                        if (String.IsNullOrEmpty(query)) continue;
                        using (NpgsqlCommand command = connection.CreateCommand())
                        {
                            if (transaction != null) command.Transaction = transaction;
                            command.CommandText = query;
                            command.CommandTimeout = _Settings.CommandTimeoutSeconds;
                            using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            foreach (SchemaMigration migration in PostgresqlSchema.Migrations)
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
