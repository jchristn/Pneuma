namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Security;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Provider-agnostic database contract suite: migrations, first-boot seeding, CRUD,
    /// tenant isolation, and the ingestion job claim.
    /// </summary>
    public static class DatabaseSuite
    {
        /// <summary>Build the database contract suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Database",
                displayName: "Database Contract",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("Database", "Seeding_BuiltInRoles", "Seeding creates all built-in roles",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            List<UserRole> roles = await db.Roles.EnumerateAsync(null, ct);
                            foreach (string name in BuiltInRoles.Names())
                            {
                                if (!roles.Exists(r => r.Name == name))
                                    throw new Exception("Missing built-in role: " + name);
                            }
                        }),

                    new TestCaseDescriptor("Database", "Seeding_AdminUser", "Seeding creates a system admin user",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            List<Tenant> tenants = await db.Tenants.EnumerateAsync(ct);
                            Tenant tenant = tenants.Find(t => t.Name == "System") ?? throw new Exception("System tenant missing");
                            User admin = await db.Users.ReadByEmailAsync(tenant.Id, "admin@pneuma", ct)
                                ?? throw new Exception("Admin user missing");
                            if (!admin.IsAdmin) throw new Exception("Admin user is not IsAdmin");
                            if (!PasswordHasher.Verify("password", admin.PasswordSha256))
                                throw new Exception("Admin password hash does not verify");
                        }),

                    new TestCaseDescriptor("Database", "Seeding_Prompts", "Seeding creates prompts",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            if (await db.Prompts.ReadByKeyAsync(null, "ontology.classify", ct) == null)
                                throw new Exception("ontology.classify prompt missing");
                            if (await db.Prompts.ReadByKeyAsync(null, "ontology.definition", ct) == null)
                                throw new Exception("ontology.definition prompt missing");
                        }),

                    new TestCaseDescriptor("Database", "Seeding_Idempotent", "Seeding twice does not duplicate",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            await FirstBootSeeder.SeedAsync(db, new SeedOptions(), new Pneuma.Core.Security.Aes256Cipher("pneuma-test-signing-key-0001"), ct);
                            List<UserRole> roles = await db.Roles.EnumerateAsync(null, ct);
                            int tenantAdminCount = roles.FindAll(r => r.Name == BuiltInRoles.TenantAdmin).Count;
                            if (tenantAdminCount != 1) throw new Exception("Re-seeding duplicated built-in roles: " + tenantAdminCount);
                        }),

                    new TestCaseDescriptor("Database", "Tenant_Crud", "Tenant create/read/update/delete",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }, ct);
                            Tenant read = await db.Tenants.ReadAsync(tenant.Id, ct) ?? throw new Exception("Tenant not read back");
                            read.Name = "Acme Records";
                            await db.Tenants.UpdateAsync(read, ct);
                            Tenant updated = await db.Tenants.ReadAsync(tenant.Id, ct) ?? throw new Exception("Tenant gone");
                            if (updated.Name != "Acme Records") throw new Exception("Update did not persist");
                            if (!await db.Tenants.DeleteAsync(tenant.Id, ct)) throw new Exception("Delete returned false");
                            if (await db.Tenants.ReadAsync(tenant.Id, ct) != null) throw new Exception("Tenant not deleted");
                        }),

                    new TestCaseDescriptor("Database", "User_TenantIsolation", "User enumeration does not leak across tenants",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Tenant a = await db.Tenants.CreateAsync(new Tenant { Name = "TenantA" }, ct);
                            Tenant b = await db.Tenants.CreateAsync(new Tenant { Name = "TenantB" }, ct);
                            await db.Users.CreateAsync(new User { TenantId = a.Id, Email = "user@a.com" }, ct);
                            await db.Users.CreateAsync(new User { TenantId = b.Id, Email = "user@b.com" }, ct);
                            List<User> aUsers = await db.Users.EnumerateAsync(a.Id, ct);
                            if (aUsers.Exists(u => u.Email == "user@b.com"))
                                throw new Exception("Cross-tenant user leak detected");
                            if (!aUsers.Exists(u => u.Email == "user@a.com"))
                                throw new Exception("Tenant A user missing from enumeration");
                        }),

                    new TestCaseDescriptor("Database", "Credential_AccessKeyLookup", "Credential is found by access key",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Tenant t = await db.Tenants.CreateAsync(new Tenant { Name = "CredTenant" }, ct);
                            User u = await db.Users.CreateAsync(new User { TenantId = t.Id, Email = "c@t.com" }, ct);
                            string accessKey = KeyGenerator.GenerateAccessKey();
                            await db.Credentials.CreateAsync(new Credential
                            {
                                TenantId = t.Id,
                                UserId = u.Id,
                                Name = "CI",
                                AccessKey = accessKey,
                                SecretKeyLast4 = "abcd"
                            }, ct);
                            Credential found = await db.Credentials.ReadByAccessKeyAsync(accessKey, ct)
                                ?? throw new Exception("Credential not found by access key");
                            if (found.TenantId != t.Id) throw new Exception("Wrong tenant on credential");
                        }),

                    new TestCaseDescriptor("Database", "IngestionJob_Claim", "ClaimNextQueued claims a queued job",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Tenant t = await db.Tenants.CreateAsync(new Tenant { Name = "JobTenant" }, ct);
                            Subject c = await db.Subjects.CreateAsync(new Subject { TenantId = t.Id, DisplayName = "Subject" }, ct);
                            SubjectLink link = await db.SubjectLinks.CreateAsync(new SubjectLink
                            {
                                TenantId = t.Id, SubjectId = c.Id, Url = "https://example.com/a"
                            }, ct);
                            await db.IngestionJobs.CreateAsync(new IngestionJob
                            {
                                TenantId = t.Id, SubjectId = c.Id, LinkId = link.Id, SourceUrl = link.Url,
                                Status = IngestionStatusEnum.Queued
                            }, ct);

                            IngestionJob claimed = await db.IngestionJobs.ClaimNextQueuedAsync(ct)
                                ?? throw new Exception("No job claimed");
                            if (claimed.Status != IngestionStatusEnum.Processing)
                                throw new Exception("Claimed job not marked Processing");
                            if (claimed.AttemptCount != 1)
                                throw new Exception("Claimed job attempt count not incremented");
                        }),

                    new TestCaseDescriptor("Database", "IngestionJob_ClaimEmptyQueue_ReturnsNull", "Claiming with no queued jobs returns null",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            IngestionJob? none = await db.IngestionJobs.ClaimNextQueuedAsync(ct);
                            if (none != null) throw new Exception("claim against an empty queue should return null, got job " + none.Id);
                        }),

                    new TestCaseDescriptor("Database", "Credential_UnknownAccessKey_ReturnsNull", "Lookup by an unknown access key returns null",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Credential? missing = await db.Credentials.ReadByAccessKeyAsync("access_does_not_exist", ct);
                            if (missing != null) throw new Exception("an unknown access key should not resolve to a credential");
                        }),

                    new TestCaseDescriptor("Database", "Tenant_ReadMissing_ReturnsNull", "Reading a non-existent tenant returns null",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Tenant? missing = await db.Tenants.ReadAsync("ten_missing", ct);
                            if (missing != null) throw new Exception("reading an unknown tenant id should return null");
                        }),

                    new TestCaseDescriptor("Database", "Transaction_RollsBackOnFailure", "A failed statement in a transactional batch rolls the whole batch back",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            await db.ExecuteQueryAsync("CREATE TABLE tx_rollback_test (id VARCHAR(64) PRIMARY KEY);", false, ct);

                            bool threw = false;
                            try
                            {
                                await db.ExecuteQueriesAsync(new List<string>
                                {
                                    "INSERT INTO tx_rollback_test (id) VALUES ('a');",
                                    "INSERT INTO tx_rollback_test (id) VALUES ('a');"
                                }, true, ct);
                            }
                            catch
                            {
                                threw = true;
                            }

                            if (!threw) throw new Exception("Expected the batch to fail on the duplicate primary key");

                            DataTable rows = await db.ExecuteQueryAsync("SELECT id FROM tx_rollback_test;", false, ct);
                            if (rows.Rows.Count != 0) throw new Exception("Transaction did not roll back; found " + rows.Rows.Count + " row(s)");
                        }),

                    new TestCaseDescriptor("Database", "Transaction_CommitsOnSuccess", "A successful transactional batch commits all statements",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            await db.ExecuteQueryAsync("CREATE TABLE tx_commit_test (id VARCHAR(64) PRIMARY KEY);", false, ct);

                            await db.ExecuteQueriesAsync(new List<string>
                            {
                                "INSERT INTO tx_commit_test (id) VALUES ('a');",
                                "INSERT INTO tx_commit_test (id) VALUES ('b');"
                            }, true, ct);

                            DataTable rows = await db.ExecuteQueryAsync("SELECT id FROM tx_commit_test;", false, ct);
                            if (rows.Rows.Count != 2) throw new Exception("Expected 2 committed rows, found " + rows.Rows.Count);
                        }),

                    new TestCaseDescriptor("Database", "CreateWithJob_PersistsLinkAndJob", "CreateWithJobAsync persists the link and its initial job atomically",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Tenant t = await db.Tenants.CreateAsync(new Tenant { Name = "CwjTenant" }, ct);
                            Subject c = await db.Subjects.CreateAsync(new Subject { TenantId = t.Id, DisplayName = "Subject" }, ct);

                            SubjectLink link = new SubjectLink { TenantId = t.Id, SubjectId = c.Id, Url = "https://example.com/a" };
                            IngestionJob job = new IngestionJob
                            {
                                TenantId = t.Id, SubjectId = c.Id, LinkId = link.Id, SourceUrl = link.Url,
                                Status = IngestionStatusEnum.Queued
                            };
                            await db.SubjectLinks.CreateWithJobAsync(link, job, ct);

                            if (await db.SubjectLinks.ReadAsync(t.Id, link.Id, ct) == null) throw new Exception("Link was not persisted");
                            List<IngestionJob> jobs = await db.IngestionJobs.EnumerateByLinkAsync(t.Id, link.Id, ct);
                            if (jobs.Count != 1) throw new Exception("Expected exactly 1 job for the link, got " + jobs.Count);
                        }),

                    new TestCaseDescriptor("Database", "DeleteWithEvents_RemovesJobAndEvents", "DeleteWithEventsAsync removes a job and its events together",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Tenant t = await db.Tenants.CreateAsync(new Tenant { Name = "DweTenant" }, ct);
                            Subject c = await db.Subjects.CreateAsync(new Subject { TenantId = t.Id, DisplayName = "Subject" }, ct);
                            SubjectLink link = await db.SubjectLinks.CreateAsync(new SubjectLink { TenantId = t.Id, SubjectId = c.Id, Url = "https://example.com/a" }, ct);
                            IngestionJob job = await db.IngestionJobs.CreateAsync(new IngestionJob
                            {
                                TenantId = t.Id, SubjectId = c.Id, LinkId = link.Id, SourceUrl = link.Url, Status = IngestionStatusEnum.Queued
                            }, ct);
                            await db.IngestionJobEvents.CreateAsync(new IngestionJobEvent
                            {
                                TenantId = t.Id, JobId = job.Id, Stage = IngestionStageEnum.TypeDetection, Status = IngestionStatusEnum.Processing, Message = "started"
                            }, ct);

                            bool deleted = await db.IngestionJobs.DeleteWithEventsAsync(t.Id, job.Id, ct);
                            if (!deleted) throw new Exception("DeleteWithEventsAsync should report the job existed");
                            if (await db.IngestionJobs.ReadAsync(t.Id, job.Id, ct) != null) throw new Exception("Job was not deleted");
                            List<IngestionJobEvent> events = await db.IngestionJobEvents.EnumerateByJobAsync(t.Id, job.Id, ct);
                            if (events.Count != 0) throw new Exception("Job events were not deleted; found " + events.Count);
                        }),

                    new TestCaseDescriptor("Database", "Cascade_LinkDelete_RemovesJobsAndEvents", "DeleteWithJobsAsync removes a link with its jobs and events, leaving the subject",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Tenant t = await db.Tenants.CreateAsync(new Tenant { Name = "LnkCascade" }, ct);
                            Subject c = await db.Subjects.CreateAsync(new Subject { TenantId = t.Id, DisplayName = "Subject" }, ct);
                            SubjectLink link = await db.SubjectLinks.CreateAsync(new SubjectLink { TenantId = t.Id, SubjectId = c.Id, Url = "https://example.com/a" }, ct);
                            IngestionJob job = await db.IngestionJobs.CreateAsync(new IngestionJob
                            {
                                TenantId = t.Id, SubjectId = c.Id, LinkId = link.Id, SourceUrl = link.Url, Status = IngestionStatusEnum.Queued
                            }, ct);
                            await db.IngestionJobEvents.CreateAsync(new IngestionJobEvent
                            {
                                TenantId = t.Id, JobId = job.Id, Stage = IngestionStageEnum.TypeDetection, Status = IngestionStatusEnum.Processing, Message = "started"
                            }, ct);

                            bool deleted = await db.SubjectLinks.DeleteWithJobsAsync(t.Id, link.Id, new List<string> { job.Id }, ct);
                            if (!deleted) throw new Exception("DeleteWithJobsAsync should report the link existed");
                            if (await db.SubjectLinks.ReadAsync(t.Id, link.Id, ct) != null) throw new Exception("Link was not deleted");
                            if (await db.IngestionJobs.ReadAsync(t.Id, job.Id, ct) != null) throw new Exception("Job was not deleted");
                            if ((await db.IngestionJobEvents.EnumerateByJobAsync(t.Id, job.Id, ct)).Count != 0) throw new Exception("Job events were not deleted");
                            if (await db.Subjects.ReadAsync(t.Id, c.Id, ct) == null) throw new Exception("The subject should survive a link delete");
                        }),

                    new TestCaseDescriptor("Database", "Cascade_SubjectDelete_RemovesSubordinates", "DeleteWithSubordinatesAsync removes a subject with all links, jobs, and events",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Tenant t = await db.Tenants.CreateAsync(new Tenant { Name = "CreCascade" }, ct);
                            Subject c = await db.Subjects.CreateAsync(new Subject { TenantId = t.Id, DisplayName = "Subject" }, ct);
                            SubjectLink link = await db.SubjectLinks.CreateAsync(new SubjectLink { TenantId = t.Id, SubjectId = c.Id, Url = "https://example.com/a" }, ct);
                            IngestionJob job = await db.IngestionJobs.CreateAsync(new IngestionJob
                            {
                                TenantId = t.Id, SubjectId = c.Id, LinkId = link.Id, SourceUrl = link.Url, Status = IngestionStatusEnum.Queued
                            }, ct);
                            await db.IngestionJobEvents.CreateAsync(new IngestionJobEvent
                            {
                                TenantId = t.Id, JobId = job.Id, Stage = IngestionStageEnum.TypeDetection, Status = IngestionStatusEnum.Processing, Message = "started"
                            }, ct);

                            bool deleted = await db.Subjects.DeleteWithSubordinatesAsync(t.Id, c.Id, new List<string> { link.Id }, new List<string> { job.Id }, ct);
                            if (!deleted) throw new Exception("DeleteWithSubordinatesAsync should report the subject existed");
                            if (await db.Subjects.ReadAsync(t.Id, c.Id, ct) != null) throw new Exception("Subject was not deleted");
                            if (await db.SubjectLinks.ReadAsync(t.Id, link.Id, ct) != null) throw new Exception("Link was not deleted");
                            if (await db.IngestionJobs.ReadAsync(t.Id, job.Id, ct) != null) throw new Exception("Job was not deleted");
                            if ((await db.IngestionJobEvents.EnumerateByJobAsync(t.Id, job.Id, ct)).Count != 0) throw new Exception("Job events were not deleted");
                        }),

                    new TestCaseDescriptor("Database", "Seeding_RolesHavePermissions", "CreateWithPermissionsAsync provisions each built-in role with its permissions atomically",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            UserRole role = await db.Roles.ReadByNameAsync(null, BuiltInRoles.TenantAdmin, ct)
                                ?? throw new Exception("TenantAdmin role missing after seeding");
                            List<Permission> permissions = await db.Permissions.EnumerateByRoleAsync(role.Id, ct);
                            if (permissions.Count == 0) throw new Exception("The seeded role has no permissions; the atomic role+permission seed did not persist");
                        }),

                    new TestCaseDescriptor("Database", "Concurrency_ParallelCreatesAllPersist", "Concurrent subject inserts all persist (SQLite serializes; other providers run concurrently)",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Tenant t = await db.Tenants.CreateAsync(new Tenant { Name = "ConcTenant" }, ct);

                            List<Task<Subject>> creates = new List<Task<Subject>>();
                            for (int i = 0; i < 20; i++)
                            {
                                creates.Add(db.Subjects.CreateAsync(new Subject { TenantId = t.Id, DisplayName = "Subject " + i }, ct));
                            }
                            await Task.WhenAll(creates);

                            List<Subject> persisted = await db.Subjects.EnumerateAsync(t.Id, ct);
                            if (persisted.Count != 20) throw new Exception("Expected 20 concurrently-created subjects, found " + persisted.Count);
                        }),

                    new TestCaseDescriptor("Database", "PromptProvenance_JobEventSnapshotIsImmutable", "A recorded prompt-provenance event is not altered when the prompt is later edited",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Prompt prompt = await db.Prompts.ReadByKeyAsync(null, "ontology.classify", ct)
                                ?? throw new Exception("Seeded ontology.classify prompt missing");
                            int recordedVersion = prompt.Version;

                            Tenant t = await db.Tenants.CreateAsync(new Tenant { Name = "ProvTenant" }, ct);
                            Subject c = await db.Subjects.CreateAsync(new Subject { TenantId = t.Id, DisplayName = "Subject" }, ct);
                            SubjectLink link = await db.SubjectLinks.CreateAsync(new SubjectLink { TenantId = t.Id, SubjectId = c.Id, Url = "https://example.com/a" }, ct);
                            IngestionJob job = await db.IngestionJobs.CreateAsync(new IngestionJob
                            {
                                TenantId = t.Id, SubjectId = c.Id, LinkId = link.Id, SourceUrl = link.Url, Status = IngestionStatusEnum.Processing
                            }, ct);

                            // The job records the prompt version it actually used (as the production pipeline does).
                            string provenance = "Prompt provenance: ontology.classify v" + recordedVersion;
                            await db.IngestionJobEvents.CreateAsync(new IngestionJobEvent
                            {
                                TenantId = t.Id, JobId = job.Id, Stage = IngestionStageEnum.Categorization, Status = IngestionStatusEnum.Processing, Message = provenance
                            }, ct);

                            // Editing the prompt afterwards must not rewrite what the completed job recorded.
                            prompt.Content = (prompt.Content ?? String.Empty) + " EDITED";
                            prompt.Version = recordedVersion + 1;
                            await db.Prompts.UpdateAsync(prompt, ct);

                            Prompt updated = await db.Prompts.ReadByKeyAsync(null, "ontology.classify", ct) ?? throw new Exception("Prompt vanished after update");
                            if (updated.Version != recordedVersion + 1) throw new Exception("Prompt version was not bumped by the edit");

                            List<IngestionJobEvent> events = await db.IngestionJobEvents.EnumerateByJobAsync(t.Id, job.Id, ct);
                            IngestionJobEvent recorded = events.Find(e => e.Message != null && e.Message.Contains("Prompt provenance"))
                                ?? throw new Exception("Provenance event missing");
                            if (recorded.Message != provenance)
                                throw new Exception("Provenance snapshot was mutated by the later prompt edit: " + recorded.Message);
                            if (!recorded.Message!.Contains("v" + recordedVersion) || recorded.Message!.Contains("v" + (recordedVersion + 1)))
                                throw new Exception("Provenance snapshot no longer reflects the version the job used");
                        }),

                    new TestCaseDescriptor("Database", "Subject_ExtendedFields_RoundTrip", "Subject slug, prompts, thinking, retention (clamped), and deletion status round-trip; slug is resolvable",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Tenant t = await db.Tenants.CreateAsync(new Tenant { Name = "SubFields" }, ct);
                            Subject created = await db.Subjects.CreateAsync(new Subject
                            {
                                TenantId = t.Id,
                                DisplayName = "Ada Lovelace",
                                UrlSlug = "ada-lovelace",
                                Tagline = "Ask me about Ada.",
                                ThinkingEnabled = true,
                                SystemPrompt = "Be precise.",
                                OntologyClassifyPrompt = "Classify people.",
                                OntologyDefinitionPrompt = "People and works.",
                                EmbeddingModel = "eep_ada",
                                InferenceModel = "cep_ada",
                                RerankingModel = "cep_rerank",
                                PromptRewriteModel = "cep_rewrite",
                                Collection = "col_ada",
                                RerankingPrompt = "Rank by relevance.",
                                PromptRewritePrompt = "Rewrite tersely.",
                                RetrievalFilterJson = "{\"required\":[{\"key\":\"rights\",\"condition\":\"Equals\",\"value\":\"public\"}]}",
                                HistoryRetentionDays = 0 // must clamp to >= 1
                            }, ct);
                            if (created.HistoryRetentionDays != 1) throw new Exception("HistoryRetentionDays must clamp to a minimum of 1, got " + created.HistoryRetentionDays);

                            Subject read = await db.Subjects.ReadAsync(t.Id, created.Id, ct) ?? throw new Exception("Subject vanished after create");
                            if (read.UrlSlug != "ada-lovelace") throw new Exception("UrlSlug did not round-trip");
                            if (read.Tagline != "Ask me about Ada.") throw new Exception("Tagline did not round-trip");
                            if (!read.ThinkingEnabled) throw new Exception("ThinkingEnabled did not round-trip");
                            if (read.SystemPrompt != "Be precise.") throw new Exception("SystemPrompt did not round-trip");
                            if (read.OntologyClassifyPrompt != "Classify people.") throw new Exception("OntologyClassifyPrompt did not round-trip");
                            if (read.OntologyDefinitionPrompt != "People and works.") throw new Exception("OntologyDefinitionPrompt did not round-trip");
                            if (read.EmbeddingModel != "eep_ada") throw new Exception("EmbeddingModel did not round-trip");
                            if (read.InferenceModel != "cep_ada") throw new Exception("InferenceModel did not round-trip");
                            if (read.RerankingModel != "cep_rerank") throw new Exception("RerankingModel did not round-trip");
                            if (read.PromptRewriteModel != "cep_rewrite") throw new Exception("PromptRewriteModel did not round-trip");
                            if (read.Collection != "col_ada") throw new Exception("Collection did not round-trip");
                            if (read.RerankingPrompt != "Rank by relevance.") throw new Exception("RerankingPrompt did not round-trip");
                            if (read.PromptRewritePrompt != "Rewrite tersely.") throw new Exception("PromptRewritePrompt did not round-trip");
                            if (read.RetrievalFilterJson == null || !read.RetrievalFilterJson.Contains("rights")) throw new Exception("RetrievalFilterJson did not round-trip");
                            if (read.DeletionStatus != SubjectDeletionStatusEnum.None) throw new Exception("New subject must have DeletionStatus None");

                            Subject bySlug = await db.Subjects.ReadBySlugAsync(t.Id, "ada-lovelace", ct) ?? throw new Exception("ReadBySlug failed to resolve the slug");
                            if (bySlug.Id != created.Id) throw new Exception("ReadBySlug resolved the wrong subject");
                            Subject? missing = await db.Subjects.ReadBySlugAsync(t.Id, "nope", ct);
                            if (missing != null) throw new Exception("ReadBySlug must return null for an unknown slug");
                        }),

                    new TestCaseDescriptor("Database", "ChatTurn_Crud_And_RetentionPrune", "Chat turns persist, enumerate newest-first by subject, and prune by cutoff",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Tenant t = await db.Tenants.CreateAsync(new Tenant { Name = "Turns" }, ct);
                            Subject s = await db.Subjects.CreateAsync(new Subject { TenantId = t.Id, DisplayName = "S", UrlSlug = "s" }, ct);

                            ChatTurnRecord turn = await db.ChatTurns.CreateAsync(new ChatTurnRecord
                            {
                                TenantId = t.Id, SubjectId = s.Id, Question = "Who?", Answer = "Ada.",
                                Model = "gemma3:4b", PromptTokens = 10, CompletionTokens = 5, TotalTokens = 15,
                                GenerationMs = 1200, ThinkingMs = 300, ContextSize = 8192,
                                PerformanceJson = "{\"schemaVersion\":1,\"stages\":[]}", PerformanceSchemaVersion = 1
                            }, ct);

                            ChatTurnRecord read = await db.ChatTurns.ReadAsync(t.Id, turn.Id, ct) ?? throw new Exception("Turn vanished after create");
                            if (read.Answer != "Ada." || read.TotalTokens != 15) throw new Exception("Turn fields did not round-trip");
                            if (read.PerformanceSchemaVersion != 1 || String.IsNullOrEmpty(read.PerformanceJson)) throw new Exception("Performance telemetry did not round-trip");

                            List<ChatTurnRecord> forSubject = await db.ChatTurns.EnumerateAsync(t.Id, s.Id, ct);
                            if (forSubject.Count != 1) throw new Exception("Expected 1 turn for the subject, got " + forSubject.Count);

                            // Prune with a past cutoff keeps the fresh turn; a future cutoff removes it.
                            await db.ChatTurns.DeleteOlderThanAsync(t.Id, s.Id, DateTime.UtcNow.AddDays(-1), ct);
                            if ((await db.ChatTurns.EnumerateAsync(t.Id, s.Id, ct)).Count != 1) throw new Exception("A past cutoff must not prune a fresh turn");
                            await db.ChatTurns.DeleteOlderThanAsync(t.Id, s.Id, DateTime.UtcNow.AddDays(1), ct);
                            if ((await db.ChatTurns.EnumerateAsync(t.Id, s.Id, ct)).Count != 0) throw new Exception("A future cutoff must prune the turn");
                        }),

                    new TestCaseDescriptor("Database", "ChatFeedback_Crud_And_DeleteBySubject", "Feedback persists, enumerates by subject, and is removed on subject cascade",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Tenant t = await db.Tenants.CreateAsync(new Tenant { Name = "Fb" }, ct);
                            Subject s = await db.Subjects.CreateAsync(new Subject { TenantId = t.Id, DisplayName = "S", UrlSlug = "s" }, ct);
                            ChatTurnRecord turn = await db.ChatTurns.CreateAsync(new ChatTurnRecord { TenantId = t.Id, SubjectId = s.Id, Question = "q", Answer = "a" }, ct);

                            await db.ChatFeedback.CreateAsync(new ChatFeedback { TenantId = t.Id, TurnId = turn.Id, SubjectId = s.Id, Rating = FeedbackRatingEnum.Down, Comment = "wrong" }, ct);
                            List<ChatFeedback> list = await db.ChatFeedback.EnumerateAsync(t.Id, s.Id, ct);
                            if (list.Count != 1) throw new Exception("Expected 1 feedback, got " + list.Count);
                            if (list[0].Rating != FeedbackRatingEnum.Down || list[0].Comment != "wrong") throw new Exception("Feedback fields did not round-trip");

                            await db.ChatFeedback.DeleteBySubjectAsync(t.Id, s.Id, ct);
                            if ((await db.ChatFeedback.EnumerateAsync(t.Id, s.Id, ct)).Count != 0) throw new Exception("DeleteBySubject must remove the subject's feedback");
                        }),

                    new TestCaseDescriptor("Database", "ChatTurnPerfEvent_Crud_And_Cascade", "Performance events persist, enumerate by turn/subject, prune by cutoff, and delete by subject",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Tenant t = await db.Tenants.CreateAsync(new Tenant { Name = "Perf" }, ct);
                            Subject s = await db.Subjects.CreateAsync(new Subject { TenantId = t.Id, DisplayName = "S", UrlSlug = "s" }, ct);
                            ChatTurnRecord turn = await db.ChatTurns.CreateAsync(new ChatTurnRecord { TenantId = t.Id, SubjectId = s.Id, Question = "q", Answer = "a" }, ct);

                            await db.ChatTurnPerfEvents.CreateManyAsync(new List<ChatTurnPerfEvent>
                            {
                                new ChatTurnPerfEvent { TenantId = t.Id, TurnId = turn.Id, SubjectId = s.Id, Stage = "prompt_rewrite", Kind = "inference", DurationMs = 12.5, Success = true },
                                new ChatTurnPerfEvent { TenantId = t.Id, TurnId = turn.Id, SubjectId = s.Id, Stage = "final_inference", Kind = "inference", Provider = "Ollama", Model = "gemma3:4b", DurationMs = 900, TimeToFirstTokenMs = 120, PromptTokens = 40, CompletionTokens = 60, Success = true }
                            }, ct);

                            List<ChatTurnPerfEvent> byTurn = await db.ChatTurnPerfEvents.EnumerateByTurnAsync(t.Id, turn.Id, ct);
                            if (byTurn.Count != 2) throw new Exception("Expected 2 perf events for the turn, got " + byTurn.Count);
                            ChatTurnPerfEvent final = byTurn.Find(e => e.Stage == "final_inference") ?? throw new Exception("final_inference stage missing");
                            if (final.Model != "gemma3:4b" || final.PromptTokens != 40 || final.CompletionTokens != 60 || final.TimeToFirstTokenMs != 120) throw new Exception("Perf-event fields did not round-trip");

                            List<ChatTurnPerfEvent> bySubject = await db.ChatTurnPerfEvents.EnumerateBySubjectAsync(t.Id, s.Id, DateTime.UtcNow.AddDays(-1), ct);
                            if (bySubject.Count != 2) throw new Exception("Expected 2 perf events for the subject since yesterday, got " + bySubject.Count);

                            await db.ChatTurnPerfEvents.DeleteOlderThanAsync(t.Id, s.Id, DateTime.UtcNow.AddDays(-1), ct);
                            if ((await db.ChatTurnPerfEvents.EnumerateByTurnAsync(t.Id, turn.Id, ct)).Count != 2) throw new Exception("A past cutoff must not prune fresh perf events");
                            await db.ChatTurnPerfEvents.DeleteBySubjectAsync(t.Id, s.Id, ct);
                            if ((await db.ChatTurnPerfEvents.EnumerateByTurnAsync(t.Id, turn.Id, ct)).Count != 0) throw new Exception("DeleteBySubject must remove the subject's perf events");
                        }),

                    new TestCaseDescriptor("Database", "ChatThread_And_ToolCall_Crud_And_Cascade", "Threads and tool calls persist, enumerate by thread/turn, and cascade on thread delete",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Tenant t = await db.Tenants.CreateAsync(new Tenant { Name = "Thr" }, ct);
                            Subject s = await db.Subjects.CreateAsync(new Subject { TenantId = t.Id, DisplayName = "S", UrlSlug = "s" }, ct);

                            ChatThread thread = await db.ChatThreads.CreateAsync(new ChatThread { TenantId = t.Id, SubjectId = s.Id, Title = "First convo" }, ct);
                            ChatThread readThread = await db.ChatThreads.ReadAsync(t.Id, thread.Id, ct) ?? throw new Exception("Thread vanished after create");
                            if (readThread.Title != "First convo") throw new Exception("Thread title did not round-trip");

                            readThread.Title = "Renamed";
                            await db.ChatThreads.UpdateAsync(readThread, ct);
                            if ((await db.ChatThreads.ReadAsync(t.Id, thread.Id, ct))!.Title != "Renamed") throw new Exception("Thread rename did not persist");

                            ChatTurnRecord turn = await db.ChatTurns.CreateAsync(new ChatTurnRecord { TenantId = t.Id, SubjectId = s.Id, ThreadId = thread.Id, Question = "q", Answer = "a" }, ct);
                            if ((await db.ChatTurns.EnumerateByThreadAsync(t.Id, thread.Id, ct)).Count != 1) throw new Exception("Expected 1 turn in the thread");

                            await db.ChatToolCalls.CreateManyAsync(new List<ChatToolCall>
                            {
                                new ChatToolCall { TenantId = t.Id, TurnId = turn.Id, SubjectId = s.Id, ToolName = "pneuma_search", ArgumentsJson = "{\"query\":\"x\"}", OutputJson = "{}", Success = true, DurationMs = 12, Sequence = 0 }
                            }, ct);
                            if ((await db.ChatToolCalls.EnumerateByTurnAsync(t.Id, turn.Id, ct)).Count != 1) throw new Exception("Expected 1 tool call for the turn");

                            // Cascade: deleting the turn's tool calls, then the thread's turns, then the thread.
                            await db.ChatToolCalls.DeleteByTurnAsync(t.Id, turn.Id, ct);
                            if ((await db.ChatToolCalls.EnumerateByTurnAsync(t.Id, turn.Id, ct)).Count != 0) throw new Exception("DeleteByTurn must remove the turn's tool calls");
                            await db.ChatTurns.DeleteByThreadAsync(t.Id, thread.Id, ct);
                            if ((await db.ChatTurns.EnumerateByThreadAsync(t.Id, thread.Id, ct)).Count != 0) throw new Exception("DeleteByThread must remove the thread's turns");
                            await db.ChatThreads.DeleteAsync(t.Id, thread.Id, ct);
                            if (await db.ChatThreads.ReadAsync(t.Id, thread.Id, ct) != null) throw new Exception("Thread delete must remove the thread");
                        }),

                    new TestCaseDescriptor("Database", "Analytics_Report_Aggregates", "Analytics aggregates turn volume, latency percentiles, stages, and feedback",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Tenant t = await db.Tenants.CreateAsync(new Tenant { Name = "An" }, ct);
                            Subject s = await db.Subjects.CreateAsync(new Subject { TenantId = t.Id, DisplayName = "S", UrlSlug = "s" }, ct);

                            for (int i = 0; i < 5; i++)
                            {
                                ChatTurnRecord turn = await db.ChatTurns.CreateAsync(new ChatTurnRecord
                                {
                                    TenantId = t.Id, SubjectId = s.Id, Question = "q", Answer = "a",
                                    GenerationMs = 100 * (i + 1), CompletionTokens = 20, PromptTokens = 30, TimeToFirstTokenMs = 50
                                }, ct);
                                await db.ChatTurnPerfEvents.CreateManyAsync(new System.Collections.Generic.List<ChatTurnPerfEvent>
                                {
                                    new ChatTurnPerfEvent { TenantId = t.Id, TurnId = turn.Id, SubjectId = s.Id, Stage = "final_inference", Kind = "inference", DurationMs = 100 * (i + 1) }
                                }, ct);
                            }
                            await db.ChatFeedback.CreateAsync(new ChatFeedback { TenantId = t.Id, TurnId = "trn_x", SubjectId = s.Id, Rating = FeedbackRatingEnum.Up }, ct);

                            Pneuma.Server.Services.AnalyticsService analytics = new Pneuma.Server.Services.AnalyticsService(db);
                            Pneuma.Core.Responses.AnalyticsReport report = await analytics.BuildAsync(t.Id, s.Id, DateTime.UtcNow.AddDays(-1), ct);
                            if (report.Overview.TurnCount != 5) throw new Exception("Expected 5 turns, got " + report.Overview.TurnCount);
                            if (report.Overview.ThumbsUp != 1) throw new Exception("Expected 1 thumbs-up, got " + report.Overview.ThumbsUp);
                            if (report.Overview.P95GenerationMs < report.Overview.P50GenerationMs) throw new Exception("p95 must be >= p50");
                            if (report.Stages.Count != 1 || report.Stages[0].Stage != "final_inference") throw new Exception("Expected one aggregated stage");
                            if (report.Timeseries.Count < 1) throw new Exception("Expected at least one timeseries bucket");
                        }),

                    new TestCaseDescriptor("Database", "Subject_PendingDeletion_Enumeration", "EnumeratePendingDeletion returns only Pending/Deleting subjects, across tenants",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Tenant t = await db.Tenants.CreateAsync(new Tenant { Name = "Del" }, ct);
                            Subject live = await db.Subjects.CreateAsync(new Subject { TenantId = t.Id, DisplayName = "Live", UrlSlug = "live" }, ct);
                            Subject pending = await db.Subjects.CreateAsync(new Subject { TenantId = t.Id, DisplayName = "Pending", UrlSlug = "pending" }, ct);
                            pending.DeletionStatus = SubjectDeletionStatusEnum.Pending;
                            await db.Subjects.UpdateAsync(pending, ct);

                            List<Subject> due = await db.Subjects.EnumeratePendingDeletionAsync(ct);
                            if (!due.Exists(x => x.Id == pending.Id)) throw new Exception("A Pending subject must be enumerated for deletion");
                            if (due.Exists(x => x.Id == live.Id)) throw new Exception("A live (None) subject must not be enumerated for deletion");
                        })
                });
        }
    }
}
