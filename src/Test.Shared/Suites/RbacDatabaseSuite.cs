namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Provider-agnostic contract suite for the RBAC repositories: permissions, roles, role/permission maps,
    /// user-role assignments, legacy user-role maps, and the audit log. Each case covers the positive
    /// round-trip and the matching negative paths (missing id, unknown natural key, tenant isolation).
    /// </summary>
    public static class RbacDatabaseSuite
    {
        /// <summary>Build the RBAC database contract suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "RbacDb",
                displayName: "RBAC Database Contract",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("RbacDb", "Permission_And_RoleMap_Crud_And_ByRole", "Permissions and role/permission maps round-trip and resolve by role",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Permission permission = await db.Permissions.CreateAsync(new Permission
                            {
                                Name = "p1",
                                ResourceTypes = new List<ResourceTypeEnum> { ResourceTypeEnum.Subject },
                                OperationTypes = new List<OperationTypeEnum> { OperationTypeEnum.Read },
                                PermissionType = PermissionTypeEnum.Permit
                            }, ct);
                            if ((await db.Permissions.ReadAsync(permission.Id, ct)) == null) throw new Exception("permission read failed");
                            if (await db.Permissions.ReadAsync("perm_missing", ct) != null) throw new Exception("missing permission should read null");

                            UserRole role = await db.Roles.CreateAsync(new UserRole { Name = "custom-role", Active = true }, ct);
                            RolePermissionMap map = await db.RolePermissionMaps.CreateAsync(new RolePermissionMap { RoleId = role.Id, PermissionId = permission.Id, Active = true }, ct);

                            if (!(await db.RolePermissionMaps.EnumerateByRoleAsync(role.Id, ct)).Exists(m => m.Id == map.Id)) throw new Exception("map should enumerate by role");
                            if (!(await db.Permissions.EnumerateByRoleAsync(role.Id, ct)).Exists(p => p.Id == permission.Id)) throw new Exception("permission should resolve by role via the map");

                            permission.Name = "p1-renamed";
                            await db.Permissions.UpdateAsync(permission, ct);
                            if ((await db.Permissions.ReadAsync(permission.Id, ct))?.Name != "p1-renamed") throw new Exception("permission update did not persist");

                            if (!await db.RolePermissionMaps.DeleteAsync(map.Id, ct)) throw new Exception("map delete should return true");
                            if (!await db.Permissions.DeleteAsync(permission.Id, ct)) throw new Exception("permission delete should return true");
                            if (await db.Permissions.ReadAsync(permission.Id, ct) != null) throw new Exception("deleted permission should read null");
                        }),

                    new TestCaseDescriptor("RbacDb", "Role_Crud_And_ByName", "Roles round-trip and resolve by name; an unknown name reads null",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            UserRole role = await db.Roles.CreateAsync(new UserRole { Name = "reviewer", Active = true }, ct);
                            if ((await db.Roles.ReadAsync(role.Id, ct))?.Name != "reviewer") throw new Exception("read by id failed");
                            if ((await db.Roles.ReadByNameAsync(null, "reviewer", ct))?.Id != role.Id) throw new Exception("read by name failed");
                            if (await db.Roles.ReadByNameAsync(null, "does-not-exist", ct) != null) throw new Exception("unknown role name should read null");
                            if (!(await db.Roles.EnumerateAsync(null, ct)).Exists(r => r.Id == role.Id)) throw new Exception("enumeration should include the role");

                            role.Name = "reviewer-2";
                            await db.Roles.UpdateAsync(role, ct);
                            if ((await db.Roles.ReadAsync(role.Id, ct))?.Name != "reviewer-2") throw new Exception("update did not persist");

                            if (!await db.Roles.DeleteAsync(role.Id, ct)) throw new Exception("delete should return true");
                            if (await db.Roles.ReadAsync(role.Id, ct) != null) throw new Exception("deleted role should read null");
                        }),

                    new TestCaseDescriptor("RbacDb", "UserRoleAssignment_Crud_And_Isolation", "User-role assignments round-trip, enumerate by user/tenant, and stay tenant-isolated",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            UserRoleAssignment assignment = await db.UserRoleAssignments.CreateAsync(new UserRoleAssignment
                            {
                                TenantId = "ten_a",
                                UserId = "usr_a",
                                RoleName = "reviewer",
                                ResourceScope = ResourceScopeEnum.Tenant
                            }, ct);

                            if ((await db.UserRoleAssignments.ReadAsync("ten_a", assignment.Id, ct)) == null) throw new Exception("read failed");
                            if (await db.UserRoleAssignments.ReadAsync("ten_a", "ura_missing", ct) != null) throw new Exception("missing assignment should read null");
                            if (!(await db.UserRoleAssignments.EnumerateByUserAsync("ten_a", "usr_a", ct)).Exists(a => a.Id == assignment.Id)) throw new Exception("enumerate by user should include it");
                            if (!(await db.UserRoleAssignments.EnumerateAsync("ten_a", ct)).Exists(a => a.Id == assignment.Id)) throw new Exception("enumerate by tenant should include it");
                            if ((await db.UserRoleAssignments.EnumerateAsync("ten_b", ct)).Exists(a => a.Id == assignment.Id)) throw new Exception("assignment must not leak across tenants");

                            assignment.RoleName = "editor";
                            await db.UserRoleAssignments.UpdateAsync(assignment, ct);
                            if ((await db.UserRoleAssignments.ReadAsync("ten_a", assignment.Id, ct))?.RoleName != "editor") throw new Exception("update did not persist");

                            if (!await db.UserRoleAssignments.DeleteAsync("ten_a", assignment.Id, ct)) throw new Exception("delete should return true");
                            if (await db.UserRoleAssignments.ReadAsync("ten_a", assignment.Id, ct) != null) throw new Exception("deleted assignment should read null");
                        }),

                    new TestCaseDescriptor("RbacDb", "UserRoleMap_Crud", "Legacy user-role maps round-trip, enumerate by user, and delete idempotently",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            UserRoleMap map = await db.UserRoleMaps.CreateAsync(new UserRoleMap { TenantId = "ten_a", UserId = "usr_a", RoleId = "rol_a", Active = true }, ct);
                            if (!(await db.UserRoleMaps.EnumerateByUserAsync("ten_a", "usr_a", ct)).Exists(m => m.Id == map.Id)) throw new Exception("enumerate by user should include the map");
                            if ((await db.UserRoleMaps.EnumerateByUserAsync("ten_a", "usr_other", ct)).Exists(m => m.Id == map.Id)) throw new Exception("map must not appear for another user");

                            if (!await db.UserRoleMaps.DeleteAsync("ten_a", map.Id, ct)) throw new Exception("delete should return true");
                            if (await db.UserRoleMaps.DeleteAsync("ten_a", map.Id, ct)) throw new Exception("deleting a missing map should return false");
                        }),

                    new TestCaseDescriptor("RbacDb", "Audit_Crud_And_Prune", "Audit records persist, enumerate by tenant, and prune by cutoff",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            AuditRecord record = await db.Audit.CreateAsync(new AuditRecord { TenantId = "ten_a", HttpMethod = "GET", UrlPath = "/v1.0/test" }, ct);
                            if ((await db.Audit.ReadAsync(record.Id, ct))?.UrlPath != "/v1.0/test") throw new Exception("audit read failed");
                            if (await db.Audit.ReadAsync("aud_missing", ct) != null) throw new Exception("missing audit record should read null");
                            if (!(await db.Audit.EnumerateAsync("ten_a", 100, ct)).Exists(a => a.Id == record.Id)) throw new Exception("enumerate by tenant should include the record");

                            int pruned = await db.Audit.PruneAsync(DateTime.UtcNow.AddMinutes(1), ct);
                            if (pruned < 1) throw new Exception("prune should remove the record");
                            if (await db.Audit.ReadAsync(record.Id, ct) != null) throw new Exception("pruned record should read null");
                        })
                });
        }
    }
}
