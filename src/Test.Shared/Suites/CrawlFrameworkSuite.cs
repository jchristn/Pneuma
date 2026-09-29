namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Crawling;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Models;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// The crawl plan framework with an in-memory source: first runs add, unchanged runs do nothing, changed objects
    /// re-ingest, failures retry, deletions are opt-in and guarded, schedules compute across daylight saving, two
    /// schedulers claim a plan once, interrupted runs recover, and old operations are pruned.
    /// </summary>
    public static class CrawlFrameworkSuite
    {
        /// <summary>Build the suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "CrawlFramework",
                displayName: "Crawl plan and sync framework",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("CrawlFramework", "FirstRun_AddsEveryObject", "The first run creates a crawl link, a job, and a tracked object for each object, and succeeds",
                        executeAsync: async ct =>
                        {
                            await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                            {
                                Seed(rig, 3);
                                CrawlPlan plan = await rig.CreatePlanAsync(null, ct);
                                CrawlOperation op = await rig.RunToEndAsync(plan.Id, ct);
                                Expect(op.Status == CrawlOperationStatusEnum.Succeeded, "status Succeeded, got " + op.Status + " " + op.Error);
                                Expect(op.Enumerated == 3 && op.Added == 3 && op.Failed == 0, "3 enumerated and added, got " + op.Enumerated + "/" + op.Added + "/" + op.Failed);
                                List<SubjectLink> links = await rig.H.Db.SubjectLinks.EnumerateByCrawlPlanAsync(rig.H.TenantId, plan.Id, ct);
                                Expect(links.Count == 3, "3 links, got " + links.Count);
                                Expect(links.All(l => l.SourceKind == SourceKindEnum.Crawl && l.Status == SubjectLinkStatusEnum.Ingested), "links are crawled and ingested: " + String.Join(",", links.Select(l => l.SourceKind + "/" + l.Status)));
                                Expect(links.All(l => l.ExternalKey != null && l.ExternalKey.StartsWith("crawl:" + plan.Id + ":")), "links carry the plan-scoped key");
                                List<CrawlObject> objects = await rig.H.Db.CrawlObjects.EnumerateByPlanAsync(rig.H.TenantId, plan.Id, ct);
                                Expect(objects.Count == 3 && objects.All(o => o.Status == CrawlObjectStatusEnum.Active && o.LinkId != null), "3 active tracked objects");
                                CrawlPlan after = await rig.H.Db.CrawlPlans.ReadAsync(rig.H.TenantId, plan.Id, ct) ?? throw new Exception("plan gone");
                                Expect(after.Status == CrawlPlanStatusEnum.Idle && after.LastSuccessUtc != null && after.LastOperationId == op.Id, "plan idle with a last success");
                            }
                        }),

                    new TestCaseDescriptor("CrawlFramework", "UnchangedRun_AddsNothing", "A second run over an unchanged source queues no jobs and counts every object unchanged",
                        executeAsync: async ct =>
                        {
                            await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                            {
                                Seed(rig, 3);
                                CrawlPlan plan = await rig.CreatePlanAsync(null, ct);
                                await rig.RunToEndAsync(plan.Id, ct);
                                int jobsBefore = (await rig.H.Db.IngestionJobs.EnumerateAsync(rig.H.TenantId, null, ct)).Count;
                                CrawlOperation second = await rig.RunToEndAsync(plan.Id, ct);
                                int jobsAfter = (await rig.H.Db.IngestionJobs.EnumerateAsync(rig.H.TenantId, null, ct)).Count;
                                Expect(second.Status == CrawlOperationStatusEnum.Succeeded && second.Unchanged == 3 && second.Added == 0 && second.Updated == 0, "3 unchanged, got " + second.Unchanged + " added " + second.Added);
                                Expect(jobsAfter == jobsBefore, "no new jobs, had " + jobsBefore + " now " + jobsAfter);
                            }
                        }),

                    new TestCaseDescriptor("CrawlFramework", "ChangedToken_ReingestsSameLink", "A changed version token re-ingests the object into the same link",
                        executeAsync: async ct =>
                        {
                            await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                            {
                                Seed(rig, 2);
                                CrawlPlan plan = await rig.CreatePlanAsync(null, ct);
                                await rig.RunToEndAsync(plan.Id, ct);
                                string linkBefore = (await LinkForAsync(rig, plan.Id, "doc-0", ct)).Id;
                                rig.Crawler.Set("doc-0", "Changed content for doc 0.", "v2");
                                CrawlOperation op = await rig.RunToEndAsync(plan.Id, ct);
                                Expect(op.Updated == 1 && op.Unchanged == 1 && op.Status == CrawlOperationStatusEnum.Succeeded, "1 updated 1 unchanged, got " + op.Updated + "/" + op.Unchanged + " " + op.Status);
                                SubjectLink after = await LinkForAsync(rig, plan.Id, "doc-0", ct);
                                Expect(after.Id == linkBefore, "the same link is re-ingested");
                                Expect((await rig.H.Db.SubjectLinks.EnumerateByCrawlPlanAsync(rig.H.TenantId, plan.Id, ct)).Count == 2, "still 2 links");
                            }
                        }),

                    new TestCaseDescriptor("CrawlFramework", "Deletion_WhenEnabled", "With deletions on, an object gone from the source has its link marked for deletion and stops being tracked",
                        executeAsync: async ct =>
                        {
                            await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                            {
                                Seed(rig, 4);
                                CrawlPlan plan = await rig.CreatePlanAsync(p => { p.ProcessDeletions = true; p.MaxDeletionFraction = 0.5; }, ct);
                                await rig.RunToEndAsync(plan.Id, ct);
                                SubjectLink gone = await LinkForAsync(rig, plan.Id, "doc-3", ct);
                                rig.Crawler.Remove("doc-3");
                                CrawlOperation op = await rig.RunToEndAsync(plan.Id, ct);
                                Expect(op.Deleted == 1 && op.Status == CrawlOperationStatusEnum.Succeeded, "1 deleted, got " + op.Deleted + " " + op.Status);
                                SubjectLink? link = await rig.H.Db.SubjectLinks.ReadAsync(rig.H.TenantId, gone.Id, ct);
                                Expect(link != null && link.DeletionStatus == LinkDeletionStatusEnum.Pending, "link marked for deletion");
                                List<CrawlObject> objects = await rig.H.Db.CrawlObjects.EnumerateByPlanAsync(rig.H.TenantId, plan.Id, ct);
                                Expect(objects.Count == 3 && objects.All(o => o.ExternalKey != "doc-3"), "the object is no longer tracked");
                            }
                        }),

                    new TestCaseDescriptor("CrawlFramework", "DeletionsOff_MarksMissing", "With deletions off (the default), an object gone from the source is marked Missing and its link is kept",
                        executeAsync: async ct =>
                        {
                            await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                            {
                                Seed(rig, 2);
                                CrawlPlan plan = await rig.CreatePlanAsync(null, ct);
                                await rig.RunToEndAsync(plan.Id, ct);
                                rig.Crawler.Remove("doc-1");
                                CrawlOperation op = await rig.RunToEndAsync(plan.Id, ct);
                                Expect(op.Missing == 1 && op.Deleted == 0, "1 missing 0 deleted, got " + op.Missing + "/" + op.Deleted);
                                SubjectLink link = await LinkForAsync(rig, plan.Id, "doc-1", ct);
                                Expect(link.DeletionStatus == LinkDeletionStatusEnum.None, "link kept");
                                CrawlObject obj = (await rig.H.Db.CrawlObjects.EnumerateByPlanAsync(rig.H.TenantId, plan.Id, ct)).First(o => o.ExternalKey == "doc-1");
                                Expect(obj.Status == CrawlObjectStatusEnum.Missing, "object Missing, got " + obj.Status);
                            }
                        }),

                    new TestCaseDescriptor("CrawlFramework", "DeletionGuard_HoldsThenConfirms", "Deleting more than the limit holds the run; confirming runs the deletions",
                        executeAsync: async ct =>
                        {
                            await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                            {
                                Seed(rig, 4);
                                CrawlPlan plan = await rig.CreatePlanAsync(p => { p.ProcessDeletions = true; p.MaxDeletionFraction = 0.2; }, ct);
                                await rig.RunToEndAsync(plan.Id, ct);
                                rig.Crawler.Remove("doc-2");
                                rig.Crawler.Remove("doc-3");
                                CrawlOperation held = await rig.RunToEndAsync(plan.Id, ct);
                                Expect(held.Status == CrawlOperationStatusEnum.Held && held.HeldDeletions == 2 && held.Deleted == 0, "held with 2 deletions, got " + held.Status + " " + held.HeldDeletions);
                                SubjectLink kept = await LinkForAsync(rig, plan.Id, "doc-2", ct);
                                Expect(kept.DeletionStatus == LinkDeletionStatusEnum.None, "nothing deleted while held");
                                CrawlPlan idle = await rig.H.Db.CrawlPlans.ReadAsync(rig.H.TenantId, plan.Id, ct) ?? throw new Exception("plan gone");
                                Expect(idle.Status == CrawlPlanStatusEnum.Idle, "a held run releases the plan");

                                int deleted = await rig.Sync.ConfirmDeletionsAsync(held, ct);
                                CrawlOperation confirmed = await rig.H.Db.CrawlOperations.ReadAsync(rig.H.TenantId, held.Id, ct) ?? throw new Exception("op gone");
                                Expect(deleted == 2 && confirmed.Status == CrawlOperationStatusEnum.Succeeded && confirmed.Deleted == 2 && confirmed.HeldDeletions == 0, "confirmed 2 deletions, got " + deleted + " " + confirmed.Status);
                                SubjectLink? after = await rig.H.Db.SubjectLinks.ReadAsync(rig.H.TenantId, kept.Id, ct);
                                Expect(after != null && after.DeletionStatus == LinkDeletionStatusEnum.Pending, "link marked for deletion after confirmation");

                                bool refused = false;
                                try { await rig.Sync.ConfirmDeletionsAsync(confirmed, ct); } catch (InvalidOperationException) { refused = true; }
                                Expect(refused, "confirming a finished operation is refused");
                            }
                        }),

                    new TestCaseDescriptor("CrawlFramework", "FailedObject_RetriedNextRun", "An object whose ingestion failed makes the run PartiallySucceeded and is retried next run even when unchanged",
                        executeAsync: async ct =>
                        {
                            await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                            {
                                Seed(rig, 3);
                                rig.Crawler.FailOpen["doc-1"] = true;
                                CrawlPlan plan = await rig.CreatePlanAsync(null, ct);
                                CrawlOperation first = await rig.RunToEndAsync(plan.Id, ct);
                                Expect(first.Status == CrawlOperationStatusEnum.PartiallySucceeded && first.Failed == 1, "partially succeeded with 1 failure, got " + first.Status + " " + first.Failed);
                                CrawlObject failed = (await rig.H.Db.CrawlObjects.EnumerateByPlanAsync(rig.H.TenantId, plan.Id, ct)).First(o => o.ExternalKey == "doc-1");
                                Expect(failed.Status == CrawlObjectStatusEnum.Failed && !String.IsNullOrEmpty(failed.LastError), "object marked Failed with its error");
                                List<CrawlOperationObject> records = await rig.H.Db.CrawlOperations.EnumerateObjectsAsync(rig.H.TenantId, first.Id, ct);
                                Expect(records.Count(r => r.Succeeded == false) == 1 && records.Count(r => r.Succeeded == true) == 2, "per-object outcomes recorded");

                                rig.Crawler.FailOpen.Clear();
                                CrawlOperation second = await rig.RunToEndAsync(plan.Id, ct);
                                Expect(second.Retried == 1 && second.Unchanged == 2 && second.Status == CrawlOperationStatusEnum.Succeeded, "1 retried and succeeded, got " + second.Retried + " " + second.Status);
                            }
                        }),

                    new TestCaseDescriptor("CrawlFramework", "EnumerationFailure_FailsWithoutDeleting", "A source that cannot be listed fails the run and deletes nothing, even with deletions on",
                        executeAsync: async ct =>
                        {
                            await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                            {
                                Seed(rig, 2);
                                CrawlPlan plan = await rig.CreatePlanAsync(p => { p.ProcessDeletions = true; p.MaxDeletionFraction = 1.0; }, ct);
                                await rig.RunToEndAsync(plan.Id, ct);
                                rig.Crawler.FailEnumeration = "source offline";
                                CrawlOperation op = await rig.RunToEndAsync(plan.Id, ct);
                                Expect(op.Status == CrawlOperationStatusEnum.Failed && op.Error != null && op.Error.Contains("source offline"), "failed with the reason, got " + op.Status + " " + op.Error);
                                List<SubjectLink> links = await rig.H.Db.SubjectLinks.EnumerateByCrawlPlanAsync(rig.H.TenantId, plan.Id, ct);
                                Expect(links.All(l => l.DeletionStatus == LinkDeletionStatusEnum.None), "no links deleted");
                            }
                        }),

                    new TestCaseDescriptor("CrawlFramework", "LabelsAndTags_ReachChunks", "Plan labels and tags are stamped on crawled links and reach their indexed chunks",
                        executeAsync: async ct =>
                        {
                            await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                            {
                                Seed(rig, 1);
                                CrawlPlan plan = await rig.CreatePlanAsync(p => { p.Labels = new List<string> { "crawled" }; p.Tags = new Dictionary<string, string> { { "origin", "fake-site" } }; }, ct);
                                await rig.RunToEndAsync(plan.Id, ct);
                                SubjectLink link = await LinkForAsync(rig, plan.Id, "doc-0", ct);
                                Expect(link.Labels.Contains("crawled") && link.Tags.TryGetValue("origin", out string? o) && o == "fake-site", "link carries the plan's labels and tags");
                                Expect(rig.H.Recall.AllDocumentTags().Any(t => t.TryGetValue("origin", out string? v) && v == "fake-site"), "a chunk carries the plan's tag");
                            }
                        }),

                    new TestCaseDescriptor("CrawlFramework", "Filter_SkipsAndLimits", "Include, exclude, content-type, and object-limit filters skip objects with a reason",
                        executeAsync: ct =>
                        {
                            CrawlPlan plan = new CrawlPlan { Type = CrawlPlanTypeEnum.Web };
                            plan.Filter.ExcludePatterns = new List<string> { "*/private/*" };
                            plan.Filter.AllowedContentTypes = new List<string> { "text/html" };
                            plan.Filter.MaxObjects = 2;
                            List<CrawledObject> listed = new List<CrawledObject>
                            {
                                new CrawledObject { Key = "https://a/private/x", ContentType = "text/html" },
                                new CrawledObject { Key = "https://a/doc.pdf", ContentType = "application/pdf" },
                                new CrawledObject { Key = "https://a/1", ContentType = "text/html; charset=utf-8" },
                                new CrawledObject { Key = "https://a/2", ContentType = "text/html" },
                                new CrawledObject { Key = "https://a/3", ContentType = "text/html" },
                                new CrawledObject { Key = "https://a/3", ContentType = "text/html" },
                                new CrawledObject { Key = "https://a/folder/", IsFolder = true }
                            };
                            CrawlDelta delta = CrawlDeltaPlanner.Compute(plan, listed, new List<CrawlObject>());
                            Expect(delta.Enumerated == 5, "duplicates and folders are not counted, got " + delta.Enumerated);
                            Expect(delta.Count(CrawlActionEnum.Add) == 2 && delta.Count(CrawlActionEnum.Skip) == 3, "2 added 3 skipped, got " + delta.Count(CrawlActionEnum.Add) + "/" + delta.Count(CrawlActionEnum.Skip));
                            Expect(delta.Items.First(i => i.Key == "https://a/3").Detail!.Contains("limit"), "the third page is over the limit");
                            Expect(CrawlFilterMatcher.GlobMatch("*.PDF", "https://a/x.pdf") && !CrawlFilterMatcher.GlobMatch("a?c", "abbc"), "glob matching");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("CrawlFramework", "Schedule_IntervalAndCronAcrossDst", "Interval and cron next runs are computed, with cron times staying local across a daylight saving change",
                        executeAsync: ct =>
                        {
                            CrawlPlan interval = new CrawlPlan { Schedule = new CrawlSchedule { Type = CrawlScheduleTypeEnum.Interval, IntervalMinutes = 60 } };
                            DateTime from = new DateTime(2026, 3, 6, 12, 0, 0, DateTimeKind.Utc);
                            Expect(CrawlScheduleCalculator.NextRunUtc(interval, from) == from.AddMinutes(60), "interval adds its minutes");

                            CrawlPlan cron = new CrawlPlan { Schedule = new CrawlSchedule { Type = CrawlScheduleTypeEnum.Cron, CronExpression = "0 3 * * *", TimeZone = "America/New_York" } };
                            DateTime? beforeDst = CrawlScheduleCalculator.NextRunUtc(cron, from);
                            DateTime? afterDst = CrawlScheduleCalculator.NextRunUtc(cron, new DateTime(2026, 3, 8, 12, 0, 0, DateTimeKind.Utc));
                            Expect(beforeDst == new DateTime(2026, 3, 7, 8, 0, 0, DateTimeKind.Utc), "03:00 EST is 08:00 UTC, got " + beforeDst);
                            Expect(afterDst == new DateTime(2026, 3, 9, 7, 0, 0, DateTimeKind.Utc), "03:00 EDT is 07:00 UTC, got " + afterDst);

                            CrawlPlan manual = new CrawlPlan();
                            Expect(CrawlScheduleCalculator.NextRunUtc(manual, from) == null, "manual plans have no next run");
                            CrawlPlan disabled = new CrawlPlan { Enabled = false, Schedule = new CrawlSchedule { Type = CrawlScheduleTypeEnum.Interval, IntervalMinutes = 60 } };
                            Expect(CrawlScheduleCalculator.NextRunUtc(disabled, from) == null, "disabled plans have no next run");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("CrawlFramework", "Schedule_InvalidRejected", "An invalid cron expression, a short or long interval, and an unknown time zone are rejected",
                        executeAsync: ct =>
                        {
                            Expect(CrawlScheduleCalculator.Validate(new CrawlSchedule { Type = CrawlScheduleTypeEnum.Cron, CronExpression = "every day" }).Count == 1, "bad cron");
                            Expect(CrawlScheduleCalculator.Validate(new CrawlSchedule { Type = CrawlScheduleTypeEnum.Cron }).Count == 1, "missing cron");
                            Expect(CrawlScheduleCalculator.Validate(new CrawlSchedule { Type = CrawlScheduleTypeEnum.Interval, IntervalMinutes = 1 }).Count == 1, "short interval");
                            Expect(CrawlScheduleCalculator.Validate(new CrawlSchedule { Type = CrawlScheduleTypeEnum.Interval, IntervalMinutes = 600000 }).Count == 1, "long interval");
                            Expect(CrawlScheduleCalculator.Validate(new CrawlSchedule { TimeZone = "Mars/Olympus" }).Count == 1, "unknown zone");
                            Expect(CrawlScheduleCalculator.Validate(new CrawlSchedule { Type = CrawlScheduleTypeEnum.Cron, CronExpression = "*/15 * * * *", TimeZone = "Europe/Berlin" }).Count == 0, "valid cron");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("CrawlFramework", "DuePlan_RunsOnSchedule", "A scheduler pass starts an enabled plan whose next run is due and moves its next run forward",
                        executeAsync: async ct =>
                        {
                            await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                            {
                                Seed(rig, 1);
                                CrawlPlan plan = await rig.CreatePlanAsync(p => { p.Schedule = new CrawlSchedule { Type = CrawlScheduleTypeEnum.Interval, IntervalMinutes = 60 }; }, ct);
                                plan.NextRunUtc = DateTime.UtcNow.AddMinutes(-1);
                                await rig.H.Db.CrawlPlans.UpdateRunStateAsync(plan, ct);
                                rig.Settings.SchedulerEnabled = true;
                                await rig.Scheduler.RunPassAsync(ct);
                                await rig.Scheduler.WaitAsync(rig.H.TenantId, plan.Id);
                                List<CrawlOperation> ops = await rig.H.Db.CrawlOperations.EnumerateByPlanAsync(rig.H.TenantId, plan.Id, ct);
                                Expect(ops.Count == 1 && ops[0].Trigger == CrawlTriggerEnum.Schedule, "one scheduled operation, got " + ops.Count);
                                CrawlPlan after = await rig.H.Db.CrawlPlans.ReadAsync(rig.H.TenantId, plan.Id, ct) ?? throw new Exception("plan gone");
                                Expect(after.NextRunUtc != null && after.NextRunUtc > DateTime.UtcNow.AddMinutes(50), "next run moved about an hour ahead, got " + after.NextRunUtc);
                            }
                        }),

                    new TestCaseDescriptor("CrawlFramework", "TwoSchedulers_ClaimOnce", "Two schedulers starting the same plan at once: exactly one claims it, the other gets 409",
                        executeAsync: async ct =>
                        {
                            await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                            {
                                Seed(rig, 1);
                                CrawlPlan plan = await rig.CreatePlanAsync(null, ct);
                                rig.Crawler.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                                using (CrawlSchedulerService other = rig.NewScheduler())
                                {
                                    Task<CrawlStartResult> a = rig.Scheduler.StartAsync(rig.H.TenantId, plan.Id, CrawlTriggerEnum.Manual, ct);
                                    Task<CrawlStartResult> b = other.StartAsync(rig.H.TenantId, plan.Id, CrawlTriggerEnum.Schedule, ct);
                                    CrawlStartResult[] results = await Task.WhenAll(a, b);
                                    Expect(results.Count(r => r.StatusCode == 202) == 1 && results.Count(r => r.StatusCode == 409) == 1, "one 202 and one 409, got " + String.Join(",", results.Select(r => r.StatusCode)));
                                    rig.Crawler.Gate.TrySetResult(true);
                                    await rig.Scheduler.WaitAsync(rig.H.TenantId, plan.Id);
                                    await other.WaitAsync(rig.H.TenantId, plan.Id);
                                }
                                Expect((await rig.H.Db.CrawlOperations.EnumerateByPlanAsync(rig.H.TenantId, plan.Id, ct)).Count == 1, "one operation");
                            }
                        }),

                    new TestCaseDescriptor("CrawlFramework", "Stop_CancelsRunningOperation", "Stopping a running operation cancels it and releases the plan; stopping an idle plan is 409",
                        executeAsync: async ct =>
                        {
                            await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                            {
                                Seed(rig, 1);
                                CrawlPlan plan = await rig.CreatePlanAsync(null, ct);
                                rig.Crawler.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                                CrawlStartResult started = await rig.Scheduler.StartAsync(rig.H.TenantId, plan.Id, CrawlTriggerEnum.Manual, ct);
                                await rig.Crawler.EnumerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
                                CrawlStartResult stopped = await rig.Scheduler.StopAsync(rig.H.TenantId, plan.Id, ct);
                                Expect(stopped.StatusCode == 202, "stop accepted, got " + stopped.StatusCode);
                                await rig.Scheduler.WaitAsync(rig.H.TenantId, plan.Id);
                                CrawlOperation op = await rig.H.Db.CrawlOperations.ReadAsync(rig.H.TenantId, started.Operation!.Id, ct) ?? throw new Exception("op gone");
                                Expect(op.Status == CrawlOperationStatusEnum.Cancelled && op.FinishedUtc != null, "cancelled, got " + op.Status);
                                CrawlPlan after = await rig.H.Db.CrawlPlans.ReadAsync(rig.H.TenantId, plan.Id, ct) ?? throw new Exception("plan gone");
                                Expect(after.Status == CrawlPlanStatusEnum.Idle, "plan released, got " + after.Status);
                                Expect((await rig.Scheduler.StopAsync(rig.H.TenantId, plan.Id, ct)).StatusCode == 409, "stopping an idle plan is 409");
                                Expect((await rig.Scheduler.StartAsync(rig.H.TenantId, "cpl_missing", CrawlTriggerEnum.Manual, ct)).StatusCode == 404, "unknown plan is 404");
                            }
                        }),

                    new TestCaseDescriptor("CrawlFramework", "StartupRecovery_FailsInterruptedRun", "A run left Running by a stopped server is marked Failed and its plan released once the claim lapses",
                        executeAsync: async ct =>
                        {
                            await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                            {
                                CrawlPlan plan = await rig.CreatePlanAsync(null, ct);
                                bool claimed = await rig.H.Db.CrawlPlans.TryClaimAsync(rig.H.TenantId, plan.Id, "dead-server", DateTime.UtcNow.AddMinutes(-1), ct);
                                Expect(claimed, "claim taken");
                                CrawlOperation op = await rig.H.Db.CrawlOperations.CreateAsync(new CrawlOperation { TenantId = rig.H.TenantId, PlanId = plan.Id, SubjectId = plan.SubjectId, Status = CrawlOperationStatusEnum.Running }, ct);
                                plan.Status = CrawlPlanStatusEnum.Running;
                                plan.LastOperationId = op.Id;
                                await rig.H.Db.CrawlPlans.UpdateRunStateAsync(plan, ct);

                                using (CrawlSchedulerService restarted = rig.NewScheduler())
                                {
                                    await restarted.RecoverAsync(ct);
                                }
                                CrawlOperation recovered = await rig.H.Db.CrawlOperations.ReadAsync(rig.H.TenantId, op.Id, ct) ?? throw new Exception("op gone");
                                Expect(recovered.Status == CrawlOperationStatusEnum.Failed && recovered.Error != null, "interrupted run failed, got " + recovered.Status);
                                CrawlPlan after = await rig.H.Db.CrawlPlans.ReadAsync(rig.H.TenantId, plan.Id, ct) ?? throw new Exception("plan gone");
                                Expect(after.Status == CrawlPlanStatusEnum.Idle && after.ClaimExpiresUtc == null, "plan released");
                            }
                        }),

                    new TestCaseDescriptor("CrawlFramework", "LiveClaim_NotRecovered", "A run whose claim has not lapsed is left alone by recovery",
                        executeAsync: async ct =>
                        {
                            await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                            {
                                CrawlPlan plan = await rig.CreatePlanAsync(null, ct);
                                await rig.H.Db.CrawlPlans.TryClaimAsync(rig.H.TenantId, plan.Id, "live-server", DateTime.UtcNow.AddMinutes(30), ct);
                                await rig.Scheduler.RecoverAsync(ct);
                                CrawlPlan after = await rig.H.Db.CrawlPlans.ReadAsync(rig.H.TenantId, plan.Id, ct) ?? throw new Exception("plan gone");
                                Expect(after.Status == CrawlPlanStatusEnum.Running, "still running, got " + after.Status);
                                Expect(!await rig.H.Db.CrawlPlans.TryClaimAsync(rig.H.TenantId, plan.Id, "another", DateTime.UtcNow.AddMinutes(30), ct), "a live claim cannot be taken");
                            }
                        }),

                    new TestCaseDescriptor("CrawlFramework", "Pruning_RemovesOldOperations", "Finished operations older than the plan's retention are pruned after a run",
                        executeAsync: async ct =>
                        {
                            await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                            {
                                Seed(rig, 1);
                                CrawlPlan plan = await rig.CreatePlanAsync(p => p.OperationRetentionDays = 7, ct);
                                CrawlOperation old = await rig.H.Db.CrawlOperations.CreateAsync(new CrawlOperation
                                {
                                    TenantId = rig.H.TenantId, PlanId = plan.Id, SubjectId = plan.SubjectId, Status = CrawlOperationStatusEnum.Succeeded,
                                    StartedUtc = DateTime.UtcNow.AddDays(-10), FinishedUtc = DateTime.UtcNow.AddDays(-10)
                                }, ct);
                                await rig.H.Db.CrawlOperations.CreateObjectsAsync(new List<CrawlOperationObject> { new CrawlOperationObject { TenantId = rig.H.TenantId, OperationId = old.Id, ExternalKey = "x", Action = CrawlActionEnum.Add, Succeeded = true } }, ct);
                                await rig.RunToEndAsync(plan.Id, ct);
                                Expect(await rig.H.Db.CrawlOperations.ReadAsync(rig.H.TenantId, old.Id, ct) == null, "old operation pruned");
                                Expect((await rig.H.Db.CrawlOperations.EnumerateObjectsAsync(rig.H.TenantId, old.Id, ct)).Count == 0, "its objects pruned");
                                Expect((await rig.H.Db.CrawlOperations.EnumerateByPlanAsync(rig.H.TenantId, plan.Id, ct)).Count == 1, "the new operation is kept");
                            }
                        }),

                    new TestCaseDescriptor("CrawlFramework", "Preview_ChangesNothing", "A preview reports what a run would do and creates no links, jobs, operations, or objects",
                        executeAsync: async ct =>
                        {
                            await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                            {
                                Seed(rig, 3);
                                CrawlPlan plan = await rig.CreatePlanAsync(null, ct);
                                CrawlPlan withSecrets = await rig.Plans.ReadWithSecretsAsync(rig.H.TenantId, plan.Id, ct) ?? throw new Exception("plan gone");
                                CrawlPreview preview = await rig.Sync.PreviewAsync(withSecrets, true, ct);
                                Expect(preview.Enumerated == 3 && preview.Add == 3 && preview.Items.Count == 3, "3 would be added, got " + preview.Add);
                                Expect((await rig.H.Db.SubjectLinks.EnumerateByCrawlPlanAsync(rig.H.TenantId, plan.Id, ct)).Count == 0, "no links");
                                Expect((await rig.H.Db.CrawlOperations.EnumerateByPlanAsync(rig.H.TenantId, plan.Id, ct)).Count == 0, "no operations");
                                Expect((await rig.H.Db.CrawlObjects.EnumerateByPlanAsync(rig.H.TenantId, plan.Id, ct)).Count == 0, "no tracked objects");
                            }
                        }),

                    new TestCaseDescriptor("CrawlFramework", "Secrets_EncryptedAndNeverRead", "Secrets are stored encrypted, absent from the stored plan, and decrypted only for the crawler",
                        executeAsync: async ct =>
                        {
                            await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                            {
                                CrawlPlan plan = await rig.CreatePlanAsync(p => { p.Web!.Authentication = "Basic"; p.Web.Username = "reader"; p.Web.Password = "s3cret-pw"; }, ct);
                                Expect(plan.Web!.Password == null && plan.SecretsSet.SequenceEqual(new[] { "password" }), "stored plan has no secret value and lists it as set");
                                Dictionary<string, string> stored = await rig.H.Db.CrawlPlans.ReadSecretsAsync(rig.H.TenantId, plan.Id, ct);
                                Expect(stored.ContainsKey("password") && !stored["password"].Contains("s3cret-pw"), "stored as ciphertext");
                                CrawlPlan read = await rig.H.Db.CrawlPlans.ReadAsync(rig.H.TenantId, plan.Id, ct) ?? throw new Exception("plan gone");
                                Expect(read.Web!.Password == null && read.Web.Username == "reader", "reads never carry the secret");
                                CrawlPlan run = await rig.Plans.ReadWithSecretsAsync(rig.H.TenantId, plan.Id, ct) ?? throw new Exception("plan gone");
                                Expect(run.Web!.Password == "s3cret-pw", "the crawler gets the decrypted secret");

                                CrawlPlan incoming = new CrawlPlan { TenantId = plan.TenantId, SubjectId = plan.SubjectId, Name = "Renamed", Type = CrawlPlanTypeEnum.Web, Web = new WebCrawlSettings { StartUrls = new List<string> { "https://fake.example/" }, Authentication = "Basic", Username = "reader" } };
                                CrawlPlanSaveResult kept = await rig.Plans.UpdateAsync(read, incoming, null, ct);
                                Expect(kept.ChangedSecrets.Count == 0 && kept.Plan.SecretsSet.Contains("password"), "an empty secret keeps the stored value");
                                CrawlPlan current = await rig.H.Db.CrawlPlans.ReadAsync(rig.H.TenantId, plan.Id, ct) ?? throw new Exception("plan gone");
                                CrawlPlanSaveResult cleared = await rig.Plans.UpdateAsync(current, incoming, new List<string> { "password" }, ct);
                                Expect(cleared.ChangedSecrets.SequenceEqual(new[] { "password" }) && cleared.Plan.SecretsSet.Count == 0, "clearSecrets removes it");
                            }
                        }),

                    new TestCaseDescriptor("CrawlFramework", "Validation_RejectsBadPlans", "Missing settings, a missing required field, an out-of-range value, a bad choice, and an unavailable type are rejected",
                        executeAsync: async ct =>
                        {
                            await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                            {
                                CrawlPlan noSettings = new CrawlPlan { Name = "x", Type = CrawlPlanTypeEnum.Web };
                                Expect(rig.Plans.Validate(noSettings, null).Any(e => e.Contains("web settings")), "missing settings");
                                CrawlPlan noUrls = new CrawlPlan { Name = "x", Type = CrawlPlanTypeEnum.Web, Web = new WebCrawlSettings() };
                                Expect(rig.Plans.Validate(noUrls, null).Any(e => e.Contains("web.startUrls is required")), "missing start URLs: " + String.Join(" ", rig.Plans.Validate(noUrls, null)));
                                CrawlPlan deep = new CrawlPlan { Name = "x", Type = CrawlPlanTypeEnum.Web, Web = new WebCrawlSettings { StartUrls = new List<string> { "https://a/" }, MaxDepth = 99, Scope = "Everywhere" } };
                                List<string> errors = rig.Plans.Validate(deep, null);
                                Expect(errors.Any(e => e.Contains("maxDepth")) && errors.Any(e => e.Contains("scope")), "range and choice: " + String.Join(" ", errors));
                                CrawlPlan nfs = new CrawlPlan { Name = "x", Type = CrawlPlanTypeEnum.Nfs, Nfs = new NfsCrawlSettings() };
                                Expect(rig.Plans.Validate(nfs, null).Any(e => e.Contains("No crawler")), "unavailable type");
                                CrawlPlan unnamed = new CrawlPlan { Type = CrawlPlanTypeEnum.Web, Web = new WebCrawlSettings { StartUrls = new List<string> { "https://a/" } } };
                                Expect(rig.Plans.Validate(unnamed, null).Any(e => e.Contains("name")), "name required");
                            }
                        }),

                    new TestCaseDescriptor("CrawlFramework", "SettingsCodec_RoundTrips", "Typed settings, filter lists, labels, and tags round-trip through stored rows; empty lists stay empty; secrets are never rows",
                        executeAsync: ct =>
                        {
                            CrawlPlan plan = new CrawlPlan
                            {
                                Type = CrawlPlanTypeEnum.Web,
                                Web = new WebCrawlSettings { StartUrls = new List<string> { "https://a/", "https://b/" }, MaxDepth = 5, FollowLinks = false, DropQueryParameters = new List<string>(), Password = "pw", UserAgent = null },
                                Labels = new List<string> { "l1", "l2" },
                                Tags = new Dictionary<string, string> { { "team", "docs" } }
                            };
                            plan.Filter.IncludePatterns = new List<string> { "https://a/*" };
                            List<CrawlPlanSetting> rows = CrawlSettingsCodec.ToRows(plan);
                            Expect(!rows.Any(r => r.Name == "password"), "secrets are not rows");
                            CrawlPlan back = new CrawlPlan { Type = CrawlPlanTypeEnum.Web };
                            CrawlSettingsCodec.ApplyRows(back, rows);
                            Expect(back.Web!.StartUrls.SequenceEqual(plan.Web.StartUrls) && back.Web.MaxDepth == 5 && !back.Web.FollowLinks, "scalars and lists round-trip");
                            Expect(back.Web.DropQueryParameters.Count == 0, "an explicit empty list stays empty, not the default");
                            Expect(back.Web.UserAgent == null && back.Web.Password == null, "nulls stay null");
                            Expect(back.Labels.SequenceEqual(plan.Labels) && back.Tags["team"] == "docs" && back.Filter.IncludePatterns.SequenceEqual(plan.Filter.IncludePatterns), "labels, tags, and filters round-trip");
                            List<CrawlSettingField> fields = CrawlSettingsCodec.Describe(typeof(WebCrawlSettings));
                            Expect(fields.First(f => f.Name == "startUrls").Kind == "list" && fields.First(f => f.Name == "startUrls").Required, "list field");
                            Expect(fields.First(f => f.Name == "password").Kind == "secret" && fields.First(f => f.Name == "password").Default == null, "secret field has no default");
                            Expect(fields.First(f => f.Name == "scope").Kind == "choice" && fields.First(f => f.Name == "scope").Options.Contains("SameHost"), "choice field");
                            Expect(fields.First(f => f.Name == "maxDepth").Max == 20, "range");
                            Expect(CrawlSettingsCodec.SettingsProperty(CrawlPlanTypeEnum.S3) == "s3" && CrawlSettingsCodec.SettingsProperty(CrawlPlanTypeEnum.Cifs) == "cifs", "settings property names");
                            Expect(CrawlKeys.LinkExternalKey("cpl_x", new string('k', 400)).Length == 256, "long keys are hashed to fit");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("CrawlFramework", "DeletingSubject_RemovesPlans", "Deleting a subject with its subordinates removes its crawl plans, objects, and operations",
                        executeAsync: async ct =>
                        {
                            await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                            {
                                Seed(rig, 1);
                                CrawlPlan plan = await rig.CreatePlanAsync(null, ct);
                                CrawlOperation op = await rig.RunToEndAsync(plan.Id, ct);
                                await rig.H.Db.Subjects.DeleteWithSubordinatesAsync(rig.H.TenantId, rig.H.SubjectId, new List<string>(), new List<string>(), ct);
                                Expect(await rig.H.Db.CrawlPlans.ReadAsync(rig.H.TenantId, plan.Id, ct) == null, "plan deleted");
                                Expect(await rig.H.Db.CrawlOperations.ReadAsync(rig.H.TenantId, op.Id, ct) == null, "operation deleted");
                                Expect((await rig.H.Db.CrawlObjects.EnumerateByPlanAsync(rig.H.TenantId, plan.Id, ct)).Count == 0, "objects deleted");
                            }
                        })
                });
        }

        private static void Seed(CrawlTestRig rig, int count)
        {
            for (int i = 0; i < count; i++)
            {
                rig.Crawler.Set("doc-" + i, "Document " + i + " explains topic " + i + " in a few sentences. It has enough text to become a chunk.", "v1");
            }
        }

        private static async Task<SubjectLink> LinkForAsync(CrawlTestRig rig, string planId, string key, CancellationToken ct)
        {
            List<SubjectLink> links = await rig.H.Db.SubjectLinks.EnumerateByCrawlPlanAsync(rig.H.TenantId, planId, ct);
            return links.FirstOrDefault(l => l.ExternalKey == CrawlKeys.LinkExternalKey(planId, key)) ?? throw new Exception("no link for " + key);
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
