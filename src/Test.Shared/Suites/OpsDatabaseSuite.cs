namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Provider-agnostic contract suite for the operations repositories: model runners, request history, and
    /// prompts. Each case covers the positive round-trip and the matching negative paths (missing id, unknown
    /// natural key, filtered enumeration, summary, and prune).
    /// </summary>
    public static class OpsDatabaseSuite
    {
        /// <summary>Build the operations database contract suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "OpsDb",
                displayName: "Ops Database Contract",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("OpsDb", "ModelRunner_Crud_And_ByName", "Model runners round-trip and resolve by name; an unknown name reads null",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            ModelRunner runner = await db.ModelRunners.CreateAsync(new ModelRunner
                            {
                                Name = "local-ollama",
                                Provider = ModelRunnerProviderEnum.Ollama,
                                BaseUrl = "http://127.0.0.1:11434",
                                Capabilities = new List<ModelCapabilityEnum> { ModelCapabilityEnum.Completion },
                                Usage = ModelRunnerUsageEnum.UserPrompt,
                                DefaultModel = "llama3"
                            }, ct);

                            if ((await db.ModelRunners.ReadAsync(runner.Id, ct))?.Name != "local-ollama") throw new Exception("read by id failed");
                            if ((await db.ModelRunners.ReadByNameAsync(null, "local-ollama", ct))?.Id != runner.Id) throw new Exception("read by name failed");
                            if (await db.ModelRunners.ReadByNameAsync(null, "nope", ct) != null) throw new Exception("unknown runner name should read null");
                            if (!(await db.ModelRunners.EnumerateAsync(null, ct)).Exists(r => r.Id == runner.Id)) throw new Exception("enumeration should include the runner");

                            runner.DefaultModel = "llama3.1";
                            await db.ModelRunners.UpdateAsync(runner, ct);
                            if ((await db.ModelRunners.ReadAsync(runner.Id, ct))?.DefaultModel != "llama3.1") throw new Exception("update did not persist");

                            if (!await db.ModelRunners.DeleteAsync(runner.Id, ct)) throw new Exception("delete should return true");
                            if (await db.ModelRunners.ReadAsync(runner.Id, ct) != null) throw new Exception("deleted runner should read null");
                        }),

                    new TestCaseDescriptor("OpsDb", "RequestHistory_Crud_Enumerate_Summary_Prune", "Request-history entries round-trip, enumerate/summarize by filter, and prune by cutoff",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            RequestHistoryEntry entry = new RequestHistoryEntry
                            {
                                TenantId = "ten_a",
                                Method = "GET",
                                Path = "/v1.0/subjects",
                                Url = "http://127.0.0.1/v1.0/subjects",
                                StatusCode = 200
                            };
                            await db.RequestHistory.CreateAsync(entry, ct);

                            if ((await db.RequestHistory.ReadAsync("ten_a", entry.Id, ct))?.Path != "/v1.0/subjects") throw new Exception("read failed");
                            if (await db.RequestHistory.ReadAsync("ten_a", "req_missing", ct) != null) throw new Exception("missing entry should read null");

                            RequestHistoryFilter filter = new RequestHistoryFilter { TenantId = "ten_a", PageNumber = 1, PageSize = 25 };
                            RequestHistoryPage page = await db.RequestHistory.EnumerateAsync(filter, ct);
                            if (!page.Items.Exists(e => e.Id == entry.Id) || page.TotalCount < 1) throw new Exception("filtered enumeration should include the entry with a total count");

                            RequestHistorySummary summary = await db.RequestHistory.SummarizeAsync(filter, ct);
                            if (summary == null) throw new Exception("summary should not be null");

                            RequestHistoryFilter otherTenant = new RequestHistoryFilter { TenantId = "ten_b", PageNumber = 1, PageSize = 25 };
                            if ((await db.RequestHistory.EnumerateAsync(otherTenant, ct)).Items.Exists(e => e.Id == entry.Id)) throw new Exception("entry must not leak across tenants");

                            int pruned = await db.RequestHistory.PruneAsync(DateTime.UtcNow.AddMinutes(1), ct);
                            if (pruned < 1) throw new Exception("prune should remove the entry");
                            if (await db.RequestHistory.ReadAsync("ten_a", entry.Id, ct) != null) throw new Exception("pruned entry should read null");
                        }),

                    new TestCaseDescriptor("OpsDb", "Prompt_Crud_And_ByKey", "Prompts round-trip and resolve by key; an unknown key reads null",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Prompt prompt = await db.Prompts.CreateAsync(new Prompt { Key = "test.custom", Name = "Custom", Content = "hello", Version = 1 }, ct);

                            if ((await db.Prompts.ReadAsync(prompt.Id, ct))?.Content != "hello") throw new Exception("read by id failed");
                            if ((await db.Prompts.ReadByKeyAsync(null, "test.custom", ct))?.Id != prompt.Id) throw new Exception("read by key failed");
                            if (await db.Prompts.ReadByKeyAsync(null, "test.nope", ct) != null) throw new Exception("unknown prompt key should read null");
                            if (!(await db.Prompts.EnumerateAsync(null, ct)).Exists(p => p.Id == prompt.Id)) throw new Exception("enumeration should include the prompt");

                            prompt.Content = "hello v2";
                            prompt.Version = 2;
                            await db.Prompts.UpdateAsync(prompt, ct);
                            Prompt updated = await db.Prompts.ReadAsync(prompt.Id, ct) ?? throw new Exception("read after update failed");
                            if (updated.Content != "hello v2" || updated.Version != 2) throw new Exception("update did not persist");

                            if (!await db.Prompts.DeleteAsync(prompt.Id, ct)) throw new Exception("delete should return true");
                            if (await db.Prompts.ReadAsync(prompt.Id, ct) != null) throw new Exception("deleted prompt should read null");
                        })
                });
        }
    }
}
