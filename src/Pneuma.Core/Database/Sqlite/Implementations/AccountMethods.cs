namespace Pneuma.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Models;

    /// <summary>SQLite account methods.</summary>
    internal class AccountMethods : SqliteMethodsBase, IAccountMethods
    {
        internal AccountMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<Account> CreateAsync(Account account, CancellationToken token = default)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            account.CreatedUtc = DateTime.UtcNow;
            account.LastUpdateUtc = account.CreatedUtc;

            string sql =
                "INSERT INTO accounts (id, name, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(account.Id) + ", " + Sanitizer.Str(account.Name) + ", " +
                Sanitizer.Bit(account.Active) + ", " + Sanitizer.Bit(account.IsProtected) + ", " +
                Sanitizer.Ts(account.CreatedUtc) + ", " + Sanitizer.Ts(account.LastUpdateUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return account;
        }

        /// <inheritdoc />
        public async Task<Account?> ReadAsync(string id, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM accounts WHERE id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<Account>> EnumerateAsync(CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM accounts ORDER BY createdutc ASC;", token).ConfigureAwait(false);
            List<Account> result = new List<Account>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<Account> UpdateAsync(Account account, CancellationToken token = default)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            account.LastUpdateUtc = DateTime.UtcNow;

            string sql =
                "UPDATE accounts SET name = " + Sanitizer.Str(account.Name) +
                ", active = " + Sanitizer.Bit(account.Active) +
                ", isprotected = " + Sanitizer.Bit(account.IsProtected) +
                ", lastupdateutc = " + Sanitizer.Ts(account.LastUpdateUtc) +
                " WHERE id = " + Sanitizer.Str(account.Id) + ";";
            await Query(sql, token).ConfigureAwait(false);
            return account;
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            return DeleteIfExistsAsync(
                "SELECT id FROM accounts WHERE id = " + Sanitizer.Str(id) + ";",
                "DELETE FROM accounts WHERE id = " + Sanitizer.Str(id) + ";",
                token);
        }

        internal static Account Map(DataRow row)
        {
            return new Account
            {
                Id = RowReader.GetString(row, "id"),
                Name = RowReader.GetString(row, "name"),
                Active = RowReader.GetBool(row, "active"),
                IsProtected = RowReader.GetBool(row, "isprotected"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
