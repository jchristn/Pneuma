namespace Pneuma.Core.Database.Mysql.Implementations
{
    using System;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;

    /// <summary>
    /// Shared base for MySQL method implementations. Holds the driver reference and query helpers.
    /// </summary>
    internal abstract class MysqlMethodsBase
    {
        #region Private-Members

        /// <summary>The owning driver used to execute queries.</summary>
        protected readonly DatabaseDriverBase _Db;

        #endregion

        #region Constructors-and-Factories

        protected MysqlMethodsBase(DatabaseDriverBase db)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            _Db = db;
        }

        #endregion

        #region Protected-Methods

        /// <summary>Execute a query and return the resulting table.</summary>
        protected Task<DataTable> Query(string sql, CancellationToken token)
        {
            return _Db.ExecuteQueryAsync(sql, false, token);
        }

        /// <summary>Execute an ordered batch of statements inside a single transaction; all commit or all roll back.</summary>
        protected Task<DataTable> QueryTransaction(System.Collections.Generic.IEnumerable<string> statements, CancellationToken token)
        {
            return _Db.ExecuteQueriesAsync(statements, true, token);
        }

        /// <summary>Execute a delete after confirming a matching row exists.</summary>
        protected async Task<bool> DeleteIfExistsAsync(string existsSql, string deleteSql, CancellationToken token)
        {
            DataTable existing = await Query(existsSql, token).ConfigureAwait(false);
            if (existing.Rows.Count == 0) return false;
            await Query(deleteSql, token).ConfigureAwait(false);
            return true;
        }

        #endregion
    }
}
