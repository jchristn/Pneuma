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

    /// <summary>PostgreSQL user methods.</summary>
    internal class UserMethods : PostgresqlMethodsBase, IUserMethods
    {
        internal UserMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<User> CreateAsync(User user, CancellationToken token = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            user.CreatedUtc = DateTime.UtcNow;
            user.LastUpdateUtc = user.CreatedUtc;

            string sql =
                "INSERT INTO users (id, tenantid, firstname, lastname, email, passwordsha256, isadmin, istenantadmin, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(user.Id) + ", " + Sanitizer.Str(user.TenantId) + ", " +
                Sanitizer.Str(user.FirstName) + ", " + Sanitizer.Str(user.LastName) + ", " +
                Sanitizer.Str(user.Email) + ", " + Sanitizer.Str(user.PasswordSha256) + ", " +
                Sanitizer.Bit(user.IsAdmin) + ", " + Sanitizer.Bit(user.IsTenantAdmin) + ", " +
                Sanitizer.Bit(user.Active) + ", " + Sanitizer.Bit(user.IsProtected) + ", " +
                Sanitizer.Ts(user.CreatedUtc) + ", " + Sanitizer.Ts(user.LastUpdateUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return user;
        }

        /// <inheritdoc />
        public async Task<User?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM users WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";",
                token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<User?> ReadByIdAsync(string id, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM users WHERE id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<User?> ReadByEmailAsync(string tenantId, string email, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM users WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND email = " + Sanitizer.Str(email) + ";",
                token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<User>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM users WHERE tenantid = " + Sanitizer.Str(tenantId) + " ORDER BY createdutc ASC;",
                token).ConfigureAwait(false);
            List<User> result = new List<User>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<User> UpdateAsync(User user, CancellationToken token = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            user.LastUpdateUtc = DateTime.UtcNow;

            string sql =
                "UPDATE users SET firstname = " + Sanitizer.Str(user.FirstName) +
                ", lastname = " + Sanitizer.Str(user.LastName) +
                ", email = " + Sanitizer.Str(user.Email) +
                ", passwordsha256 = " + Sanitizer.Str(user.PasswordSha256) +
                ", isadmin = " + Sanitizer.Bit(user.IsAdmin) +
                ", istenantadmin = " + Sanitizer.Bit(user.IsTenantAdmin) +
                ", active = " + Sanitizer.Bit(user.Active) +
                ", isprotected = " + Sanitizer.Bit(user.IsProtected) +
                ", lastupdateutc = " + Sanitizer.Ts(user.LastUpdateUtc) +
                " WHERE tenantid = " + Sanitizer.Str(user.TenantId) + " AND id = " + Sanitizer.Str(user.Id) + ";";
            await Query(sql, token).ConfigureAwait(false);
            return user;
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            return DeleteIfExistsAsync(
                "SELECT id FROM users WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";",
                "DELETE FROM users WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";",
                token);
        }

        internal static User Map(DataRow row)
        {
            return new User
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                FirstName = RowReader.GetString(row, "firstname"),
                LastName = RowReader.GetString(row, "lastname"),
                Email = RowReader.GetString(row, "email"),
                PasswordSha256 = RowReader.GetString(row, "passwordsha256"),
                IsAdmin = RowReader.GetBool(row, "isadmin"),
                IsTenantAdmin = RowReader.GetBool(row, "istenantadmin"),
                Active = RowReader.GetBool(row, "active"),
                IsProtected = RowReader.GetBool(row, "isprotected"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
