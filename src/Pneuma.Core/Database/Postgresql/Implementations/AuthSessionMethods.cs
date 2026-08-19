namespace Pneuma.Core.Database.Postgresql.Implementations
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

    /// <summary>PostgreSQL authentication session methods.</summary>
    internal class AuthSessionMethods : PostgresqlMethodsBase, IAuthSessionMethods
    {
        internal AuthSessionMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<AuthSession> CreateAsync(AuthSession session, CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            session.CreatedUtc = DateTime.UtcNow;
            session.LastUpdateUtc = session.CreatedUtc;

            string sql =
                "INSERT INTO authsessions (id, tenantid, accountid, administratorid, userid, credentialid, principaltype, authscheme, tokenid, sourceip, useragent, expiresutc, lastusedutc, revokedutc, revocationreason, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(session.Id) + ", " + Sanitizer.Str(session.TenantId) + ", " +
                Sanitizer.Str(session.AccountId) + ", " + Sanitizer.Str(session.AdministratorId) + ", " +
                Sanitizer.Str(session.UserId) + ", " + Sanitizer.Str(session.CredentialId) + ", " +
                Sanitizer.Str(session.PrincipalType.ToString()) + ", " + Sanitizer.Str(session.AuthScheme.ToString()) + ", " +
                Sanitizer.Str(session.TokenId) + ", " + Sanitizer.Str(session.SourceIp) + ", " +
                Sanitizer.Str(session.UserAgent) + ", " + Sanitizer.Ts(session.ExpiresUtc) + ", " +
                Sanitizer.Ts(session.LastUsedUtc) + ", " + Sanitizer.Ts(session.RevokedUtc) + ", " +
                Sanitizer.Str(session.RevocationReason) + ", " + Sanitizer.Bit(session.Active) + ", " +
                Sanitizer.Bit(session.IsProtected) + ", " + Sanitizer.Ts(session.CreatedUtc) + ", " +
                Sanitizer.Ts(session.LastUpdateUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return session;
        }

        /// <inheritdoc />
        public async Task<AuthSession?> ReadAsync(string id, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM authsessions WHERE id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<AuthSession>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM authsessions WHERE tenantid = " + Sanitizer.Str(tenantId) + " ORDER BY createdutc ASC;",
                token).ConfigureAwait(false);
            List<AuthSession> result = new List<AuthSession>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<AuthSession> UpdateAsync(AuthSession session, CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            session.LastUpdateUtc = DateTime.UtcNow;

            string sql =
                "UPDATE authsessions SET tenantid = " + Sanitizer.Str(session.TenantId) +
                ", accountid = " + Sanitizer.Str(session.AccountId) +
                ", administratorid = " + Sanitizer.Str(session.AdministratorId) +
                ", userid = " + Sanitizer.Str(session.UserId) +
                ", credentialid = " + Sanitizer.Str(session.CredentialId) +
                ", principaltype = " + Sanitizer.Str(session.PrincipalType.ToString()) +
                ", authscheme = " + Sanitizer.Str(session.AuthScheme.ToString()) +
                ", tokenid = " + Sanitizer.Str(session.TokenId) +
                ", sourceip = " + Sanitizer.Str(session.SourceIp) +
                ", useragent = " + Sanitizer.Str(session.UserAgent) +
                ", expiresutc = " + Sanitizer.Ts(session.ExpiresUtc) +
                ", lastusedutc = " + Sanitizer.Ts(session.LastUsedUtc) +
                ", revokedutc = " + Sanitizer.Ts(session.RevokedUtc) +
                ", revocationreason = " + Sanitizer.Str(session.RevocationReason) +
                ", active = " + Sanitizer.Bit(session.Active) +
                ", isprotected = " + Sanitizer.Bit(session.IsProtected) +
                ", lastupdateutc = " + Sanitizer.Ts(session.LastUpdateUtc) +
                " WHERE id = " + Sanitizer.Str(session.Id) + ";";
            await Query(sql, token).ConfigureAwait(false);
            return session;
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            return DeleteIfExistsAsync(
                "SELECT id FROM authsessions WHERE id = " + Sanitizer.Str(id) + ";",
                "DELETE FROM authsessions WHERE id = " + Sanitizer.Str(id) + ";",
                token);
        }

        /// <inheritdoc />
        public async Task<int> DeleteExpiredAsync(DateTime olderThanUtc, CancellationToken token = default)
        {
            string where = " WHERE expiresutc < " + Sanitizer.Ts(olderThanUtc);
            DataTable count = await Query("SELECT COUNT(*) AS cnt FROM authsessions" + where + ";", token).ConfigureAwait(false);
            int affected = count.Rows.Count == 0 ? 0 : RowReader.GetInt(count.Rows[0], "cnt");
            if (affected == 0) return 0;
            await Query("DELETE FROM authsessions" + where + ";", token).ConfigureAwait(false);
            return affected;
        }

        internal static AuthSession Map(DataRow row)
        {
            return new AuthSession
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetNullableString(row, "tenantid"),
                AccountId = RowReader.GetNullableString(row, "accountid"),
                AdministratorId = RowReader.GetNullableString(row, "administratorid"),
                UserId = RowReader.GetNullableString(row, "userid"),
                CredentialId = RowReader.GetNullableString(row, "credentialid"),
                PrincipalType = RowReader.GetEnum<PrincipalTypeEnum>(row, "principaltype", PrincipalTypeEnum.User),
                AuthScheme = RowReader.GetEnum<AuthSchemeEnum>(row, "authscheme", AuthSchemeEnum.PasswordHeaders),
                TokenId = RowReader.GetString(row, "tokenid"),
                SourceIp = RowReader.GetNullableString(row, "sourceip"),
                UserAgent = RowReader.GetNullableString(row, "useragent"),
                ExpiresUtc = RowReader.GetDateTime(row, "expiresutc"),
                LastUsedUtc = RowReader.GetNullableDateTime(row, "lastusedutc"),
                RevokedUtc = RowReader.GetNullableDateTime(row, "revokedutc"),
                RevocationReason = RowReader.GetNullableString(row, "revocationreason"),
                Active = RowReader.GetBool(row, "active"),
                IsProtected = RowReader.GetBool(row, "isprotected"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
