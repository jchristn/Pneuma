namespace Pneuma.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;

    /// <summary>SQLite credential methods.</summary>
    internal class CredentialMethods : SqliteMethodsBase, ICredentialMethods
    {
        internal CredentialMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<Credential> CreateAsync(Credential credential, CancellationToken token = default)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            credential.CreatedUtc = DateTime.UtcNow;
            credential.LastUpdateUtc = credential.CreatedUtc;

            string sql =
                "INSERT INTO credentials (id, tenantid, userid, name, accesskey, secretkeyencrypted, secretkeylast4, authmode, lastusedutc, expiresutc, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(credential.Id) + ", " + Sanitizer.Str(credential.TenantId) + ", " +
                Sanitizer.Str(credential.UserId) + ", " + Sanitizer.Str(credential.Name) + ", " +
                Sanitizer.Str(credential.AccessKey) + ", " + Sanitizer.Str(credential.SecretKeyEncrypted) + ", " +
                Sanitizer.Str(credential.SecretKeyLast4) + ", " + Sanitizer.Str(credential.AuthMode.ToString()) + ", " +
                Sanitizer.Ts(credential.LastUsedUtc) + ", " + Sanitizer.Ts(credential.ExpiresUtc) + ", " +
                Sanitizer.Bit(credential.Active) + ", " + Sanitizer.Bit(credential.IsProtected) + ", " +
                Sanitizer.Ts(credential.CreatedUtc) + ", " + Sanitizer.Ts(credential.LastUpdateUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return credential;
        }

        /// <inheritdoc />
        public async Task<Credential?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM credentials WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";",
                token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<Credential?> ReadByAccessKeyAsync(string accessKey, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM credentials WHERE accesskey = " + Sanitizer.Str(accessKey) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<Credential>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM credentials WHERE tenantid = " + Sanitizer.Str(tenantId) + " ORDER BY createdutc ASC;",
                token).ConfigureAwait(false);
            List<Credential> result = new List<Credential>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<List<Credential>> EnumerateByUserAsync(string tenantId, string userId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM credentials WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND userid = " + Sanitizer.Str(userId) + " ORDER BY createdutc ASC;",
                token).ConfigureAwait(false);
            List<Credential> result = new List<Credential>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<Credential> UpdateAsync(Credential credential, CancellationToken token = default)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            credential.LastUpdateUtc = DateTime.UtcNow;

            string sql =
                "UPDATE credentials SET name = " + Sanitizer.Str(credential.Name) +
                ", accesskey = " + Sanitizer.Str(credential.AccessKey) +
                ", secretkeyencrypted = " + Sanitizer.Str(credential.SecretKeyEncrypted) +
                ", secretkeylast4 = " + Sanitizer.Str(credential.SecretKeyLast4) +
                ", authmode = " + Sanitizer.Str(credential.AuthMode.ToString()) +
                ", lastusedutc = " + Sanitizer.Ts(credential.LastUsedUtc) +
                ", expiresutc = " + Sanitizer.Ts(credential.ExpiresUtc) +
                ", active = " + Sanitizer.Bit(credential.Active) +
                ", isprotected = " + Sanitizer.Bit(credential.IsProtected) +
                ", lastupdateutc = " + Sanitizer.Ts(credential.LastUpdateUtc) +
                " WHERE tenantid = " + Sanitizer.Str(credential.TenantId) + " AND id = " + Sanitizer.Str(credential.Id) + ";";
            await Query(sql, token).ConfigureAwait(false);
            return credential;
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            return DeleteIfExistsAsync(
                "SELECT id FROM credentials WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";",
                "DELETE FROM credentials WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";",
                token);
        }

        internal static Credential Map(DataRow row)
        {
            return new Credential
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                UserId = RowReader.GetString(row, "userid"),
                Name = RowReader.GetString(row, "name"),
                AccessKey = RowReader.GetString(row, "accesskey"),
                SecretKeyEncrypted = RowReader.GetString(row, "secretkeyencrypted"),
                SecretKeyLast4 = RowReader.GetString(row, "secretkeylast4"),
                AuthMode = RowReader.GetEnum<CredentialAuthModeEnum>(row, "authmode", CredentialAuthModeEnum.DirectHeader),
                LastUsedUtc = RowReader.GetNullableDateTime(row, "lastusedutc"),
                ExpiresUtc = RowReader.GetNullableDateTime(row, "expiresutc"),
                Active = RowReader.GetBool(row, "active"),
                IsProtected = RowReader.GetBool(row, "isprotected"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
