namespace Test.Shared.Support
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;

    /// <summary>
    /// Creates fresh, isolated databases for contract tests. Defaults to SQLite on a temp file;
    /// honors the PNEUMA_TEST_DB_TYPE environment variable so the same suites can run against other
    /// providers in the four-provider matrix. For the server providers a unique database is created
    /// per call (via a maintenance connection) so every test case runs against an isolated schema,
    /// exactly as each SQLite case gets its own file.
    /// </summary>
    public static class TestDatabase
    {
        /// <summary>
        /// Create and initialize a fresh database driver, returning it along with a disposer that
        /// removes any temporary files.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An initialized driver.</returns>
        public static async Task<DatabaseDriverBase> CreateAsync(CancellationToken token = default)
        {
            DatabaseSettings settings = await BuildSettingsAsync(token).ConfigureAwait(false);
            DatabaseDriverBase driver = await DatabaseDriverFactory.CreateAndInitializeAsync(settings, token).ConfigureAwait(false);
            await FirstBootSeeder.SeedAsync(driver, new SeedOptions(), new Pneuma.Core.Security.Aes256Cipher("pneuma-test-signing-key-0001"), token).ConfigureAwait(false);
            return driver;
        }

        private static async Task<DatabaseSettings> BuildSettingsAsync(CancellationToken token)
        {
            string? typeName = Environment.GetEnvironmentVariable("PNEUMA_TEST_DB_TYPE");
            DatabaseTypeEnum type = DatabaseTypeEnum.Sqlite;
            if (!String.IsNullOrWhiteSpace(typeName) && Enum.TryParse<DatabaseTypeEnum>(typeName, true, out DatabaseTypeEnum parsed))
            {
                type = parsed;
            }

            DatabaseSettings settings = new DatabaseSettings { Type = type };

            if (type == DatabaseTypeEnum.Sqlite)
            {
                string dir = Path.Combine(Path.GetTempPath(), "pneuma-tests");
                Directory.CreateDirectory(dir);
                settings.Filename = Path.Combine(dir, "pneuma-test-" + Guid.NewGuid().ToString("N") + ".db");
                return settings;
            }

            string host = EnvOr("PNEUMA_TEST_DB_HOST", "127.0.0.1");
            int port = IntEnvOr("PNEUMA_TEST_DB_PORT", DefaultPort(type));
            string user = EnvOr("PNEUMA_TEST_DB_USER", DefaultUser(type));
            string password = EnvOr("PNEUMA_TEST_DB_PASSWORD", "postgres");
            string? schema = Environment.GetEnvironmentVariable("PNEUMA_TEST_DB_SCHEMA");
            string? explicitDatabase = Environment.GetEnvironmentVariable("PNEUMA_TEST_DB_NAME");

            settings.Hostname = host;
            settings.Port = port;
            settings.Username = user;
            settings.Password = password;
            if (!String.IsNullOrWhiteSpace(schema)) settings.Schema = schema;

            // Isolate each case in its own database, created through a maintenance connection. The maintenance
            // database defaults to the provider's built-in admin database (postgres/mysql/master); a supplied
            // database name (--database / PNEUMA_TEST_DB_NAME) overrides which database the admin connection
            // targets to issue CREATE DATABASE, for servers where the default admin database is not reachable.
            string maintenanceDatabase = String.IsNullOrWhiteSpace(explicitDatabase) ? MaintenanceDatabase(type) : explicitDatabase;
            string databaseName = "pneuma_test_" + Guid.NewGuid().ToString("N");
            await CreateDatabaseAsync(type, host, port, user, password, databaseName, schema, maintenanceDatabase, token).ConfigureAwait(false);
            settings.DatabaseName = databaseName;

            return settings;
        }

        private static async Task CreateDatabaseAsync(DatabaseTypeEnum type, string host, int port, string user, string password, string databaseName, string? schema, string maintenanceDatabase, CancellationToken token)
        {
            DatabaseSettings maintenance = new DatabaseSettings
            {
                Type = type,
                Hostname = host,
                Port = port,
                Username = user,
                Password = password,
                Schema = String.IsNullOrWhiteSpace(schema) ? null : schema,
                DatabaseName = maintenanceDatabase
            };

            DatabaseDriverBase driver = DatabaseDriverFactory.Create(maintenance);
            try
            {
                await driver.ExecuteQueryAsync("CREATE DATABASE " + databaseName + ";", false, token).ConfigureAwait(false);
            }
            finally
            {
                await driver.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static string MaintenanceDatabase(DatabaseTypeEnum type)
        {
            switch (type)
            {
                case DatabaseTypeEnum.Postgresql: return "postgres";
                case DatabaseTypeEnum.Mysql: return "mysql";
                case DatabaseTypeEnum.SqlServer: return "master";
                default: return String.Empty;
            }
        }

        private static int DefaultPort(DatabaseTypeEnum type)
        {
            switch (type)
            {
                case DatabaseTypeEnum.Postgresql: return 5432;
                case DatabaseTypeEnum.Mysql: return 3306;
                case DatabaseTypeEnum.SqlServer: return 1433;
                default: return 0;
            }
        }

        private static string DefaultUser(DatabaseTypeEnum type)
        {
            switch (type)
            {
                case DatabaseTypeEnum.Postgresql: return "postgres";
                case DatabaseTypeEnum.Mysql: return "root";
                case DatabaseTypeEnum.SqlServer: return "sa";
                default: return String.Empty;
            }
        }

        private static string EnvOr(string name, string fallback)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            return String.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static int IntEnvOr(string name, int fallback)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (!String.IsNullOrWhiteSpace(value) && Int32.TryParse(value, out int parsed)) return parsed;
            return fallback;
        }
    }
}
