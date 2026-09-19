namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Server.Services;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Provider-agnostic contract suite for the ingestion-tuning singleton, per-subject concurrency-override
    /// persistence (JSON round-trip), and the runtime ConcurrencyManager's effective-value resolution and
    /// gate acquisition.
    /// </summary>
    public static class ConcurrencyOverridesSuite
    {
        /// <summary>Build the concurrency-overrides suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "ConcurrencyOverrides",
                displayName: "Ingestion Concurrency Overrides",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("ConcurrencyOverrides", "IngestionTuning_Upsert_RoundTrip_And_Clamp", "The ingestion-tuning singleton upserts, reads back, and clamps out-of-range values",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);

                            await db.IngestionTuning.UpsertAsync(new IngestionTuning { Summarization = 12, MaxConcurrentTasks = 7, SummarizationMinCellLength = 200, StageTimeoutSeconds = 111, ClassificationBatchSize = 40, ClassificationBatchOverlap = 5, ClassificationBatchConcurrency = 6 }, ct);
                            IngestionTuning? read = await db.IngestionTuning.ReadAsync(ct);
                            if (read == null) throw new Exception("tuning read null after upsert");
                            if (read.Summarization != 12 || read.MaxConcurrentTasks != 7 || read.SummarizationMinCellLength != 200 || read.StageTimeoutSeconds != 111) throw new Exception("tuning did not round-trip");
                            if (read.ClassificationBatchSize != 40 || read.ClassificationBatchOverlap != 5 || read.ClassificationBatchConcurrency != 6) throw new Exception("classification batching tuning did not round-trip");

                            // A second upsert replaces the singleton (does not create a second row) and clamps.
                            await db.IngestionTuning.UpsertAsync(new IngestionTuning { Summarization = 9999, MaxConcurrentTasks = 0, ClassificationBatchSize = 0, ClassificationBatchConcurrency = 9999 }, ct);
                            IngestionTuning? read2 = await db.IngestionTuning.ReadAsync(ct);
                            if (read2 == null) throw new Exception("tuning read null after second upsert");
                            if (read2.Summarization != 256) throw new Exception("Summarization not clamped to 256");
                            if (read2.MaxConcurrentTasks != 1) throw new Exception("MaxConcurrentTasks not clamped to 1");
                            if (read2.ClassificationBatchSize != 1) throw new Exception("ClassificationBatchSize not clamped to 1");
                            if (read2.ClassificationBatchConcurrency != 64) throw new Exception("ClassificationBatchConcurrency not clamped to 64");
                        }),

                    new TestCaseDescriptor("ConcurrencyOverrides", "Subject_Overrides_Json_RoundTrip", "A subject's concurrency overrides serialize to/from JSON with only the set fields surviving",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);

                            Subject subject = new Subject { TenantId = "ten_x", DisplayName = "Overridden Subject" };
                            subject.ConcurrencyOverrides = new SubjectConcurrencyOverrides { Summarization = 16, Embedding = 8 };
                            Subject created = await db.Subjects.CreateAsync(subject, ct);

                            Subject? read = await db.Subjects.ReadByIdAsync(created.Id, ct);
                            if (read == null) throw new Exception("subject read null after create");
                            SubjectConcurrencyOverrides? overrides = read.GetConcurrencyOverrides();
                            if (overrides == null) throw new Exception("overrides null after round-trip");
                            if (overrides.Summarization != 16 || overrides.Embedding != 8) throw new Exception("set override fields did not round-trip");
                            if (overrides.Classification != null || overrides.StageTimeoutSeconds != null || overrides.ContentRetrieval != null) throw new Exception("unset override fields must stay null");

                            // A subject with no overrides persists a null JSON column and reads back as no overrides.
                            Subject plain = await db.Subjects.CreateAsync(new Subject { TenantId = "ten_x", DisplayName = "Plain Subject" }, ct);
                            Subject? readPlain = await db.Subjects.ReadByIdAsync(plain.Id, ct);
                            if (readPlain == null) throw new Exception("plain subject read null");
                            if (readPlain.GetConcurrencyOverrides() != null) throw new Exception("subject without overrides should read null");

                            // Enumeration used by the ConcurrencyManager returns only subjects that carry overrides.
                            List<Subject> withOverrides = await db.Subjects.EnumerateWithConcurrencyOverridesAsync(ct);
                            bool sawOverridden = false;
                            bool sawPlain = false;
                            foreach (Subject s in withOverrides)
                            {
                                if (s.Id == created.Id) sawOverridden = true;
                                if (s.Id == plain.Id) sawPlain = true;
                            }
                            if (!sawOverridden) throw new Exception("subject with overrides missing from enumeration");
                            if (sawPlain) throw new Exception("subject without overrides must not appear in the overrides enumeration");
                        }),

                    new TestCaseDescriptor("ConcurrencyOverrides", "ConcurrencyManager_Effective_And_Acquire", "The ConcurrencyManager resolves effective values (override else default) and acquires/releases gated and ungated stages",
                        executeAsync: async ct =>
                        {
                            ConcurrencyManager manager = new ConcurrencyManager(new IngestionTuning());
                            string subjectId = "sub_hot";

                            // No override -> system defaults.
                            if (manager.EffectiveSummarizationConcurrency(subjectId) != 4) throw new Exception("default summarization concurrency wrong");
                            if (manager.EffectiveStageTimeoutSeconds(subjectId) != 900) throw new Exception("default stage timeout wrong");
                            if (manager.EffectiveSummarizationMinCellLength(subjectId) != 128) throw new Exception("default min cell length wrong");
                            if (manager.EffectiveClassificationBatchSize(subjectId) != 25) throw new Exception("default classification batch size wrong");
                            if (manager.EffectiveClassificationBatchOverlap(subjectId) != 3) throw new Exception("default classification batch overlap wrong");
                            if (manager.EffectiveClassificationBatchConcurrency(subjectId) != 4) throw new Exception("default classification batch concurrency wrong");

                            // Override several fields; the others still resolve to the default, and other subjects are unaffected.
                            manager.ApplySubjectOverride(subjectId, new SubjectConcurrencyOverrides { SummarizationConcurrency = 9, StageTimeoutSeconds = 42, ClassificationBatchSize = 50, ClassificationBatchConcurrency = 8 });
                            if (manager.EffectiveSummarizationConcurrency(subjectId) != 9) throw new Exception("override summarization concurrency not applied");
                            if (manager.EffectiveStageTimeoutSeconds(subjectId) != 42) throw new Exception("override stage timeout not applied");
                            if (manager.EffectiveSummarizationMinCellLength(subjectId) != 128) throw new Exception("unset override field should stay at default");
                            if (manager.EffectiveClassificationBatchSize(subjectId) != 50) throw new Exception("override classification batch size not applied");
                            if (manager.EffectiveClassificationBatchConcurrency(subjectId) != 8) throw new Exception("override classification batch concurrency not applied");
                            if (manager.EffectiveClassificationBatchOverlap(subjectId) != 3) throw new Exception("unset classification batch overlap should stay at default");
                            if (manager.EffectiveSummarizationConcurrency("other_subject") != 4) throw new Exception("override leaked to another subject");
                            if (manager.EffectiveClassificationBatchSize("other_subject") != 25) throw new Exception("batch override leaked to another subject");

                            // Clearing reverts to the system default.
                            manager.ClearSubjectOverride(subjectId);
                            if (manager.EffectiveSummarizationConcurrency(subjectId) != 4) throw new Exception("clear did not revert to default");

                            // A gated stage yields a real releasable handle; an ungated stage yields a no-op handle.
                            using (IDisposable gated = await manager.AcquireStageAsync(IngestionStageEnum.Summarization, subjectId, ct))
                            {
                                if (gated == null) throw new Exception("gated stage acquisition returned null");
                            }
                            using (IDisposable ungated = await manager.AcquireStageAsync(IngestionStageEnum.OntologyCanonicalization, subjectId, ct))
                            {
                                if (ungated == null) throw new Exception("ungated stage acquisition returned null");
                            }
                            using (IDisposable jobSlot = await manager.AcquireJobSlotAsync(ct))
                            {
                                if (jobSlot == null) throw new Exception("job-slot acquisition returned null");
                            }
                        })
                });
        }
    }
}
