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

    /// <summary>SQLite user role assignment methods.</summary>
    internal class UserRoleAssignmentMethods : SqliteMethodsBase, IUserRoleAssignmentMethods
    {
        internal UserRoleAssignmentMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<UserRoleAssignment> CreateAsync(UserRoleAssignment assignment, CancellationToken token = default)
        {
            if (assignment == null) throw new ArgumentNullException(nameof(assignment));
            assignment.CreatedUtc = DateTime.UtcNow;
            assignment.LastUpdateUtc = assignment.CreatedUtc;

            string sql =
                "INSERT INTO userroleassignments (id, tenantid, userid, roleid, rolename, resourcescope, resourceid, inheritstochildren, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(assignment.Id) + ", " + Sanitizer.Str(assignment.TenantId) + ", " +
                Sanitizer.Str(assignment.UserId) + ", " + Sanitizer.Str(assignment.RoleId) + ", " +
                Sanitizer.Str(assignment.RoleName) + ", " + Sanitizer.Str(assignment.ResourceScope.ToString()) + ", " +
                Sanitizer.Str(assignment.ResourceId) + ", " + Sanitizer.Bit(assignment.InheritsToChildren) + ", " +
                Sanitizer.Bit(assignment.Active) + ", " + Sanitizer.Bit(assignment.IsProtected) + ", " +
                Sanitizer.Ts(assignment.CreatedUtc) + ", " + Sanitizer.Ts(assignment.LastUpdateUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return assignment;
        }

        /// <inheritdoc />
        public async Task<UserRoleAssignment?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM userroleassignments WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";",
                token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<UserRoleAssignment>> EnumerateByUserAsync(string tenantId, string userId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM userroleassignments WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND userid = " + Sanitizer.Str(userId) + " ORDER BY createdutc ASC;",
                token).ConfigureAwait(false);
            List<UserRoleAssignment> result = new List<UserRoleAssignment>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<List<UserRoleAssignment>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM userroleassignments WHERE tenantid = " + Sanitizer.Str(tenantId) + " ORDER BY createdutc ASC;",
                token).ConfigureAwait(false);
            List<UserRoleAssignment> result = new List<UserRoleAssignment>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<UserRoleAssignment> UpdateAsync(UserRoleAssignment assignment, CancellationToken token = default)
        {
            if (assignment == null) throw new ArgumentNullException(nameof(assignment));
            assignment.LastUpdateUtc = DateTime.UtcNow;

            string sql =
                "UPDATE userroleassignments SET userid = " + Sanitizer.Str(assignment.UserId) +
                ", roleid = " + Sanitizer.Str(assignment.RoleId) +
                ", rolename = " + Sanitizer.Str(assignment.RoleName) +
                ", resourcescope = " + Sanitizer.Str(assignment.ResourceScope.ToString()) +
                ", resourceid = " + Sanitizer.Str(assignment.ResourceId) +
                ", inheritstochildren = " + Sanitizer.Bit(assignment.InheritsToChildren) +
                ", active = " + Sanitizer.Bit(assignment.Active) +
                ", isprotected = " + Sanitizer.Bit(assignment.IsProtected) +
                ", lastupdateutc = " + Sanitizer.Ts(assignment.LastUpdateUtc) +
                " WHERE tenantid = " + Sanitizer.Str(assignment.TenantId) + " AND id = " + Sanitizer.Str(assignment.Id) + ";";
            await Query(sql, token).ConfigureAwait(false);
            return assignment;
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            return DeleteIfExistsAsync(
                "SELECT id FROM userroleassignments WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";",
                "DELETE FROM userroleassignments WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";",
                token);
        }

        internal static UserRoleAssignment Map(DataRow row)
        {
            return new UserRoleAssignment
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                UserId = RowReader.GetString(row, "userid"),
                RoleId = RowReader.GetNullableString(row, "roleid"),
                RoleName = RowReader.GetNullableString(row, "rolename"),
                ResourceScope = RowReader.GetEnum<ResourceScopeEnum>(row, "resourcescope", ResourceScopeEnum.Tenant),
                ResourceId = RowReader.GetNullableString(row, "resourceid"),
                InheritsToChildren = RowReader.GetBool(row, "inheritstochildren"),
                Active = RowReader.GetBool(row, "active"),
                IsProtected = RowReader.GetBool(row, "isprotected"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
