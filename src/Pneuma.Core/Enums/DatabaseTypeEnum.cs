namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Supported database providers.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DatabaseTypeEnum
    {
        /// <summary>SQLite (default for local development and tests).</summary>
        Sqlite,
        /// <summary>MySQL.</summary>
        Mysql,
        /// <summary>PostgreSQL (default deployed provider).</summary>
        Postgresql,
        /// <summary>Microsoft SQL Server.</summary>
        SqlServer
    }
}
