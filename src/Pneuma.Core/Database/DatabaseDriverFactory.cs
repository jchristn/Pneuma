namespace Pneuma.Core.Database
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database.Mysql;
    using Pneuma.Core.Database.Postgresql;
    using Pneuma.Core.Database.Sqlite;
    using Pneuma.Core.Database.SqlServer;
    using Pneuma.Core.Enums;

    /// <summary>
    /// Composition root for the data layer. Produces the driver for the configured provider.
    /// </summary>
    public static class DatabaseDriverFactory
    {
        /// <summary>
        /// Create a driver for the configured provider.
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <returns>Database driver.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the database type is unknown.</exception>
        public static DatabaseDriverBase Create(DatabaseSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            switch (settings.Type)
            {
                case DatabaseTypeEnum.Sqlite:
                    return new SqliteDatabaseDriver(settings);
                case DatabaseTypeEnum.Mysql:
                    return new MysqlDatabaseDriver(settings);
                case DatabaseTypeEnum.Postgresql:
                    return new PostgresqlDatabaseDriver(settings);
                case DatabaseTypeEnum.SqlServer:
                    return new SqlServerDatabaseDriver(settings);
                default:
                    throw new ArgumentException("Unknown database type: " + settings.Type.ToString());
            }
        }

        /// <summary>
        /// Create and initialize a driver.
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Initialized database driver.</returns>
        public static async Task<DatabaseDriverBase> CreateAndInitializeAsync(DatabaseSettings settings, CancellationToken token = default)
        {
            DatabaseDriverBase driver = Create(settings);
            await driver.InitializeAsync(token).ConfigureAwait(false);
            return driver;
        }
    }
}
