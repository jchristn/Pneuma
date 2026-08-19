namespace Pneuma.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Models;

    /// <summary>SQL Server tenant methods.</summary>
    internal class TenantMethods : SqlServerMethodsBase, ITenantMethods
    {
        internal TenantMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<Tenant> CreateAsync(Tenant tenant, CancellationToken token = default)
        {
            if (tenant == null) throw new ArgumentNullException(nameof(tenant));
            tenant.CreatedUtc = DateTime.UtcNow;
            tenant.LastUpdateUtc = tenant.CreatedUtc;

            string sql =
                "INSERT INTO tenants (id, accountid, parentid, name, region, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(tenant.Id) + ", " + Sanitizer.Str(tenant.AccountId) + ", " + Sanitizer.Str(tenant.ParentId) + ", " +
                Sanitizer.Str(tenant.Name) + ", " + Sanitizer.Str(tenant.Region) + ", " + Sanitizer.Bit(tenant.Active) + ", " +
                Sanitizer.Bit(tenant.IsProtected) + ", " + Sanitizer.Ts(tenant.CreatedUtc) + ", " + Sanitizer.Ts(tenant.LastUpdateUtc) + ");";
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
                ", active = " + Sanitizer.Bit(tenant.Active) +
                ", isprotected = " + Sanitizer.Bit(tenant.IsProtected) +
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

        internal static Tenant Map(DataRow row)
        {
            return new Tenant
            {
                Id = RowReader.GetString(row, "id"),
                AccountId = RowReader.GetNullableString(row, "accountid"),
                ParentId = RowReader.GetNullableString(row, "parentid"),
                Name = RowReader.GetString(row, "name"),
                Region = RowReader.GetNullableString(row, "region"),
                Active = RowReader.GetBool(row, "active"),
                IsProtected = RowReader.GetBool(row, "isprotected"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
