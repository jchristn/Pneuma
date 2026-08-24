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
    /// Provider-agnostic contract suite for the authentication-related repositories: accounts, administrators,
    /// authentication sessions, credentials, and credential scope assignments. Each case exercises the positive
    /// round-trip (create/read/enumerate/update/delete) and the matching negative paths (read/delete of a
    /// missing id, unknown natural-key lookup, tenant isolation).
    /// </summary>
    public static class AuthDatabaseSuite
    {
        /// <summary>Build the authentication database contract suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "AuthDb",
                displayName: "Auth Database Contract",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("AuthDb", "Account_Crud", "Account create/read/enumerate/update/delete round-trips; missing id reads null and deletes false",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Account created = await db.Accounts.CreateAsync(new Account { Name = "Acct A" }, ct);
                            if (String.IsNullOrEmpty(created.Id)) throw new Exception("create returned no id");

                            Account read = await db.Accounts.ReadAsync(created.Id, ct) ?? throw new Exception("read returned null");
                            if (read.Name != "Acct A") throw new Exception("name did not round-trip");
                            if (await db.Accounts.ReadAsync("acct_missing", ct) != null) throw new Exception("missing account should read null");

                            List<Account> all = await db.Accounts.EnumerateAsync(ct);
                            if (!all.Exists(a => a.Id == created.Id)) throw new Exception("enumeration should include the account");

                            created.Name = "Acct A2";
                            await db.Accounts.UpdateAsync(created, ct);
                            if ((await db.Accounts.ReadAsync(created.Id, ct))?.Name != "Acct A2") throw new Exception("update did not persist");

                            if (!await db.Accounts.DeleteAsync(created.Id, ct)) throw new Exception("delete should return true");
                            if (await db.Accounts.ReadAsync(created.Id, ct) != null) throw new Exception("deleted account should read null");
                            if (await db.Accounts.DeleteAsync(created.Id, ct)) throw new Exception("deleting a missing account should return false");
                        }),

                    new TestCaseDescriptor("AuthDb", "Administrator_Crud_And_ByEmail", "Administrator round-trips and is resolvable by email; an unknown email reads null",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Administrator created = await db.Administrators.CreateAsync(new Administrator { FirstName = "Ada", LastName = "Admin", Email = "ada@pneuma", PasswordSha256 = new string('a', 64) }, ct);

                            if ((await db.Administrators.ReadAsync(created.Id, ct))?.Email != "ada@pneuma") throw new Exception("read by id failed");
                            if ((await db.Administrators.ReadByEmailAsync("ada@pneuma", ct))?.Id != created.Id) throw new Exception("read by email failed");
                            if (await db.Administrators.ReadByEmailAsync("nobody@pneuma", ct) != null) throw new Exception("unknown email should read null");

                            List<Administrator> all = await db.Administrators.EnumerateAsync(ct);
                            if (!all.Exists(a => a.Id == created.Id)) throw new Exception("enumeration should include the administrator");

                            created.LastName = "Renamed";
                            await db.Administrators.UpdateAsync(created, ct);
                            if ((await db.Administrators.ReadAsync(created.Id, ct))?.LastName != "Renamed") throw new Exception("update did not persist");

                            if (!await db.Administrators.DeleteAsync(created.Id, ct)) throw new Exception("delete should return true");
                            if (await db.Administrators.ReadAsync(created.Id, ct) != null) throw new Exception("deleted administrator should read null");
                        }),

                    new TestCaseDescriptor("AuthDb", "AuthSession_Crud_And_DeleteExpired", "Auth sessions round-trip, enumerate by tenant, revoke, and expired ones prune",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            AuthSession live = await db.Sessions.CreateAsync(new AuthSession { TenantId = "ten_a", TokenId = "tok_live", PrincipalType = PrincipalTypeEnum.User, ExpiresUtc = DateTime.UtcNow.AddHours(1) }, ct);
                            if ((await db.Sessions.ReadAsync(live.Id, ct))?.TokenId != "tok_live") throw new Exception("session read failed");
                            if (await db.Sessions.ReadAsync("sess_missing", ct) != null) throw new Exception("missing session should read null");

                            List<AuthSession> forTenant = await db.Sessions.EnumerateAsync("ten_a", ct);
                            if (!forTenant.Exists(s => s.Id == live.Id)) throw new Exception("enumeration by tenant should include the session");
                            List<AuthSession> otherTenant = await db.Sessions.EnumerateAsync("ten_b", ct);
                            if (otherTenant.Exists(s => s.Id == live.Id)) throw new Exception("session must not leak across tenants");

                            live.RevokedUtc = DateTime.UtcNow;
                            live.RevocationReason = "test";
                            await db.Sessions.UpdateAsync(live, ct);
                            if ((await db.Sessions.ReadAsync(live.Id, ct))?.RevokedUtc == null) throw new Exception("revoke did not persist");

                            AuthSession expired = await db.Sessions.CreateAsync(new AuthSession { TenantId = "ten_a", TokenId = "tok_old", PrincipalType = PrincipalTypeEnum.User, ExpiresUtc = DateTime.UtcNow.AddHours(-2) }, ct);
                            int pruned = await db.Sessions.DeleteExpiredAsync(DateTime.UtcNow, ct);
                            if (pruned < 1) throw new Exception("expired session should be pruned");
                            if (await db.Sessions.ReadAsync(expired.Id, ct) != null) throw new Exception("pruned session should read null");

                            if (!await db.Sessions.DeleteAsync(live.Id, ct)) throw new Exception("delete should return true");
                        }),

                    new TestCaseDescriptor("AuthDb", "Credential_Full_Crud_And_Isolation", "Credentials round-trip, enumerate by tenant/user, look up by access key, and stay tenant-isolated",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Credential cred = await db.Credentials.CreateAsync(new Credential { TenantId = "ten_a", UserId = "usr_a", Name = "key", AccessKey = "access_abc", SecretKeyEncrypted = "enc", SecretKeyLast4 = "cdef" }, ct);

                            if ((await db.Credentials.ReadAsync("ten_a", cred.Id, ct))?.AccessKey != "access_abc") throw new Exception("read failed");
                            if (await db.Credentials.ReadAsync("ten_a", "cred_missing", ct) != null) throw new Exception("missing credential should read null");
                            if ((await db.Credentials.ReadByAccessKeyAsync("access_abc", ct))?.Id != cred.Id) throw new Exception("access-key lookup failed");
                            if (await db.Credentials.ReadByAccessKeyAsync("access_nope", ct) != null) throw new Exception("unknown access key should read null");

                            if (!(await db.Credentials.EnumerateAsync("ten_a", ct)).Exists(c => c.Id == cred.Id)) throw new Exception("enumerate by tenant should include it");
                            if (!(await db.Credentials.EnumerateByUserAsync("ten_a", "usr_a", ct)).Exists(c => c.Id == cred.Id)) throw new Exception("enumerate by user should include it");
                            if ((await db.Credentials.EnumerateAsync("ten_b", ct)).Exists(c => c.Id == cred.Id)) throw new Exception("credential must not leak across tenants");

                            cred.Name = "renamed";
                            await db.Credentials.UpdateAsync(cred, ct);
                            if ((await db.Credentials.ReadAsync("ten_a", cred.Id, ct))?.Name != "renamed") throw new Exception("update did not persist");

                            if (!await db.Credentials.DeleteAsync("ten_a", cred.Id, ct)) throw new Exception("delete should return true");
                            if (await db.Credentials.ReadAsync("ten_a", cred.Id, ct) != null) throw new Exception("deleted credential should read null");
                        }),

                    new TestCaseDescriptor("AuthDb", "CredentialScopeAssignment_Crud", "Credential scope assignments round-trip and enumerate by credential",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            CredentialScopeAssignment assignment = await db.CredentialScopeAssignments.CreateAsync(new CredentialScopeAssignment
                            {
                                TenantId = "ten_a",
                                CredentialId = "cred_a",
                                ResourceScope = ResourceScopeEnum.Tenant,
                                Permissions = new List<OperationTypeEnum> { OperationTypeEnum.Read },
                                ResourceTypes = new List<ResourceTypeEnum> { ResourceTypeEnum.Subject }
                            }, ct);

                            if ((await db.CredentialScopeAssignments.ReadAsync("ten_a", assignment.Id, ct)) == null) throw new Exception("read failed");
                            if (await db.CredentialScopeAssignments.ReadAsync("ten_a", "csa_missing", ct) != null) throw new Exception("missing assignment should read null");
                            if (!(await db.CredentialScopeAssignments.EnumerateByCredentialAsync("ten_a", "cred_a", ct)).Exists(a => a.Id == assignment.Id)) throw new Exception("enumerate by credential should include it");

                            assignment.ResourceScope = ResourceScopeEnum.Resource;
                            assignment.ResourceId = "sub_x";
                            await db.CredentialScopeAssignments.UpdateAsync(assignment, ct);
                            if ((await db.CredentialScopeAssignments.ReadAsync("ten_a", assignment.Id, ct))?.ResourceId != "sub_x") throw new Exception("update did not persist");

                            if (!await db.CredentialScopeAssignments.DeleteAsync("ten_a", assignment.Id, ct)) throw new Exception("delete should return true");
                            if (await db.CredentialScopeAssignments.ReadAsync("ten_a", assignment.Id, ct) != null) throw new Exception("deleted assignment should read null");
                        })
                });
        }
    }
}
