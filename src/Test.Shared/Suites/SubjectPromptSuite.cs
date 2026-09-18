namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Server.Services;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Provider-agnostic contract suite for per-subject prompt overrides and the prompt resolver: CRUD
    /// round-trip, global fallback, Append and Replace merge, legacy-column addendum, and revert-on-delete.
    /// </summary>
    public static class SubjectPromptSuite
    {
        /// <summary>Build the subject-prompt suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "SubjectPrompt",
                displayName: "Subject Prompt Overrides",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("SubjectPrompt", "Crud_RoundTrip", "Per-subject prompt overrides upsert, read, enumerate, and delete",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            SubjectPrompt sp = await db.SubjectPrompts.UpsertAsync(new SubjectPrompt
                            {
                                TenantId = "ten_x",
                                SubjectId = "sub_1",
                                PromptKey = "cell.summarize",
                                Content = "override text",
                                MergeMode = PromptMergeModeEnum.Append
                            }, ct);

                            SubjectPrompt? read = await db.SubjectPrompts.ReadAsync("ten_x", "sub_1", "cell.summarize", ct);
                            if (read == null || read.Content != "override text") throw new Exception("read after upsert failed");

                            // Upsert again for the same (tenant, subject, key) must replace, not duplicate.
                            await db.SubjectPrompts.UpsertAsync(new SubjectPrompt { TenantId = "ten_x", SubjectId = "sub_1", PromptKey = "cell.summarize", Content = "v2", MergeMode = PromptMergeModeEnum.Replace }, ct);
                            List<SubjectPrompt> all = await db.SubjectPrompts.EnumerateBySubjectAsync("ten_x", "sub_1", ct);
                            if (all.Count != 1) throw new Exception("upsert must not create a duplicate row");
                            if (all[0].Content != "v2" || all[0].MergeMode != PromptMergeModeEnum.Replace) throw new Exception("upsert did not replace");

                            if (await db.SubjectPrompts.ReadAsync("ten_x", "sub_other", "cell.summarize", ct) != null) throw new Exception("override must not leak across subjects");

                            if (!await db.SubjectPrompts.DeleteAsync("ten_x", "sub_1", "cell.summarize", ct)) throw new Exception("delete should return true");
                            if (await db.SubjectPrompts.ReadAsync("ten_x", "sub_1", "cell.summarize", ct) != null) throw new Exception("deleted override should read null");
                        }),

                    new TestCaseDescriptor("SubjectPrompt", "Resolver_Fallback_Merge_Legacy", "The resolver falls back to global, applies Append/Replace, and appends the legacy addendum",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            await db.Prompts.CreateAsync(new Prompt { TenantId = "ten_x", Key = "resolver.key", Name = "Resolver", Content = "GLOBAL", Version = 1 }, ct);
                            PromptResolver resolver = new PromptResolver(db);

                            ResolvedPrompt fallback = await resolver.ResolveAsync("ten_x", "sub_1", "resolver.key", null, ct);
                            if (fallback.EffectiveContent != "GLOBAL" || fallback.Source != PromptSourceEnum.Global) throw new Exception("global fallback failed");

                            await db.SubjectPrompts.UpsertAsync(new SubjectPrompt { TenantId = "ten_x", SubjectId = "sub_1", PromptKey = "resolver.key", Content = "EXTRA", MergeMode = PromptMergeModeEnum.Append }, ct);
                            ResolvedPrompt appended = await resolver.ResolveAsync("ten_x", "sub_1", "resolver.key", null, ct);
                            if (appended.EffectiveContent != "GLOBAL\n\nEXTRA" || appended.Source != PromptSourceEnum.SubjectOverride) throw new Exception("append merge failed");

                            await db.SubjectPrompts.UpsertAsync(new SubjectPrompt { TenantId = "ten_x", SubjectId = "sub_1", PromptKey = "resolver.key", Content = "ONLY", MergeMode = PromptMergeModeEnum.Replace }, ct);
                            ResolvedPrompt replaced = await resolver.ResolveAsync("ten_x", "sub_1", "resolver.key", null, ct);
                            if (replaced.EffectiveContent != "ONLY") throw new Exception("replace merge failed");

                            await db.SubjectPrompts.DeleteAsync("ten_x", "sub_1", "resolver.key", ct);
                            ResolvedPrompt legacy = await resolver.ResolveAsync("ten_x", "sub_1", "resolver.key", "LEGACY", ct);
                            if (legacy.EffectiveContent != "GLOBAL\n\nLEGACY" || legacy.Source != PromptSourceEnum.SubjectOverride) throw new Exception("legacy addendum failed");

                            ResolvedPrompt reverted = await resolver.ResolveAsync("ten_x", "sub_1", "resolver.key", null, ct);
                            if (reverted.EffectiveContent != "GLOBAL" || reverted.Source != PromptSourceEnum.Global) throw new Exception("delete should revert to global");
                        })
                });
        }
    }
}
