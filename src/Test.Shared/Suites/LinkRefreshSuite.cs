namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Crawling.Crawlers;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Configuration;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Ingestion.Refresh;
    using Pneuma.Core.Integrations.Implementations;
    using Pneuma.Core.Models;
    using Pneuma.Core.Observability;
    using SyslogLogging;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Scheduled link refresh: due links are checked once with a conditional GET, a 304 moves the schedule without
    /// work, a change queues a Refresh job, failures back off and keep the current version, jitter spreads links, and
    /// links that are inactive, being deleted, pushed, or owned by a crawl plan are never refreshed.
    /// </summary>
    public static class LinkRefreshSuite
    {
        /// <summary>Build the suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "LinkRefresh",
                displayName: "Scheduled link refresh",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("LinkRefresh", "DueLink_CheckedOnceAndQueued", "A due link is checked once: the first check queues a Refresh job, stores the ETag, and moves the next refresh about one interval ahead",
                        executeAsync: async ct =>
                        {
                            await using (LinkRefreshRig rig = await LinkRefreshRig.CreateAsync(ct))
                            {
                                rig.Site.Html("/page", "<html><body>Version one</body></html>", "\"v1\"");
                                SubjectLink link = await rig.LinkAsync("/page", 60, ct);
                                List<LinkRefreshResult> first = await rig.Service.RunPassAsync(ct);
                                Expect(first.Count == 1 && first[0].Outcome == LinkRefreshOutcomeEnum.Queued && first[0].JobId != null, "queued once: " + Describe(first));
                                IngestionJob job = await rig.H.Db.IngestionJobs.ReadAsync(rig.H.TenantId, first[0].JobId!, ct) ?? throw new Exception("job missing");
                                Expect(job.Trigger == IngestionTriggerEnum.Refresh && job.LinkId == link.Id, "a Refresh job for the link");
                                SubjectLink after = await rig.ReadAsync(link.Id, ct);
                                Expect(after.SourceETag == "\"v1\"" && after.LastRefreshUtc != null && after.RefreshFailures == 0, "ETag and last refresh stored");
                                double minutes = (after.NextRefreshUtc!.Value - DateTime.UtcNow).TotalMinutes;
                                Expect(minutes > 50 && minutes < 70, "next refresh about an hour ahead, got " + minutes);
                                Expect(after.Status == SubjectLinkStatusEnum.Submitted, "the link is waiting for its job");
                                List<LinkRefreshResult> second = await rig.Service.RunPassAsync(ct);
                                Expect(second.Count == 0, "not checked again before it is due");
                                Expect(PneumaMetrics.Render().Contains("pneuma_link_refresh_total{outcome=\"Queued\"}"), "metric recorded");
                            }
                        }),

                    new TestCaseDescriptor("LinkRefresh", "NotModified_EndsEarly", "A 304 queues nothing, keeps the link as it is, and moves the next refresh",
                        executeAsync: async ct =>
                        {
                            await using (LinkRefreshRig rig = await LinkRefreshRig.CreateAsync(ct))
                            {
                                rig.Site.Html("/page", "<html><body>Same</body></html>", "\"v1\"");
                                SubjectLink link = await rig.LinkAsync("/page", 120, ct, l => { l.SourceETag = "\"v1\""; l.Status = SubjectLinkStatusEnum.Ingested; });
                                int jobsBefore = (await rig.H.Db.IngestionJobs.EnumerateByLinkAsync(rig.H.TenantId, link.Id, ct)).Count;
                                List<LinkRefreshResult> results = await rig.Service.RunPassAsync(ct);
                                Expect(results.Count == 1 && results[0].Outcome == LinkRefreshOutcomeEnum.Unchanged, "unchanged: " + Describe(results));
                                Expect((await rig.H.Db.IngestionJobs.EnumerateByLinkAsync(rig.H.TenantId, link.Id, ct)).Count == jobsBefore, "no job queued");
                                SubjectLink after = await rig.ReadAsync(link.Id, ct);
                                Expect(after.Status == SubjectLinkStatusEnum.Ingested && after.NextRefreshUtc > DateTime.UtcNow.AddMinutes(100), "status kept and schedule moved");
                            }
                        }),

                    new TestCaseDescriptor("LinkRefresh", "ChangedPage_Queued", "A page whose ETag changed is re-ingested and the new ETag is stored",
                        executeAsync: async ct =>
                        {
                            await using (LinkRefreshRig rig = await LinkRefreshRig.CreateAsync(ct))
                            {
                                rig.Site.Html("/page", "<html><body>Version two</body></html>", "\"v2\"");
                                SubjectLink link = await rig.LinkAsync("/page", 60, ct, l => { l.SourceETag = "\"v1\""; l.Status = SubjectLinkStatusEnum.Ingested; });
                                List<LinkRefreshResult> results = await rig.Service.RunPassAsync(ct);
                                Expect(results.Count == 1 && results[0].Outcome == LinkRefreshOutcomeEnum.Queued, "queued: " + Describe(results));
                                Expect((await rig.ReadAsync(link.Id, ct)).SourceETag == "\"v2\"", "new ETag stored");
                            }
                        }),

                    new TestCaseDescriptor("LinkRefresh", "FailedCheck_BacksOffAndKeepsVersion", "A failed check keeps the current version, queues nothing, and backs off 15 then 30 minutes",
                        executeAsync: async ct =>
                        {
                            await using (LinkRefreshRig rig = await LinkRefreshRig.CreateAsync(ct))
                            {
                                rig.Site.Set("/broken", new StubPage { Status = 500, ContentType = "text/plain", Body = System.Text.Encoding.UTF8.GetBytes("down") });
                                SubjectLink link = await rig.LinkAsync("/broken", 1440, ct, l => { l.Status = SubjectLinkStatusEnum.Ingested; l.ContentHash = "abc"; });
                                List<LinkRefreshResult> first = await rig.Service.RunPassAsync(ct);
                                Expect(first.Count == 1 && first[0].Outcome == LinkRefreshOutcomeEnum.Failed, "failed: " + Describe(first));
                                SubjectLink after = await rig.ReadAsync(link.Id, ct);
                                double minutes = (after.NextRefreshUtc!.Value - DateTime.UtcNow).TotalMinutes;
                                Expect(after.RefreshFailures == 1 && minutes > 13 && minutes < 16, "first back-off about 15 minutes, got " + minutes);
                                Expect(after.Status == SubjectLinkStatusEnum.Ingested && after.ContentHash == "abc", "current version kept");
                                Expect((await rig.H.Db.IngestionJobs.EnumerateByLinkAsync(rig.H.TenantId, link.Id, ct)).Count == 0, "no job");

                                after.NextRefreshUtc = DateTime.UtcNow.AddMinutes(-1);
                                await rig.H.Db.SubjectLinks.UpdateRefreshStateAsync(after, ct);
                                await rig.Service.RunPassAsync(ct);
                                SubjectLink again = await rig.ReadAsync(link.Id, ct);
                                double second = (again.NextRefreshUtc!.Value - DateTime.UtcNow).TotalMinutes;
                                Expect(again.RefreshFailures == 2 && second > 28 && second < 31, "second back-off about 30 minutes, got " + second);
                                Expect(LinkRefreshSchedule.Backoff(DateTime.UtcNow, 20, 60) <= DateTime.UtcNow.AddMinutes(60.1), "back-off never exceeds the interval");
                            }
                        }),

                    new TestCaseDescriptor("LinkRefresh", "Ineligible_NeverRefreshed", "Inactive, deleting, pushed, crawl-owned, and not-yet-due links are never checked; a busy link is postponed",
                        executeAsync: async ct =>
                        {
                            await using (LinkRefreshRig rig = await LinkRefreshRig.CreateAsync(ct))
                            {
                                rig.Site.Html("/page", "<html><body>x</body></html>", "\"v1\"");
                                await rig.LinkAsync("/page", 60, ct, l => l.Active = false);
                                await rig.LinkAsync("/page", 60, ct, l => l.DeletionStatus = LinkDeletionStatusEnum.Pending);
                                await rig.LinkAsync("/page", 60, ct, l => { l.SourceKind = SourceKindEnum.Inline; l.Url = "pneuma-inline://x"; });
                                await rig.LinkAsync("/page", 60, ct, l => l.CrawlPlanId = "cpl_x");
                                await rig.LinkAsync("/page", 60, ct, l => l.NextRefreshUtc = DateTime.UtcNow.AddHours(1));
                                SubjectLink busy = await rig.LinkAsync("/page", 60, ct, l => l.Status = SubjectLinkStatusEnum.Processing);
                                List<LinkRefreshResult> results = await rig.Service.RunPassAsync(ct);
                                Expect(results.Count == 1 && results[0].LinkId == busy.Id && results[0].Outcome == LinkRefreshOutcomeEnum.Busy, "only the busy link is looked at, and postponed: " + Describe(results));
                                Expect(rig.Site.Hits("/page") == 0, "the source was never contacted");
                                Expect((await rig.ReadAsync(busy.Id, ct)).NextRefreshUtc < DateTime.UtcNow.AddMinutes(16), "busy links are looked at again soon");
                            }
                        }),

                    new TestCaseDescriptor("LinkRefresh", "Claim_OnlyOnce", "Two servers claiming the same due link: only one wins",
                        executeAsync: async ct =>
                        {
                            await using (LinkRefreshRig rig = await LinkRefreshRig.CreateAsync(ct))
                            {
                                SubjectLink link = await rig.LinkAsync("/page", 60, ct);
                                Task<bool> a = rig.H.Db.SubjectLinks.TryClaimRefreshAsync(link.TenantId, link.Id, link.NextRefreshUtc, DateTime.UtcNow.AddMinutes(10).AddMilliseconds(3), ct);
                                Task<bool> b = rig.H.Db.SubjectLinks.TryClaimRefreshAsync(link.TenantId, link.Id, link.NextRefreshUtc, DateTime.UtcNow.AddMinutes(10).AddMilliseconds(7), ct);
                                bool[] won = await Task.WhenAll(a, b);
                                Expect(won.Count(w => w) == 1, "exactly one claim wins, got " + String.Join(",", won));
                            }
                        }),

                    new TestCaseDescriptor("LinkRefresh", "Schedule_JitterAndValidation", "Next refreshes spread within 10% of the interval; intervals under 60 minutes or over a year are rejected; 0 and null are allowed",
                        executeAsync: ct =>
                        {
                            DateTime now = DateTime.UtcNow;
                            List<DateTime> nexts = Enumerable.Range(0, 50).Select(i => LinkRefreshSchedule.Next(now, 1440)!.Value).ToList();
                            Expect(nexts.All(n => (n - now).TotalMinutes >= 1296 - 0.01 && (n - now).TotalMinutes <= 1584 + 0.01), "within 10%");
                            Expect(nexts.Distinct().Count() > 40, "spread out, got " + nexts.Distinct().Count() + " distinct");
                            Expect(LinkRefreshSchedule.Next(now, 0) == null, "0 is off");
                            Expect(LinkRefreshSchedule.Validate(30, "x") != null && LinkRefreshSchedule.Validate(600000, "x") != null, "out of range rejected");
                            Expect(LinkRefreshSchedule.Validate(0, "x") == null && LinkRefreshSchedule.Validate(null, "x") == null && LinkRefreshSchedule.Validate(60, "x") == null, "0, null, and 60 allowed");
                            SubjectLink inherits = new SubjectLink { Url = "https://a/" };
                            Expect(LinkRefreshSchedule.EffectiveInterval(inherits, new Subject { DefaultRefreshIntervalMinutes = 720 }) == 720, "null follows the subject default");
                            Expect(LinkRefreshSchedule.EffectiveInterval(new SubjectLink { Url = "https://a/", RefreshIntervalMinutes = 0 }, new Subject { DefaultRefreshIntervalMinutes = 720 }) == 0, "0 overrides the default");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("LinkRefresh", "Api_IntervalsDefaultsAndMcp", "Link intervals are set singly and in bulk, the subject default schedules inheriting links, invalid values are 400, pushed content is refused, and the MCP tool works",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await ApiClientHelper.LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                string linksUrl = server.BaseUrl + "/v1.0/subjects/" + subjectId + "/links";
                                ApiResult own = await ApiClientHelper.CallAsync(HttpMethod.Post, linksUrl, token, "{\"url\":\"https://example.com/a\",\"refreshIntervalMinutes\":120}", ct);
                                Expect(own, 201);
                                Check(own.Body.Contains("\"refreshIntervalMinutes\":120") && own.Body.Contains("\"nextRefreshUtc\":\"20"), "submit sets the interval and schedule: " + own.Body);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, linksUrl, token, "{\"url\":\"https://example.com/b\",\"refreshIntervalMinutes\":30}", ct), 400);
                                ApiResult inherit = await ApiClientHelper.CallAsync(HttpMethod.Post, linksUrl, token, "{\"url\":\"https://example.com/c\"}", ct);
                                string inheritId = ApiClientHelper.ExtractString(inherit.Body, "id");
                                Check(!inherit.Body.Contains("\"nextRefreshUtc\":\"20"), "no refresh without a default");

                                ApiResult subject = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/subjects/" + subjectId, token, null, ct);
                                string withDefault = subject.Body.Replace("\"defaultRefreshIntervalMinutes\":0", "\"defaultRefreshIntervalMinutes\":1440");
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Put, server.BaseUrl + "/v1.0/subjects/" + subjectId, token, withDefault, ct), 200);
                                ApiResult inherited = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/links/" + inheritId, token, null, ct);
                                Check(inherited.Body.Contains("\"nextRefreshUtc\":\"20"), "the subject default schedules inheriting links: " + inherited.Body);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Put, server.BaseUrl + "/v1.0/subjects/" + subjectId, token, subject.Body.Replace("\"defaultRefreshIntervalMinutes\":0", "\"defaultRefreshIntervalMinutes\":10"), ct), 400);

                                string ownId = ApiClientHelper.ExtractString(own.Body, "id");
                                ApiResult off = await ApiClientHelper.CallAsync(HttpMethod.Put, server.BaseUrl + "/v1.0/links/" + ownId, token, "{\"refreshIntervalMinutes\":0}", ct);
                                Expect(off, 200);
                                Check(off.Body.Contains("\"refreshIntervalMinutes\":0") && !off.Body.Contains("\"nextRefreshUtc\":\"20"), "0 turns it off: " + off.Body);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Put, server.BaseUrl + "/v1.0/links/" + ownId, token, "{\"refreshIntervalMinutes\":59}", ct), 400);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Put, server.BaseUrl + "/v1.0/links/" + ownId, token, "{}", ct), 400);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Put, server.BaseUrl + "/v1.0/links/lnk_missing", token, "{\"refreshIntervalMinutes\":60}", ct), 404);

                                ApiResult bulk = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/links/refresh-interval", token, "{\"ids\":[\"" + ownId + "\",\"" + inheritId + "\",\"lnk_missing\"],\"refreshIntervalMinutes\":10080}", ct);
                                Expect(bulk, 200);
                                Check(bulk.Body.Contains("\"updated\":2") && bulk.Body.Contains("lnk_missing"), "bulk updates and reports skips: " + bulk.Body);

                                ApiResult pushed = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/content", token, "{\"content\":\"x\",\"contentType\":\"text/plain\"}", ct);
                                Expect(pushed, 201);
                                string pushedId = String.Empty;
                                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(pushed.Body))
                                {
                                    pushedId = doc.RootElement.GetProperty("link").GetProperty("id").GetString() ?? String.Empty;
                                }
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Put, server.BaseUrl + "/v1.0/links/" + pushedId, token, "{\"refreshIntervalMinutes\":60}", ct), 400);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/links/" + pushedId + "/refresh", token, null, ct), 400);

                                string mcp = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_set_link_refresh\",\"arguments\":{\"id\":\"" + ownId + "\",\"useSubjectDefault\":true}}}";
                                ApiResult tool = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/mcp", token, mcp, ct);
                                Check(tool.Body.Contains("effectiveIntervalMinutes") && tool.Body.Contains("1440"), "MCP tool follows the default: " + tool.Body);
                                string bad = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_set_link_refresh\",\"arguments\":{\"id\":\"" + ownId + "\",\"refreshIntervalMinutes\":5}}}";
                                Check((await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/mcp", token, bad, ct)).Body.Contains("\"error\""), "MCP rejects a short interval");
                            }
                        })
                });
        }

        private static string Describe(List<LinkRefreshResult> results)
        {
            return String.Join("; ", results.Select(r => r.LinkId + ":" + r.Outcome + " " + r.Message));
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void Expect(ApiResult result, int status)
        {
            if (result.StatusCode != status) throw new Exception("expected " + status + ", got " + result.StatusCode + " " + result.Body);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
