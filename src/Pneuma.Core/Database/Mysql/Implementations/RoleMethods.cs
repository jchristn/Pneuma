namespace Pneuma.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Models;

    /// <summary>MySQL role methods.</summary>
    internal class RoleMethods : MysqlMethodsBase, IRoleMethods
    {
        internal RoleMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<UserRole> CreateAsync(UserRole role, CancellationToken token = default)
        {
            if (role == null) throw new ArgumentNullException(nameof(role));
            role.CreatedUtc = DateTime.UtcNow;
            role.LastUpdateUtc = role.CreatedUtc;

            await Query(InsertSql(role), token).ConfigureAwait(false);
            return role;
        }

        /// <inheritdoc />
        public async Task<UserRole> CreateWithPermissionsAsync(UserRole role, IReadOnlyList<Permission> permissions, IReadOnlyList<RolePermissionMap> maps, CancellationToken token = default)
        {
            if (role == null) throw new ArgumentNullException(nameof(role));
            if (permissions == null) throw new ArgumentNullException(nameof(permissions));
            if (maps == null) throw new ArgumentNullException(nameof(maps));

            DateTime now = DateTime.UtcNow;
            role.CreatedUtc = now;
            role.LastUpdateUtc = now;

            List<string> statements = new List<string> { InsertSql(role) };
            foreach (Permission permission in permissions)
            {
                permission.CreatedUtc = now;
                permission.LastUpdateUtc = now;
                statements.Add(PermissionMethods.InsertSql(permission));
            }
            foreach (RolePermissionMap map in maps)
            {
                map.CreatedUtc = now;
                map.LastUpdateUtc = now;
                statements.Add(RolePermissionMapMethods.InsertSql(map));
            }

            await QueryTransaction(statements, token).ConfigureAwait(false);
            return role;
        }

        internal static string InsertSql(UserRole role)
        {
            return
                "INSERT INTO userroles (id, tenantid, name, isbuiltin, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(role.Id) + ", " + Sanitizer.Str(role.TenantId) + ", " +
                Sanitizer.Str(role.Name) + ", " + Sanitizer.Bit(role.IsBuiltIn) + ", " +
                Sanitizer.Bit(role.Active) + ", " + Sanitizer.Bit(role.IsProtected) + ", " +
                Sanitizer.Ts(role.CreatedUtc) + ", " + Sanitizer.Ts(role.LastUpdateUtc) + ");";
        }

        /// <inheritdoc />
        public async Task<UserRole?> ReadAsync(string id, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM userroles WHERE id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<UserRole?> ReadByNameAsync(string? tenantId, string name, CancellationToken token = default)
        {
            if (tenantId != null)
            {
                DataTable scoped = await Query(
                    "SELECT * FROM userroles WHERE name = " + Sanitizer.Str(name) + " AND tenantid = " + Sanitizer.Str(tenantId) + " LIMIT 1;",
                    token).ConfigureAwait(false);
                if (scoped.Rows.Count > 0) return Map(scoped.Rows[0]);
            }

            DataTable global = await Query(
                "SELECT * FROM userroles WHERE name = " + Sanitizer.Str(name) + " AND tenantid IS NULL LIMIT 1;",
                token).ConfigureAwait(false);
            if (global.Rows.Count > 0) return Map(global.Rows[0]);
            return null;
        }

        /// <inheritdoc />
        public async Task<List<UserRole>> EnumerateAsync(string? tenantId, CancellationToken token = default)
        {
            string sql;
            if (tenantId == null)
            {
                sql = "SELECT * FROM userroles WHERE tenantid IS NULL ORDER BY createdutc ASC;";
            }
            else
            {
                sql = "SELECT * FROM userroles WHERE (tenantid IS NULL OR tenantid = " + Sanitizer.Str(tenantId) + ") ORDER BY createdutc ASC;";
            }

            DataTable table = await Query(sql, token).ConfigureAwait(false);
            List<UserRole> result = new List<UserRole>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<UserRole> UpdateAsync(UserRole role, CancellationToken token = default)
        {
            if (role == null) throw new ArgumentNullException(nameof(role));
            role.LastUpdateUtc = DateTime.UtcNow;

            string sql =
                "UPDATE userroles SET tenantid = " + Sanitizer.Str(role.TenantId) +
                ", name = " + Sanitizer.Str(role.Name) +
                ", isbuiltin = " + Sanitizer.Bit(role.IsBuiltIn) +
                ", active = " + Sanitizer.Bit(role.Active) +
                ", isprotected = " + Sanitizer.Bit(role.IsProtected) +
                ", lastupdateutc = " + Sanitizer.Ts(role.LastUpdateUtc) +
                " WHERE id = " + Sanitizer.Str(role.Id) + ";";
            await Query(sql, token).ConfigureAwait(false);
            return role;
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            return DeleteIfExistsAsync(
                "SELECT id FROM userroles WHERE id = " + Sanitizer.Str(id) + ";",
                "DELETE FROM userroles WHERE id = " + Sanitizer.Str(id) + ";",
                token);
        }

        internal static UserRole Map(DataRow row)
        {
            return new UserRole
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetNullableString(row, "tenantid"),
                Name = RowReader.GetString(row, "name"),
                IsBuiltIn = RowReader.GetBool(row, "isbuiltin"),
                Active = RowReader.GetBool(row, "active"),
                IsProtected = RowReader.GetBool(row, "isprotected"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
