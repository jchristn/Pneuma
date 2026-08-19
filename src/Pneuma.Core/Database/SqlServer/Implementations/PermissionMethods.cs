namespace Pneuma.Core.Database.SqlServer.Implementations
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

    /// <summary>SQL Server permission methods.</summary>
    internal class PermissionMethods : SqlServerMethodsBase, IPermissionMethods
    {
        internal PermissionMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<Permission> CreateAsync(Permission permission, CancellationToken token = default)
        {
            if (permission == null) throw new ArgumentNullException(nameof(permission));
            permission.CreatedUtc = DateTime.UtcNow;
            permission.LastUpdateUtc = permission.CreatedUtc;

            await Query(InsertSql(permission), token).ConfigureAwait(false);
            return permission;
        }

        internal static string InsertSql(Permission permission)
        {
            return
                "INSERT INTO permissions (id, tenantid, name, resourcetypes, operationtypes, permissiontype, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(permission.Id) + ", " + Sanitizer.Str(permission.TenantId) + ", " +
                Sanitizer.Str(permission.Name) + ", " + Sanitizer.Str(JsonColumn.FromEnums(permission.ResourceTypes)) + ", " +
                Sanitizer.Str(JsonColumn.FromEnums(permission.OperationTypes)) + ", " + Sanitizer.Str(permission.PermissionType.ToString()) + ", " +
                Sanitizer.Bit(permission.Active) + ", " + Sanitizer.Bit(permission.IsProtected) + ", " +
                Sanitizer.Ts(permission.CreatedUtc) + ", " + Sanitizer.Ts(permission.LastUpdateUtc) + ");";
        }

        /// <inheritdoc />
        public async Task<Permission?> ReadAsync(string id, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM permissions WHERE id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<Permission>> EnumerateAsync(string? tenantId, CancellationToken token = default)
        {
            string sql;
            if (tenantId == null)
            {
                sql = "SELECT * FROM permissions WHERE tenantid IS NULL ORDER BY createdutc ASC;";
            }
            else
            {
                sql = "SELECT * FROM permissions WHERE (tenantid IS NULL OR tenantid = " + Sanitizer.Str(tenantId) + ") ORDER BY createdutc ASC;";
            }

            DataTable table = await Query(sql, token).ConfigureAwait(false);
            List<Permission> result = new List<Permission>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<List<Permission>> EnumerateByRoleAsync(string roleId, CancellationToken token = default)
        {
            string sql =
                "SELECT p.* FROM permissions p INNER JOIN rolepermissionmaps rpm ON p.id = rpm.permissionid " +
                "WHERE rpm.roleid = " + Sanitizer.Str(roleId) + " AND rpm.active = 1 AND p.active = 1;";
            DataTable table = await Query(sql, token).ConfigureAwait(false);
            List<Permission> result = new List<Permission>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<Permission> UpdateAsync(Permission permission, CancellationToken token = default)
        {
            if (permission == null) throw new ArgumentNullException(nameof(permission));
            permission.LastUpdateUtc = DateTime.UtcNow;

            string sql =
                "UPDATE permissions SET tenantid = " + Sanitizer.Str(permission.TenantId) +
                ", name = " + Sanitizer.Str(permission.Name) +
                ", resourcetypes = " + Sanitizer.Str(JsonColumn.FromEnums(permission.ResourceTypes)) +
                ", operationtypes = " + Sanitizer.Str(JsonColumn.FromEnums(permission.OperationTypes)) +
                ", permissiontype = " + Sanitizer.Str(permission.PermissionType.ToString()) +
                ", active = " + Sanitizer.Bit(permission.Active) +
                ", isprotected = " + Sanitizer.Bit(permission.IsProtected) +
                ", lastupdateutc = " + Sanitizer.Ts(permission.LastUpdateUtc) +
                " WHERE id = " + Sanitizer.Str(permission.Id) + ";";
            await Query(sql, token).ConfigureAwait(false);
            return permission;
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            return DeleteIfExistsAsync(
                "SELECT id FROM permissions WHERE id = " + Sanitizer.Str(id) + ";",
                "DELETE FROM permissions WHERE id = " + Sanitizer.Str(id) + ";",
                token);
        }

        internal static Permission Map(DataRow row)
        {
            return new Permission
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetNullableString(row, "tenantid"),
                Name = RowReader.GetString(row, "name"),
                ResourceTypes = RowReader.GetEnumList<ResourceTypeEnum>(row, "resourcetypes"),
                OperationTypes = RowReader.GetEnumList<OperationTypeEnum>(row, "operationtypes"),
                PermissionType = RowReader.GetEnum<PermissionTypeEnum>(row, "permissiontype", PermissionTypeEnum.Permit),
                Active = RowReader.GetBool(row, "active"),
                IsProtected = RowReader.GetBool(row, "isprotected"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
