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

    /// <summary>PostgreSQL role-permission mapping methods.</summary>
    internal class RolePermissionMapMethods : PostgresqlMethodsBase, IRolePermissionMapMethods
    {
        internal RolePermissionMapMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<RolePermissionMap> CreateAsync(RolePermissionMap map, CancellationToken token = default)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            map.CreatedUtc = DateTime.UtcNow;
            map.LastUpdateUtc = map.CreatedUtc;

            await Query(InsertSql(map), token).ConfigureAwait(false);
            return map;
        }

        internal static string InsertSql(RolePermissionMap map)
        {
            return
                "INSERT INTO rolepermissionmaps (id, tenantid, roleid, permissionid, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(map.Id) + ", " + Sanitizer.Str(map.TenantId) + ", " +
                Sanitizer.Str(map.RoleId) + ", " + Sanitizer.Str(map.PermissionId) + ", " +
                Sanitizer.Bit(map.Active) + ", " + Sanitizer.Bit(map.IsProtected) + ", " +
                Sanitizer.Ts(map.CreatedUtc) + ", " + Sanitizer.Ts(map.LastUpdateUtc) + ");";
        }

        /// <inheritdoc />
        public async Task<RolePermissionMap?> ReadAsync(string id, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM rolepermissionmaps WHERE id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<RolePermissionMap>> EnumerateByRoleAsync(string roleId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM rolepermissionmaps WHERE roleid = " + Sanitizer.Str(roleId) + " ORDER BY createdutc ASC;",
                token).ConfigureAwait(false);
            List<RolePermissionMap> result = new List<RolePermissionMap>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            return DeleteIfExistsAsync(
                "SELECT id FROM rolepermissionmaps WHERE id = " + Sanitizer.Str(id) + ";",
                "DELETE FROM rolepermissionmaps WHERE id = " + Sanitizer.Str(id) + ";",
                token);
        }

        internal static RolePermissionMap Map(DataRow row)
        {
            return new RolePermissionMap
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetNullableString(row, "tenantid"),
                RoleId = RowReader.GetString(row, "roleid"),
                PermissionId = RowReader.GetString(row, "permissionid"),
                Active = RowReader.GetBool(row, "active"),
                IsProtected = RowReader.GetBool(row, "isprotected"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
