namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Crawling;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// The crawl plan REST surface against a loopback server with a fake Web crawler: the type catalog, CRUD with
    /// write-only secrets, request-history redaction, connectivity tests and previews that change nothing, start and
    /// stop with 409s, validation errors, RBAC denials with audit, and cross-tenant isolation.
    /// </summary>
    public static class CrawlApiSuite
    {
        private const string _Password = "s3cret-crawl-pw";

        /// <summary>Build the suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "CrawlApi",
                displayName: "Crawl plan REST API",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("CrawlApi", "Types_ListRegisteredWithSchema", "The type catalog lists registered crawlers with their settings schema",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await StartAsync(ct))
                            {
                                string token = await AdminAsync(server, ct);
                                ApiResult types = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/crawl-plan-types", token, null, ct);
                                Expect(types, 200);
                                Check(types.Body.Contains("\"type\":\"Web\"") && types.Body.Contains("\"settingsProperty\":\"web\"") && types.Body.Contains("\"name\":\"startUrls\""), "catalog has the Web schema: " + types.Body);
                                Check(types.Body.Contains("\"kind\":\"secret\""), "secret fields are marked");
                            }
                        }),

                    new TestCaseDescriptor("CrawlApi", "Crud_SecretsNeverReturned", "Create, read, list, and update never return a secret; request history stores it redacted; a secret change is audited",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await StartAsync(ct))
                            {
                                string token = await AdminAsync(server, ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                ApiResult created = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/crawl-plans", token, PlanBody("Docs", _Password), ct);
                                Expect(created, 201);
                                Check(!created.Body.Contains(_Password) && created.Body.Contains("\"secretsSet\":[\"password\"]"), "create hides the secret: " + created.Body);
                                string planId = ApiClientHelper.ExtractString(created.Body, "id");

                                ApiResult read = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/crawl-plans/" + planId, token, null, ct);
                                Expect(read, 200);
                                Check(!read.Body.Contains(_Password) && read.Body.Contains("\"username\":\"reader\""), "read hides the secret");
                                ApiResult list = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/crawl-plans", token, null, ct);
                                Check(!list.Body.Contains(_Password) && list.Body.Contains(planId), "list hides the secret");
                                ApiResult bySubject = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/crawl-plans", token, null, ct);
                                Check(bySubject.Body.Contains(planId), "subject list has the plan");

                                ApiResult updated = await ApiClientHelper.CallAsync(HttpMethod.Put, server.BaseUrl + "/v1.0/crawl-plans/" + planId, token, PlanBody("Docs renamed", null), ct);
                                Expect(updated, 200);
                                Check(updated.Body.Contains("Docs renamed") && updated.Body.Contains("\"secretsSet\":[\"password\"]"), "an update without the secret keeps it");

                                bool redacted = false;
                                for (int attempt = 0; attempt < 30 && !redacted; attempt++)
                                {
                                    RequestHistoryPage page = await server.Database.RequestHistory.EnumerateAsync(new RequestHistoryFilter { PathContains = "/crawl-plans", PageSize = 50 }, ct);
                                    foreach (RequestHistoryEntry entry in page.Items.Where(e => e.Method == "POST"))
                                    {
                                        RequestHistoryEntry? full = await server.Database.RequestHistory.ReadAsync(null, entry.Id, ct);
                                        if (full?.RequestBody == null) continue;
                                        Check(!full.RequestBody.Contains(_Password), "request history must not store the secret: " + full.RequestBody);
                                        if (full.RequestBody.Contains("***redacted***")) redacted = true;
                                    }
                                    if (!redacted) await Task.Delay(100, ct);
                                }
                                Check(redacted, "the captured create body shows the secret redacted");

                                List<AuditRecord> audit = await server.Database.Audit.EnumerateAsync(null, 500, ct);
                                Check(audit.Any(a => a.EventType == AuditEventTypeEnum.CrawlPlanSecurityChanged && a.ResourceId == planId && (a.DenialReason ?? "").Contains("password") && !(a.DenialReason ?? "").Contains(_Password)), "the secret change is audited by name only");
                            }
                        }),

                    new TestCaseDescriptor("CrawlApi", "DraftTest_And_Preview_ChangeNothing", "Testing a draft saves nothing; a preview lists what would change and creates nothing",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await StartAsync(ct))
                            {
                                FakeCrawler crawler = Register(server);
                                crawler.Set("a", "Alpha page text.", "v1");
                                crawler.Set("b", "Beta page text.", "v1");
                                string token = await AdminAsync(server, ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);

                                ApiResult draft = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/crawl-plans/test", token, PlanBody("Draft", _Password), ct);
                                Expect(draft, 200);
                                Check(draft.Body.Contains("\"success\":true") && draft.Body.Contains("\"layers\""), "draft test reports its layers: " + draft.Body);
                                Check(crawler.LastPlan?.Web?.Password == _Password, "a draft's secret reaches the crawler");
                                ApiResult none = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/crawl-plans", token, null, ct);
                                Check(none.Body.Contains("\"totalRecords\":0") || !none.Body.Contains("cpl_"), "the draft test saved nothing: " + none.Body);

                                string planId = await CreatePlanAsync(server, token, subjectId, ct);
                                ApiResult test = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/crawl-plans/" + planId + "/test", token, null, ct);
                                Expect(test, 200);
                                Check(crawler.LastPlan?.Web?.Password == _Password, "a stored plan's secret is decrypted for the crawler");

                                ApiResult preview = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/crawl-plans/" + planId + "/preview", token, null, ct);
                                Expect(preview, 200);
                                Check(preview.Body.Contains("\"enumerated\":2") && preview.Body.Contains("\"add\":2"), "preview counts: " + preview.Body);
                                ApiResult ops = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/crawl-plans/" + planId + "/operations", token, null, ct);
                                Check(!ops.Body.Contains("cop_"), "the preview started no operation");
                                List<SubjectLink> links = await server.Database.SubjectLinks.EnumerateByCrawlPlanAsync(ApiTenant(server), planId, ct);
                                Check(links.Count == 0, "the preview created no links");

                                crawler.FailEnumeration = "unreachable";
                                ApiResult failed = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/crawl-plans/" + planId + "/preview", token, null, ct);
                                Expect(failed, 502);
                            }
                        }),

                    new TestCaseDescriptor("CrawlApi", "StartStop_And_Conflicts", "Start returns 202, a second start 409, stop 202 and cancels, and stop on an idle plan 409; a running plan cannot be deleted",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await StartAsync(ct))
                            {
                                FakeCrawler crawler = Register(server);
                                crawler.Set("a", "Alpha page text.", "v1");
                                crawler.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                                string token = await AdminAsync(server, ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                string planId = await CreatePlanAsync(server, token, subjectId, ct);

                                ApiResult started = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/crawl-plans/" + planId + "/start", token, null, ct);
                                Expect(started, 202);
                                string opId = ApiClientHelper.ExtractString(started.Body, "id");
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/crawl-plans/" + planId + "/start", token, null, ct), 409);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Delete, server.BaseUrl + "/v1.0/crawl-plans/" + planId, token, null, ct), 409);
                                await crawler.EnumerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/crawl-plans/" + planId + "/stop", token, null, ct), 202);
                                await server.CrawlScheduler.WaitAsync(ApiTenant(server), planId);
                                ApiResult op = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/crawl-operations/" + opId, token, null, ct);
                                Check(op.Body.Contains("\"status\":\"Cancelled\""), "the operation is cancelled: " + op.Body);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/crawl-plans/" + planId + "/stop", token, null, ct), 409);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/crawl-operations/" + opId + "/confirm-deletions", token, null, ct), 409);
                            }
                        }),

                    new TestCaseDescriptor("CrawlApi", "RunEndToEnd_OperationsAndObjects", "A started run is visible through the operation, per-object results, and the plan's tracked objects; deleting the plan with deleteLinks marks its links",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await StartAsync(ct))
                            {
                                FakeCrawler crawler = Register(server);
                                crawler.Set("a", "Alpha page text.", "v1");
                                crawler.Set("b", "Beta page text.", "v1");
                                string token = await AdminAsync(server, ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                string planId = await CreatePlanAsync(server, token, subjectId, ct);
                                ApiResult started = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/crawl-plans/" + planId + "/start", token, null, ct);
                                Expect(started, 202);
                                string opId = ApiClientHelper.ExtractString(started.Body, "id");
                                await server.CrawlScheduler.WaitAsync(ApiTenant(server), planId);

                                ApiResult op = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/crawl-operations/" + opId, token, null, ct);
                                Check(op.Body.Contains("\"added\":2") && op.Body.Contains("\"status\":\"Ingesting\""), "operation dispatched 2 adds and waits on ingestion: " + op.Body);
                                ApiResult objs = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/crawl-operations/" + opId + "/objects?action=Add", token, null, ct);
                                Check(objs.Body.Contains("\"externalKey\":\"a\"") && objs.Body.Contains("\"jobId\":\"job_"), "per-object results: " + objs.Body);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/crawl-operations/" + opId + "/objects?action=Bogus", token, null, ct), 400);
                                ApiResult tracked = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/crawl-plans/" + planId + "/objects?status=Active", token, null, ct);
                                Check(tracked.Body.Contains("\"externalKey\":\"b\""), "tracked objects: " + tracked.Body);
                                ApiResult byStatus = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/crawl-operations?planId=" + planId + "&status=Ingesting", token, null, ct);
                                Check(byStatus.Body.Contains(opId), "operations filter by plan and status");
                                ApiResult links = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/links?maxResults=100", token, null, ct);
                                Check(links.Body.Contains("\"sourceKind\":\"Crawl\"") && links.Body.Contains("\"crawlPlanId\":\"" + planId + "\""), "crawled links appear on the subject");

                                // A crawl-managed key cannot be overwritten by pushed content.
                                string crawlKey = CrawlKeys.LinkExternalKey(planId, "a");
                                ApiResult push = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/content", token,
                                    "{\"content\":\"x\",\"contentType\":\"text/plain\",\"externalKey\":\"" + crawlKey + "\"}", ct);
                                Expect(push, 409);

                                // The operation is still Ingesting (no worker in this server), so stop it before deleting the plan.
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/crawl-plans/" + planId + "/stop", token, null, ct), 202);
                                ApiResult deleted = await ApiClientHelper.CallAsync(HttpMethod.Delete, server.BaseUrl + "/v1.0/crawl-plans/" + planId + "?deleteLinks=true", token, null, ct);
                                Expect(deleted, 200);
                                Check(deleted.Body.Contains("\"linksDeleted\":2"), "links marked for deletion: " + deleted.Body);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/crawl-plans/" + planId, token, null, ct), 404);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/crawl-operations/" + opId, token, null, ct), 404);
                            }
                        }),

                    new TestCaseDescriptor("CrawlApi", "Validation_Returns400", "Invalid cron, a short interval, missing settings, a changed type, an unregistered type, and an unconfigured subject are 400; unknown ids are 404",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await StartAsync(ct))
                            {
                                Register(server);
                                string token = await AdminAsync(server, ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                string url = server.BaseUrl + "/v1.0/subjects/" + subjectId + "/crawl-plans";
                                string web = "\"web\":{\"startUrls\":[\"https://fake.example/\"]}";
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, url, token, "{\"name\":\"x\",\"type\":\"Web\"," + web + ",\"schedule\":{\"type\":\"Cron\",\"cronExpression\":\"daily at 3\"}}", ct), 400);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, url, token, "{\"name\":\"x\",\"type\":\"Web\"," + web + ",\"schedule\":{\"type\":\"Interval\",\"intervalMinutes\":1}}", ct), 400);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, url, token, "{\"name\":\"x\",\"type\":\"Web\"}", ct), 400);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, url, token, "{\"name\":\"x\",\"type\":\"Nfs\",\"nfs\":{\"host\":\"h\"}}", ct), 400);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, url, token, "{\"name\":\"x\",\"type\":\"Gopher\"}", ct), 400);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, url, token, null, ct), 400);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects/sub_missing/crawl-plans", token, PlanBody("x", null), ct), 404);

                                ApiResult bare = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects", token, "{\"displayName\":\"Unconfigured\",\"type\":\"Person\"}", ct);
                                string bareId = ApiClientHelper.ExtractString(bare.Body, "id");
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects/" + bareId + "/crawl-plans", token, PlanBody("x", null), ct), 400);

                                string planId = await CreatePlanAsync(server, token, subjectId, ct);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Put, server.BaseUrl + "/v1.0/crawl-plans/" + planId, token, "{\"name\":\"x\",\"type\":\"Sitemap\",\"sitemap\":{\"sitemapUrls\":[\"https://a/sitemap.xml\"]}}", ct), 400);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/crawl-plans/cpl_missing", token, null, ct), 404);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/crawl-plans/cpl_missing/start", token, null, ct), 404);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/crawl-operations/cop_missing", token, null, ct), 404);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/crawl-plans/" + planId + "/objects?status=Nope", token, null, ct), 400);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/crawl-plans", null, null, ct), 401);
                            }
                        }),

                    new TestCaseDescriptor("CrawlApi", "Rbac_DeniedAndAudited", "A user without crawl permissions is refused with 403 and the denial is audited",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await StartAsync(ct))
                            {
                                string token = await AdminAsync(server, ct);
                                ApiResult user = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/users", token, "{\"email\":\"crawler-less@pneuma\",\"password\":\"pw\",\"firstName\":\"No\",\"lastName\":\"Crawl\"}", ct);
                                Expect(user, 201);
                                string userToken = await ApiClientHelper.LoginAsync(server.BaseUrl, "crawler-less@pneuma", "pw", ct);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/crawl-plans", userToken, null, ct), 403);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/crawl-plans/cpl_x/start", userToken, null, ct), 403);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/crawl-operations", userToken, null, ct), 403);
                                List<AuditRecord> audit = await server.Database.Audit.EnumerateAsync(null, 500, ct);
                                Check(audit.Any(a => a.EventType == AuditEventTypeEnum.AuthorizationDenied && a.RequiredResourceType == ResourceTypeEnum.CrawlPlan), "the CrawlPlan denial is audited");
                                Check(audit.Any(a => a.EventType == AuditEventTypeEnum.AuthorizationDenied && a.RequiredResourceType == ResourceTypeEnum.CrawlOperation), "the CrawlOperation denial is audited");
                            }
                        }),

                    new TestCaseDescriptor("CrawlApi", "Mcp_CrawlTools", "The MCP crawl tools create, read, test, preview, start, stop, and enumerate, never return secrets, and reject bad input",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await StartAsync(ct))
                            {
                                FakeCrawler crawler = Register(server);
                                crawler.Set("a", "Alpha page text.", "v1");
                                crawler.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                                string token = await AdminAsync(server, ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);

                                ApiResult tools = await Mcp(server, token, "tools/list", null, ct);
                                Check(tools.Body.Contains("pneuma_create_crawl_plan") && tools.Body.Contains("pneuma_get_crawl_operation"), "tools listed");

                                string plan = "{\"name\":\"Mcp plan\",\"type\":\"Web\",\"web\":{\"startUrls\":[\"https://fake.example/\"],\"authentication\":\"Basic\",\"username\":\"reader\",\"password\":\"" + _Password + "\"}}";
                                ApiResult created = await Mcp(server, token, "pneuma_create_crawl_plan", "{\"subjectId\":\"" + subjectId + "\",\"plan\":" + plan + "}", ct);
                                Check(created.Body.Contains("cpl_") && !created.Body.Contains(_Password) && !created.Body.Contains("\"error\""), "created without the secret: " + created.Body);
                                string planId = server.Database.CrawlPlans.EnumerateAsync(ApiTenant(server), ct).GetAwaiter().GetResult().Single().Id;

                                ApiResult got = await Mcp(server, token, "pneuma_get_crawl_plan", "{\"id\":\"" + planId + "\"}", ct);
                                Check(got.Body.Contains("Mcp plan") && !got.Body.Contains(_Password), "get hides the secret");
                                ApiResult listed = await Mcp(server, token, "pneuma_enumerate_crawl_plans", "{\"subjectId\":\"" + subjectId + "\"}", ct);
                                Check(listed.Body.Contains(planId), "enumerated");
                                ApiResult tested = await Mcp(server, token, "pneuma_test_crawl_plan", "{\"id\":\"" + planId + "\"}", ct);
                                Check(tested.Body.Contains("layers") && crawler.LastPlan?.Web?.Password == _Password, "test uses the decrypted secret");

                                crawler.Gate.TrySetResult(true);
                                ApiResult preview = await Mcp(server, token, "pneuma_preview_crawl_plan", "{\"id\":\"" + planId + "\"}", ct);
                                Check(preview.Body.Contains("enumerated"), "preview: " + preview.Body);
                                crawler.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                                crawler.ResetSignal();

                                ApiResult started = await Mcp(server, token, "pneuma_start_crawl_plan", "{\"id\":\"" + planId + "\"}", ct);
                                Check(started.Body.Contains("cop_"), "started: " + started.Body);
                                ApiResult again = await Mcp(server, token, "pneuma_start_crawl_plan", "{\"id\":\"" + planId + "\"}", ct);
                                Check(again.Body.Contains("\"error\"") && again.Body.Contains("already running"), "second start refused: " + again.Body);
                                await crawler.EnumerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
                                ApiResult stopped = await Mcp(server, token, "pneuma_stop_crawl_plan", "{\"id\":\"" + planId + "\"}", ct);
                                Check(!stopped.Body.Contains("\"error\""), "stopped: " + stopped.Body);
                                await server.CrawlScheduler.WaitAsync(ApiTenant(server), planId);

                                ApiResult ops = await Mcp(server, token, "pneuma_enumerate_crawl_operations", "{\"planId\":\"" + planId + "\"}", ct);
                                Check(ops.Body.Contains("cop_") && ops.Body.Contains("Cancelled"), "operations: " + ops.Body);
                                string opId = server.Database.CrawlOperations.EnumerateByPlanAsync(ApiTenant(server), planId, ct).GetAwaiter().GetResult().First().Id;
                                ApiResult op = await Mcp(server, token, "pneuma_get_crawl_operation", "{\"id\":\"" + opId + "\"}", ct);
                                Check(op.Body.Contains(opId), "get operation");

                                ApiResult updated = await Mcp(server, token, "pneuma_update_crawl_plan", "{\"id\":\"" + planId + "\",\"plan\":{\"name\":\"Renamed\",\"type\":\"Web\",\"web\":{\"startUrls\":[\"https://fake.example/\"]}}}", ct);
                                Check(updated.Body.Contains("Renamed") && updated.Body.Contains("password"), "update keeps the stored secret name: " + updated.Body);

                                Check((await Mcp(server, token, "pneuma_create_crawl_plan", "{\"subjectId\":\"" + subjectId + "\"}", ct)).Body.Contains("\"error\""), "missing plan refused");
                                Check((await Mcp(server, token, "pneuma_create_crawl_plan", "{\"subjectId\":\"" + subjectId + "\",\"plan\":{\"name\":\"x\",\"type\":\"Web\"}}", ct)).Body.Contains("\"error\""), "invalid plan refused");
                                Check((await Mcp(server, token, "pneuma_get_crawl_plan", "{\"id\":\"cpl_missing\"}", ct)).Body.Contains("\"error\""), "unknown plan refused");
                                Check((await Mcp(server, token, "pneuma_update_crawl_plan", "{\"id\":\"" + planId + "\",\"plan\":{\"name\":\"x\",\"type\":\"S3\",\"s3\":{\"bucket\":\"b\"}}}", ct)).Body.Contains("\"error\""), "type change refused");
                            }
                        }),

                    new TestCaseDescriptor("CrawlApi", "RobotsOff_AdminOnly", "An editor can manage plans but cannot turn robots.txt off (403, audited); an administrator can",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await StartAsync(ct))
                            {
                                string adminToken = await AdminAsync(server, ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, adminToken, ct);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/users", adminToken, "{\"email\":\"editor@pneuma\",\"password\":\"pw\",\"firstName\":\"Ed\",\"lastName\":\"Itor\"}", ct), 201);
                                string tenantId = ApiTenant(server);
                                User editor = await server.Database.Users.ReadByEmailAsync(tenantId, "editor@pneuma", ct) ?? throw new Exception("editor missing");
                                UserRole role = await server.Database.Roles.ReadByNameAsync(null, "Editor", ct) ?? await server.Database.Roles.ReadByNameAsync(tenantId, "Editor", ct) ?? throw new Exception("Editor role missing");
                                await server.Database.UserRoleAssignments.CreateAsync(new UserRoleAssignment { TenantId = tenantId, UserId = editor.Id, RoleId = role.Id, RoleName = role.Name }, ct);
                                string editorToken = await ApiClientHelper.LoginAsync(server.BaseUrl, "editor@pneuma", "pw", ct);

                                string url = server.BaseUrl + "/v1.0/subjects/" + subjectId + "/crawl-plans";
                                string polite = "{\"name\":\"Polite\",\"type\":\"Web\",\"web\":{\"startUrls\":[\"https://fake.example/\"]}}";
                                string impolite = "{\"name\":\"Impolite\",\"type\":\"Web\",\"web\":{\"startUrls\":[\"https://fake.example/\"],\"respectRobotsTxt\":false}}";
                                ApiResult created = await ApiClientHelper.CallAsync(HttpMethod.Post, url, editorToken, polite, ct);
                                Expect(created, 201);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, url, editorToken, impolite, ct), 403);
                                string planId = ApiClientHelper.ExtractString(created.Body, "id");
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Put, server.BaseUrl + "/v1.0/crawl-plans/" + planId, editorToken, impolite, ct), 403);
                                List<AuditRecord> audit = await server.Database.Audit.EnumerateAsync(null, 500, ct);
                                Check(audit.Any(a => a.EventType == AuditEventTypeEnum.AuthorizationDenied && (a.DenialReason ?? "").Contains("robots.txt")), "the refusal is audited");

                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Put, server.BaseUrl + "/v1.0/crawl-plans/" + planId, adminToken, impolite, ct), 200);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Put, server.BaseUrl + "/v1.0/crawl-plans/" + planId, editorToken, impolite.Replace("Impolite", "Renamed"), ct), 200);
                                audit = await server.Database.Audit.EnumerateAsync(null, 500, ct);
                                Check(audit.Any(a => a.EventType == AuditEventTypeEnum.CrawlPlanSecurityChanged && (a.DenialReason ?? "").Contains("robots.txt disabled")), "turning it off is audited");
                            }
                        }),

                    new TestCaseDescriptor("CrawlApi", "CrossTenant_Isolated", "Crawl plans, objects, and operations are invisible outside their tenant",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await StartAsync(ct))
                            {
                                string token = await AdminAsync(server, ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                string planId = await CreatePlanAsync(server, token, subjectId, ct);
                                string tenantId = ApiTenant(server);
                                Check(await server.Database.CrawlPlans.ReadAsync(tenantId, planId, ct) != null, "visible in its tenant");
                                Check(await server.Database.CrawlPlans.ReadAsync("ten_other", planId, ct) == null, "invisible in another tenant");
                                Check((await server.Database.CrawlPlans.EnumerateAsync("ten_other", ct)).Count == 0, "not listed in another tenant");
                                Check((await server.Database.CrawlPlans.ReadSecretsAsync("ten_other", planId, ct)).Count == 0, "secrets invisible in another tenant");
                                Check(!await server.Database.CrawlPlans.DeleteAsync("ten_other", planId, ct), "cannot be deleted from another tenant");
                                Check(await server.Database.CrawlPlans.ReadAsync(tenantId, planId, ct) != null, "still present");
                            }
                        })
                });
        }

        private static async Task<TestServer> StartAsync(CancellationToken ct)
        {
            TestServer server = await TestServer.CreateAsync(ct);
            Register(server);
            return server;
        }

        private static FakeCrawler Register(TestServer server)
        {
            FakeCrawler crawler = new FakeCrawler(CrawlPlanTypeEnum.Web);
            server.Crawlers.Register(crawler);
            return crawler;
        }

        private static Task<string> AdminAsync(TestServer server, CancellationToken ct)
        {
            return ApiClientHelper.LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
        }

        private static string ApiTenant(TestServer server)
        {
            List<Tenant> tenants = server.Database.Tenants.EnumerateAsync(CancellationToken.None).GetAwaiter().GetResult();
            return (tenants.FirstOrDefault(t => t.Name == "System") ?? tenants.First()).Id;
        }

        private static async Task<string> CreatePlanAsync(TestServer server, string token, string subjectId, CancellationToken ct)
        {
            ApiResult created = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/crawl-plans", token, PlanBody("Docs", _Password), ct);
            Expect(created, 201);
            return ApiClientHelper.ExtractString(created.Body, "id");
        }

        private static Task<ApiResult> Mcp(TestServer server, string token, string tool, string? arguments, CancellationToken ct)
        {
            string body = tool == "tools/list"
                ? "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}"
                : "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"" + tool + "\",\"arguments\":" + (arguments ?? "{}") + "}}";
            return ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/mcp", token, body, ct);
        }

        private static string PlanBody(string name, string? password)
        {
            string secret = password == null ? String.Empty : ",\"password\":\"" + password + "\"";
            return "{\"name\":\"" + name + "\",\"type\":\"Web\",\"web\":{\"startUrls\":[\"https://fake.example/\"],\"authentication\":\"Basic\",\"username\":\"reader\"" + secret + "},\"labels\":[\"crawled\"]}";
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
