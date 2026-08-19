namespace Pneuma.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;

    /// <summary>SQLite audit record methods.</summary>
    internal class AuditMethods : SqliteMethodsBase, IAuditMethods
    {
        internal AuditMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<AuditRecord> CreateAsync(AuditRecord record, CancellationToken token = default)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            record.CreatedUtc = DateTime.UtcNow;

            string sql =
                "INSERT INTO audit (id, eventtype, tenantid, userid, credentialid, sessionid, resourceid, principaltype, authscheme, requestid, httpmethod, urlpath, sourceip, authenticationresult, authorizationresult, requiredresourcetype, requiredoperation, denialreason, bypassreason, statuscode, createdutc) VALUES (" +
                Sanitizer.Str(record.Id) + ", " + Sanitizer.Str(record.EventType.ToString()) + ", " +
                Sanitizer.Str(record.TenantId) + ", " + Sanitizer.Str(record.UserId) + ", " +
                Sanitizer.Str(record.CredentialId) + ", " + Sanitizer.Str(record.SessionId) + ", " +
                Sanitizer.Str(record.ResourceId) + ", " + Sanitizer.Str(NullableEnum(record.PrincipalType)) + ", " +
                Sanitizer.Str(NullableEnum(record.AuthScheme)) + ", " + Sanitizer.Str(record.RequestId) + ", " +
                Sanitizer.Str(record.HttpMethod) + ", " + Sanitizer.Str(record.UrlPath) + ", " +
                Sanitizer.Str(record.SourceIp) + ", " + Sanitizer.Str(NullableEnum(record.AuthenticationResult)) + ", " +
                Sanitizer.Str(NullableEnum(record.AuthorizationResult)) + ", " + Sanitizer.Str(NullableEnum(record.RequiredResourceType)) + ", " +
                Sanitizer.Str(NullableEnum(record.RequiredOperation)) + ", " + Sanitizer.Str(record.DenialReason) + ", " +
                Sanitizer.Str(record.BypassReason) + ", " + NullableInt(record.StatusCode) + ", " +
                Sanitizer.Ts(record.CreatedUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return record;
        }

        /// <inheritdoc />
        public async Task<AuditRecord?> ReadAsync(string id, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM audit WHERE id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<AuditRecord>> EnumerateAsync(string? tenantId, int maxResults, CancellationToken token = default)
        {
            string sql;
            if (tenantId == null)
            {
                sql = "SELECT * FROM audit ORDER BY createdutc DESC LIMIT " + maxResults.ToString(CultureInfo.InvariantCulture) + ";";
            }
            else
            {
                sql = "SELECT * FROM audit WHERE tenantid = " + Sanitizer.Str(tenantId) + " ORDER BY createdutc DESC LIMIT " + maxResults.ToString(CultureInfo.InvariantCulture) + ";";
            }

            DataTable table = await Query(sql, token).ConfigureAwait(false);
            List<AuditRecord> result = new List<AuditRecord>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken token = default)
        {
            string where = " WHERE createdutc < " + Sanitizer.Ts(olderThanUtc);
            DataTable count = await Query("SELECT COUNT(*) AS cnt FROM audit" + where + ";", token).ConfigureAwait(false);
            int affected = count.Rows.Count == 0 ? 0 : RowReader.GetInt(count.Rows[0], "cnt");
            if (affected == 0) return 0;
            await Query("DELETE FROM audit" + where + ";", token).ConfigureAwait(false);
            return affected;
        }

        private static string? NullableEnum<T>(T? value) where T : struct, Enum
        {
            return value.HasValue ? value.Value.ToString() : null;
        }

        private static string NullableInt(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "NULL";
        }

        internal static AuditRecord Map(DataRow row)
        {
            return new AuditRecord
            {
                Id = RowReader.GetString(row, "id"),
                EventType = RowReader.GetEnum<AuditEventTypeEnum>(row, "eventtype", AuditEventTypeEnum.AuthorizationDenied),
                TenantId = RowReader.GetNullableString(row, "tenantid"),
                UserId = RowReader.GetNullableString(row, "userid"),
                CredentialId = RowReader.GetNullableString(row, "credentialid"),
                SessionId = RowReader.GetNullableString(row, "sessionid"),
                ResourceId = RowReader.GetNullableString(row, "resourceid"),
                PrincipalType = RowReader.GetNullableEnum<PrincipalTypeEnum>(row, "principaltype"),
                AuthScheme = RowReader.GetNullableEnum<AuthSchemeEnum>(row, "authscheme"),
                RequestId = RowReader.GetNullableString(row, "requestid"),
                HttpMethod = RowReader.GetNullableString(row, "httpmethod"),
                UrlPath = RowReader.GetNullableString(row, "urlpath"),
                SourceIp = RowReader.GetNullableString(row, "sourceip"),
                AuthenticationResult = RowReader.GetNullableEnum<AuthenticationResultEnum>(row, "authenticationresult"),
                AuthorizationResult = RowReader.GetNullableEnum<AuthorizationResultEnum>(row, "authorizationresult"),
                RequiredResourceType = RowReader.GetNullableEnum<ResourceTypeEnum>(row, "requiredresourcetype"),
                RequiredOperation = RowReader.GetNullableEnum<OperationTypeEnum>(row, "requiredoperation"),
                DenialReason = RowReader.GetNullableString(row, "denialreason"),
                BypassReason = RowReader.GetNullableString(row, "bypassreason"),
                StatusCode = (row["statuscode"] == null || row["statuscode"] == DBNull.Value) ? (int?)null : RowReader.GetInt(row, "statuscode"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
