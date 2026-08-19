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

    /// <summary>PostgreSQL credential scope assignment methods.</summary>
    internal class CredentialScopeAssignmentMethods : PostgresqlMethodsBase, ICredentialScopeAssignmentMethods
    {
        internal CredentialScopeAssignmentMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<CredentialScopeAssignment> CreateAsync(CredentialScopeAssignment assignment, CancellationToken token = default)
        {
            if (assignment == null) throw new ArgumentNullException(nameof(assignment));
            assignment.CreatedUtc = DateTime.UtcNow;
            assignment.LastUpdateUtc = assignment.CreatedUtc;

            string sql =
                "INSERT INTO credentialscopeassignments (id, tenantid, credentialid, roleid, rolename, resourcescope, resourceid, permissions, resourcetypes, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(assignment.Id) + ", " + Sanitizer.Str(assignment.TenantId) + ", " +
                Sanitizer.Str(assignment.CredentialId) + ", " + Sanitizer.Str(assignment.RoleId) + ", " +
                Sanitizer.Str(assignment.RoleName) + ", " + Sanitizer.Str(assignment.ResourceScope.ToString()) + ", " +
                Sanitizer.Str(assignment.ResourceId) + ", " + Sanitizer.Str(JsonColumn.FromEnums(assignment.Permissions)) + ", " +
                Sanitizer.Str(JsonColumn.FromEnums(assignment.ResourceTypes)) + ", " + Sanitizer.Bit(assignment.Active) + ", " +
                Sanitizer.Bit(assignment.IsProtected) + ", " + Sanitizer.Ts(assignment.CreatedUtc) + ", " +
                Sanitizer.Ts(assignment.LastUpdateUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return assignment;
        }

        /// <inheritdoc />
        public async Task<CredentialScopeAssignment?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM credentialscopeassignments WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";",
                token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<CredentialScopeAssignment>> EnumerateByCredentialAsync(string tenantId, string credentialId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM credentialscopeassignments WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND credentialid = " + Sanitizer.Str(credentialId) + " ORDER BY createdutc ASC;",
                token).ConfigureAwait(false);
            List<CredentialScopeAssignment> result = new List<CredentialScopeAssignment>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<CredentialScopeAssignment> UpdateAsync(CredentialScopeAssignment assignment, CancellationToken token = default)
        {
            if (assignment == null) throw new ArgumentNullException(nameof(assignment));
            assignment.LastUpdateUtc = DateTime.UtcNow;

            string sql =
                "UPDATE credentialscopeassignments SET credentialid = " + Sanitizer.Str(assignment.CredentialId) +
                ", roleid = " + Sanitizer.Str(assignment.RoleId) +
                ", rolename = " + Sanitizer.Str(assignment.RoleName) +
                ", resourcescope = " + Sanitizer.Str(assignment.ResourceScope.ToString()) +
                ", resourceid = " + Sanitizer.Str(assignment.ResourceId) +
                ", permissions = " + Sanitizer.Str(JsonColumn.FromEnums(assignment.Permissions)) +
                ", resourcetypes = " + Sanitizer.Str(JsonColumn.FromEnums(assignment.ResourceTypes)) +
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
                "SELECT id FROM credentialscopeassignments WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";",
                "DELETE FROM credentialscopeassignments WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";",
                token);
        }

        internal static CredentialScopeAssignment Map(DataRow row)
        {
            return new CredentialScopeAssignment
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                CredentialId = RowReader.GetString(row, "credentialid"),
                RoleId = RowReader.GetNullableString(row, "roleid"),
                RoleName = RowReader.GetNullableString(row, "rolename"),
                ResourceScope = RowReader.GetEnum<ResourceScopeEnum>(row, "resourcescope", ResourceScopeEnum.Tenant),
                ResourceId = RowReader.GetNullableString(row, "resourceid"),
                Permissions = RowReader.GetEnumList<OperationTypeEnum>(row, "permissions"),
                ResourceTypes = RowReader.GetEnumList<ResourceTypeEnum>(row, "resourcetypes"),
                Active = RowReader.GetBool(row, "active"),
                IsProtected = RowReader.GetBool(row, "isprotected"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
