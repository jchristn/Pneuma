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
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ingestion.Models;

    /// <summary>SQLite tenant methods.</summary>
    internal class TenantMethods : SqliteMethodsBase, ITenantMethods
    {
        internal TenantMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<Tenant> CreateAsync(Tenant tenant, CancellationToken token = default)
        {
            if (tenant == null) throw new ArgumentNullException(nameof(tenant));
            tenant.CreatedUtc = DateTime.UtcNow;
            tenant.LastUpdateUtc = tenant.CreatedUtc;

            string sql =
                "INSERT INTO tenants (id, accountid, parentid, name, region, litegraphtenantguid, litegraphgraphguid, active, isprotected, deletionstatus, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(tenant.Id) + ", " + Sanitizer.Str(tenant.AccountId) + ", " + Sanitizer.Str(tenant.ParentId) + ", " +
                Sanitizer.Str(tenant.Name) + ", " + Sanitizer.Str(tenant.Region) + ", " + Sanitizer.Str(tenant.LiteGraphTenantGuid) + ", " + Sanitizer.Str(tenant.LiteGraphGraphGuid) + ", " + Sanitizer.Bit(tenant.Active) + ", " +
                Sanitizer.Bit(tenant.IsProtected) + ", " + Sanitizer.Str(tenant.DeletionStatus.ToString()) + ", " + Sanitizer.Ts(tenant.CreatedUtc) + ", " + Sanitizer.Ts(tenant.LastUpdateUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return tenant;
        }

        /// <inheritdoc />
        public async Task<Tenant?> ReadAsync(string id, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM tenants WHERE id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<Tenant>> EnumerateAsync(CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM tenants ORDER BY createdutc ASC;", token).ConfigureAwait(false);
            List<Tenant> result = new List<Tenant>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<Tenant> UpdateAsync(Tenant tenant, CancellationToken token = default)
        {
            if (tenant == null) throw new ArgumentNullException(nameof(tenant));
            tenant.LastUpdateUtc = DateTime.UtcNow;

            string sql =
                "UPDATE tenants SET accountid = " + Sanitizer.Str(tenant.AccountId) +
                ", parentid = " + Sanitizer.Str(tenant.ParentId) +
                ", name = " + Sanitizer.Str(tenant.Name) +
                ", region = " + Sanitizer.Str(tenant.Region) +
                ", litegraphtenantguid = " + Sanitizer.Str(tenant.LiteGraphTenantGuid) +
                ", litegraphgraphguid = " + Sanitizer.Str(tenant.LiteGraphGraphGuid) +
                ", active = " + Sanitizer.Bit(tenant.Active) +
                ", isprotected = " + Sanitizer.Bit(tenant.IsProtected) +
                ", deletionstatus = " + Sanitizer.Str(tenant.DeletionStatus.ToString()) +
                ", lastupdateutc = " + Sanitizer.Ts(tenant.LastUpdateUtc) +
                " WHERE id = " + Sanitizer.Str(tenant.Id) + ";";
            await Query(sql, token).ConfigureAwait(false);
            return tenant;
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            return DeleteIfExistsAsync(
                "SELECT id FROM tenants WHERE id = " + Sanitizer.Str(id) + ";",
                "DELETE FROM tenants WHERE id = " + Sanitizer.Str(id) + ";",
                token);
        }

        /// <inheritdoc />
        public async Task<List<Tenant>> EnumeratePendingDeletionAsync(CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM tenants WHERE deletionstatus IN ('Pending', 'Deleting') ORDER BY createdutc ASC;",
                token).ConfigureAwait(false);
            List<Tenant> result = new List<Tenant>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteWithTenantDataAsync(string tenantId, CancellationToken token = default)
        {
            DataTable existing = await Query("SELECT id FROM tenants WHERE id = " + Sanitizer.Str(tenantId) + ";", token).ConfigureAwait(false);

            string t = Sanitizer.Str(tenantId);
            List<string> statements = new List<string>
            {
                "DELETE FROM ingestionjobevents WHERE tenantid = " + t + ";",
                "DELETE FROM ingestionjobs WHERE tenantid = " + t + ";",
                "DELETE FROM subjectlinks WHERE tenantid = " + t + ";",
                "DELETE FROM evalresults WHERE tenantid = " + t + ";",
                "DELETE FROM evalruns WHERE tenantid = " + t + ";",
                "DELETE FROM evalfacts WHERE tenantid = " + t + ";",
                "DELETE FROM chattoolcalls WHERE tenantid = " + t + ";",
                "DELETE FROM chatturnperfevents WHERE tenantid = " + t + ";",
                "DELETE FROM chatfeedback WHERE tenantid = " + t + ";",
                "DELETE FROM chatturns WHERE tenantid = " + t + ";",
                "DELETE FROM chatthreads WHERE tenantid = " + t + ";",
                "DELETE FROM subjectprompts WHERE tenantid = " + t + ";",
                "DELETE FROM subjects WHERE tenantid = " + t + ";",
                "DELETE FROM modelrunners WHERE tenantid = " + t + ";",
                "DELETE FROM prompts WHERE tenantid = " + t + ";",
                "DELETE FROM credentialscopeassignments WHERE tenantid = " + t + ";",
                "DELETE FROM userroleassignments WHERE tenantid = " + t + ";",
                "DELETE FROM userrolemaps WHERE tenantid = " + t + ";",
                "DELETE FROM rolepermissionmaps WHERE tenantid = " + t + ";",
                "DELETE FROM permissions WHERE tenantid = " + t + ";",
                "DELETE FROM userroles WHERE tenantid = " + t + ";",
                "DELETE FROM authsessions WHERE tenantid = " + t + ";",
                "DELETE FROM credentials WHERE tenantid = " + t + ";",
                "DELETE FROM users WHERE tenantid = " + t + ";",
                "DELETE FROM requesthistory WHERE tenantid = " + t + ";",
                "DELETE FROM audit WHERE tenantid = " + t + ";",
                "DELETE FROM tenants WHERE id = " + t + ";"
            };

            await QueryTransaction(statements, token).ConfigureAwait(false);
            return existing.Rows.Count > 0;
        }

        internal static Tenant Map(DataRow row)
        {
            return new Tenant
            {
                Id = RowReader.GetString(row, "id"),
                AccountId = RowReader.GetNullableString(row, "accountid"),
                ParentId = RowReader.GetNullableString(row, "parentid"),
                Name = RowReader.GetString(row, "name"),
                Region = RowReader.GetNullableString(row, "region"),
                LiteGraphTenantGuid = RowReader.GetNullableString(row, "litegraphtenantguid"),
                LiteGraphGraphGuid = RowReader.GetNullableString(row, "litegraphgraphguid"),
                Active = RowReader.GetBool(row, "active"),
                IsProtected = RowReader.GetBool(row, "isprotected"),
                DeletionStatus = RowReader.GetEnum<TenantDeletionStatusEnum>(row, "deletionstatus", TenantDeletionStatusEnum.None),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
