namespace Pneuma.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Models;

    /// <summary>PostgreSQL administrator methods.</summary>
    internal class AdministratorMethods : PostgresqlMethodsBase, IAdministratorMethods
    {
        internal AdministratorMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<Administrator> CreateAsync(Administrator administrator, CancellationToken token = default)
        {
            if (administrator == null) throw new ArgumentNullException(nameof(administrator));
            administrator.CreatedUtc = DateTime.UtcNow;
            administrator.LastUpdateUtc = administrator.CreatedUtc;

            string sql =
                "INSERT INTO administrators (id, accountid, firstname, lastname, email, passwordsha256, telephone, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(administrator.Id) + ", " + Sanitizer.Str(administrator.AccountId) + ", " +
                Sanitizer.Str(administrator.FirstName) + ", " + Sanitizer.Str(administrator.LastName) + ", " +
                Sanitizer.Str(administrator.Email) + ", " + Sanitizer.Str(administrator.PasswordSha256) + ", " +
                Sanitizer.Str(administrator.Telephone) + ", " + Sanitizer.Bit(administrator.Active) + ", " +
                Sanitizer.Bit(administrator.IsProtected) + ", " + Sanitizer.Ts(administrator.CreatedUtc) + ", " +
                Sanitizer.Ts(administrator.LastUpdateUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return administrator;
        }

        /// <inheritdoc />
        public async Task<Administrator?> ReadAsync(string id, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM administrators WHERE id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<Administrator?> ReadByEmailAsync(string email, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM administrators WHERE email = " + Sanitizer.Str(email) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<Administrator>> EnumerateAsync(CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM administrators ORDER BY createdutc ASC;", token).ConfigureAwait(false);
            List<Administrator> result = new List<Administrator>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<Administrator> UpdateAsync(Administrator administrator, CancellationToken token = default)
        {
            if (administrator == null) throw new ArgumentNullException(nameof(administrator));
            administrator.LastUpdateUtc = DateTime.UtcNow;

            string sql =
                "UPDATE administrators SET accountid = " + Sanitizer.Str(administrator.AccountId) +
                ", firstname = " + Sanitizer.Str(administrator.FirstName) +
                ", lastname = " + Sanitizer.Str(administrator.LastName) +
                ", email = " + Sanitizer.Str(administrator.Email) +
                ", passwordsha256 = " + Sanitizer.Str(administrator.PasswordSha256) +
                ", telephone = " + Sanitizer.Str(administrator.Telephone) +
                ", active = " + Sanitizer.Bit(administrator.Active) +
                ", isprotected = " + Sanitizer.Bit(administrator.IsProtected) +
                ", lastupdateutc = " + Sanitizer.Ts(administrator.LastUpdateUtc) +
                " WHERE id = " + Sanitizer.Str(administrator.Id) + ";";
            await Query(sql, token).ConfigureAwait(false);
            return administrator;
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            return DeleteIfExistsAsync(
                "SELECT id FROM administrators WHERE id = " + Sanitizer.Str(id) + ";",
                "DELETE FROM administrators WHERE id = " + Sanitizer.Str(id) + ";",
                token);
        }

        internal static Administrator Map(DataRow row)
        {
            return new Administrator
            {
                Id = RowReader.GetString(row, "id"),
                AccountId = RowReader.GetNullableString(row, "accountid"),
                FirstName = RowReader.GetString(row, "firstname"),
                LastName = RowReader.GetString(row, "lastname"),
                Email = RowReader.GetString(row, "email"),
                PasswordSha256 = RowReader.GetString(row, "passwordsha256"),
                Telephone = RowReader.GetNullableString(row, "telephone"),
                Active = RowReader.GetBool(row, "active"),
                IsProtected = RowReader.GetBool(row, "isprotected"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
