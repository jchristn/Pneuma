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

    /// <summary>PostgreSQL legacy user-role map methods.</summary>
    internal class UserRoleMapMethods : PostgresqlMethodsBase, IUserRoleMapMethods
    {
        internal UserRoleMapMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<UserRoleMap> CreateAsync(UserRoleMap map, CancellationToken token = default)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            map.CreatedUtc = DateTime.UtcNow;
            map.LastUpdateUtc = map.CreatedUtc;

            string sql =
                "INSERT INTO userrolemaps (id, tenantid, userid, roleid, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(map.Id) + ", " + Sanitizer.Str(map.TenantId) + ", " +
                Sanitizer.Str(map.UserId) + ", " + Sanitizer.Str(map.RoleId) + ", " +
                Sanitizer.Bit(map.Active) + ", " + Sanitizer.Bit(map.IsProtected) + ", " +
                Sanitizer.Ts(map.CreatedUtc) + ", " + Sanitizer.Ts(map.LastUpdateUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return map;
        }

        /// <inheritdoc />
        public async Task<List<UserRoleMap>> EnumerateByUserAsync(string tenantId, string userId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM userrolemaps WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND userid = " + Sanitizer.Str(userId) + " ORDER BY createdutc ASC;",
                token).ConfigureAwait(false);
            List<UserRoleMap> result = new List<UserRoleMap>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            return DeleteIfExistsAsync(
                "SELECT id FROM userrolemaps WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";",
                "DELETE FROM userrolemaps WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";",
                token);
        }

        internal static UserRoleMap Map(DataRow row)
        {
            return new UserRoleMap
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                UserId = RowReader.GetString(row, "userid"),
                RoleId = RowReader.GetString(row, "roleid"),
                Active = RowReader.GetBool(row, "active"),
                IsProtected = RowReader.GetBool(row, "isprotected"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
