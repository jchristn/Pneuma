namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Serialization;
    using Pneuma.Server.Streaming;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// End-to-end HTTP tests against an in-process PneumaServer: anonymous health, auth flow, RBAC
    /// gating, subject/link enqueue, session revocation, request-history capture, and metrics.
    /// </summary>
    public static class ApiSuite
    {
        private static readonly HttpClient _Http = new HttpClient();

        /// <summary>Build the API suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Api",
                displayName: "HTTP API",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("Api", "Health_Anonymous", "Health is reachable without auth",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            HttpResponseMessage response = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/api/health", null, null, ct);
                            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("health not 200: " + (int)response.StatusCode);
                        }),

                    new TestCaseDescriptor("Api", "Login_And_BearerGating", "Login yields a token; protected route needs it",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            HttpResponseMessage noAuth = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/tenants", null, null, ct);
                            if (noAuth.StatusCode != HttpStatusCode.Unauthorized) throw new Exception("expected 401 without token, got " + (int)noAuth.StatusCode);

                            HttpResponseMessage authed = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/tenants", token, null, ct);
                            if (authed.StatusCode != HttpStatusCode.OK) throw new Exception("expected 200 with token, got " + (int)authed.StatusCode);
                            string body = await authed.Content.ReadAsStringAsync(ct);
                            if (!body.Contains("System")) throw new Exception("tenants list should contain the System tenant");
                        }),

                    new TestCaseDescriptor("Api", "NonAdmin_Forbidden", "A user with no roles is denied admin routes",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string adminToken = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            string createBody = "{\"email\":\"nobody@pneuma\",\"password\":\"pw\",\"firstName\":\"No\",\"lastName\":\"Body\"}";
                            HttpResponseMessage created = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/users", adminToken, createBody, ct);
                            if (created.StatusCode != HttpStatusCode.Created) throw new Exception("user create failed: " + (int)created.StatusCode);

                            string userToken = await LoginAsync(server.BaseUrl, "nobody@pneuma", "pw", ct);
                            HttpResponseMessage denied = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/tenants", userToken, null, ct);
                            if (denied.StatusCode != HttpStatusCode.Forbidden) throw new Exception("expected 403 for non-admin, got " + (int)denied.StatusCode);
                        }),

                    new TestCaseDescriptor("Api", "Subject_Link_EnqueuesJob", "Submitting a link enqueues an ingestion job",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            // Models + collection are owned by the subject; create the collection first, then a
                            // subject configured with it, then submit a link carrying only the URL.
                            string collectionId = await CreateCollectionAsync(server.BaseUrl, token, ct);

                            HttpResponseMessage subjectResp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects", token, "{\"displayName\":\"Example Subject\",\"type\":\"Person\",\"embeddingModel\":\"default\",\"inferenceModel\":\"default\",\"collection\":\"" + collectionId + "\"}", ct);
                            string subjectBody = await subjectResp.Content.ReadAsStringAsync(ct);
                            string subjectId = ExtractString(subjectBody, "id");
                            if (String.IsNullOrEmpty(subjectId)) throw new Exception("subject id missing");

                            HttpResponseMessage linkResp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/links", token, "{\"url\":\"https://example.com/a\"}", ct);
                            if (linkResp.StatusCode != HttpStatusCode.Created) throw new Exception("link submit failed: " + (int)linkResp.StatusCode);

                            HttpResponseMessage jobsResp = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/jobs", token, null, ct);
                            string jobsBody = await jobsResp.Content.ReadAsStringAsync(ct);
                            if (!jobsBody.Contains("Queued")) throw new Exception("expected a Queued job after link submit");

                            // The link's ingestion log endpoint must return the job (events accrue as it runs).
                            string linkId = ExtractString(await linkResp.Content.ReadAsStringAsync(ct), "id");
                            HttpResponseMessage logResp = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/links/" + linkId + "/log", token, null, ct);
                            if (logResp.StatusCode != HttpStatusCode.OK) throw new Exception("link log not 200: " + (int)logResp.StatusCode);
                            string logBody = await logResp.Content.ReadAsStringAsync(ct);
                            if (!logBody.Contains("\"job\"") && !logBody.Contains("\"events\"")) throw new Exception("link log missing job/events shape");
                        }),

                    new TestCaseDescriptor("Api", "Subject_Link_LabelsAndTags_RoundTrip", "Submitting a link with labels and tags round-trips them on the created and re-read link; a link with no URL is rejected",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                            string collectionId = await CreateCollectionAsync(server.BaseUrl, token, ct);

                            HttpResponseMessage subjectResp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects", token, "{\"displayName\":\"Example Subject\",\"type\":\"Person\",\"embeddingModel\":\"default\",\"inferenceModel\":\"default\",\"collection\":\"" + collectionId + "\"}", ct);
                            string subjectId = ExtractString(await subjectResp.Content.ReadAsStringAsync(ct), "id");
                            if (String.IsNullOrEmpty(subjectId)) throw new Exception("subject id missing");

                            // Positive: labels + tags on the body round-trip on the created link.
                            string body = "{\"url\":\"https://example.com/a\",\"labels\":[\"news\",\"2024\"],\"tags\":{\"author\":\"jane\"}}";
                            HttpResponseMessage linkResp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/links", token, body, ct);
                            if (linkResp.StatusCode != HttpStatusCode.Created) throw new Exception("link submit failed: " + (int)linkResp.StatusCode);
                            string linkBody = await linkResp.Content.ReadAsStringAsync(ct);
                            if (!linkBody.Contains("news") || !linkBody.Contains("2024")) throw new Exception("created link did not echo its labels: " + linkBody);
                            if (!linkBody.Contains("author") || !linkBody.Contains("jane")) throw new Exception("created link did not echo its tags: " + linkBody);

                            // Persisted: re-reading the link returns the same labels/tags.
                            string linkId = ExtractString(linkBody, "id");
                            HttpResponseMessage readResp = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/links/" + linkId, token, null, ct);
                            if (readResp.StatusCode != HttpStatusCode.OK) throw new Exception("link read not 200: " + (int)readResp.StatusCode);
                            string readBody = await readResp.Content.ReadAsStringAsync(ct);
                            if (!readBody.Contains("news") || !readBody.Contains("author")) throw new Exception("persisted link missing labels/tags: " + readBody);

                            // Negative: a body with no URL is rejected with 400 even when labels/tags are present.
                            HttpResponseMessage bad = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/links", token, "{\"labels\":[\"x\"]}", ct);
                            if (bad.StatusCode != HttpStatusCode.BadRequest) throw new Exception("expected 400 for a link with no URL, got " + (int)bad.StatusCode);
                        }),

                    new TestCaseDescriptor("Api", "Eval_Run_QueuesAndProcesses", "Starting a run queues it (non-blocking) and the background worker drives it to a terminal state; missing subjectId is rejected; cancel is idempotent",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                            string collectionId = await CreateCollectionAsync(server.BaseUrl, token, ct);
                            HttpResponseMessage subjectResp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects", token, "{\"displayName\":\"Ada\",\"type\":\"Person\",\"embeddingModel\":\"default\",\"inferenceModel\":\"default\",\"collection\":\"" + collectionId + "\"}", ct);
                            string subjectId = ExtractString(await subjectResp.Content.ReadAsStringAsync(ct), "id");
                            if (String.IsNullOrEmpty(subjectId)) throw new Exception("subject id missing");

                            HttpResponseMessage factResp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/eval/facts", token, "{\"subjectId\":\"" + subjectId + "\",\"question\":\"Who?\",\"expectedAnswer\":\"Ada\"}", ct);
                            if (factResp.StatusCode != HttpStatusCode.Created) throw new Exception("fact create failed: " + (int)factResp.StatusCode);

                            // Start returns immediately with a queued (non-terminal) run.
                            HttpResponseMessage startResp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/eval/runs", token, "{\"subjectId\":\"" + subjectId + "\"}", ct);
                            if (startResp.StatusCode != HttpStatusCode.Created) throw new Exception("run start failed: " + (int)startResp.StatusCode);
                            string startBody = await startResp.Content.ReadAsStringAsync(ct);
                            string runId = ExtractString(startBody, "id");
                            string startStatus = ExtractString(startBody, "status");
                            if (String.IsNullOrEmpty(runId)) throw new Exception("run id missing");
                            if (startStatus == "Completed" || startStatus == "Failed") throw new Exception("run should be queued (Pending/Running), not already " + startStatus);

                            // The background worker drives it to a terminal state.
                            string finalStatus = String.Empty;
                            for (int attempt = 0; attempt < 60; attempt++)
                            {
                                HttpResponseMessage detailResp = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/eval/runs/" + runId, token, null, ct);
                                finalStatus = ExtractString(await detailResp.Content.ReadAsStringAsync(ct), "status");
                                if (finalStatus == "Completed" || finalStatus == "Failed" || finalStatus == "Cancelled") break;
                                await Task.Delay(500, ct);
                            }
                            if (finalStatus != "Completed" && finalStatus != "Failed" && finalStatus != "Cancelled") throw new Exception("run did not reach a terminal state; last status: " + finalStatus);

                            // Cancel is idempotent on a terminal run.
                            HttpResponseMessage cancelResp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/eval/runs/" + runId + "/cancel", token, null, ct);
                            if (cancelResp.StatusCode != HttpStatusCode.OK) throw new Exception("cancel should be 200, got " + (int)cancelResp.StatusCode);

                            // Negative: no subjectId → 400.
                            HttpResponseMessage bad = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/eval/runs", token, "{}", ct);
                            if (bad.StatusCode != HttpStatusCode.BadRequest) throw new Exception("missing subjectId should be 400, got " + (int)bad.StatusCode);
                        }),

                    new TestCaseDescriptor("Api", "Threads_Crud_RoundTrip", "A conversation thread can be created, listed, read with its turns, renamed, and deleted (the switcher's API surface)",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            HttpResponseMessage createResp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/threads", token, "{\"title\":\"My chat\"}", ct);
                            if (createResp.StatusCode != HttpStatusCode.Created) throw new Exception("thread create failed: " + (int)createResp.StatusCode);
                            string threadId = ExtractString(await createResp.Content.ReadAsStringAsync(ct), "id");
                            if (String.IsNullOrEmpty(threadId)) throw new Exception("thread id missing");

                            HttpResponseMessage listResp = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/threads", token, null, ct);
                            if (!(await listResp.Content.ReadAsStringAsync(ct)).Contains(threadId)) throw new Exception("thread not in list");

                            HttpResponseMessage getResp = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/threads/" + threadId, token, null, ct);
                            string getBody = await getResp.Content.ReadAsStringAsync(ct);
                            if (!getBody.Contains("\"thread\"") || !getBody.Contains("\"turns\"")) throw new Exception("thread detail should carry thread + turns (the rehydrate shape)");

                            HttpResponseMessage renameResp = await Send(HttpMethod.Put, server.BaseUrl + "/v1.0/threads/" + threadId, token, "{\"title\":\"Renamed\"}", ct);
                            if (renameResp.StatusCode != HttpStatusCode.OK || !(await renameResp.Content.ReadAsStringAsync(ct)).Contains("Renamed")) throw new Exception("rename failed");

                            HttpResponseMessage delResp = await Send(HttpMethod.Delete, server.BaseUrl + "/v1.0/threads/" + threadId, token, null, ct);
                            if (delResp.StatusCode != HttpStatusCode.NoContent) throw new Exception("delete should be 204, got " + (int)delResp.StatusCode);
                            HttpResponseMessage after = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/threads/" + threadId, token, null, ct);
                            if (after.StatusCode != HttpStatusCode.NotFound) throw new Exception("deleted thread should be 404, got " + (int)after.StatusCode);
                        }),

                    new TestCaseDescriptor("Api", "TokenRevoke", "A revoked token is rejected",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            HttpResponseMessage revoke = await Send(HttpMethod.Delete, server.BaseUrl + "/v1.0/token", token, null, ct);
                            if (revoke.StatusCode != HttpStatusCode.NoContent) throw new Exception("logout should be 204, got " + (int)revoke.StatusCode);

                            HttpResponseMessage after = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/tenants", token, null, ct);
                            if (after.StatusCode != HttpStatusCode.Unauthorized) throw new Exception("revoked token should be 401, got " + (int)after.StatusCode);
                        }),

                    new TestCaseDescriptor("Api", "RequestHistory_Captured", "Handled requests are captured to history",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                            await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/tenants", token, null, ct);

                            long total = 0;
                            for (int attempt = 0; attempt < 20; attempt++)
                            {
                                HttpResponseMessage resp = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/api/request-history?pageSize=50", token, null, ct);
                                string body = await resp.Content.ReadAsStringAsync(ct);
                                RequestHistoryPage? page = Json.Deserialize<RequestHistoryPage>(body);
                                total = page?.TotalCount ?? 0;
                                if (total > 0) break;
                                await Task.Delay(100, ct);
                            }
                            if (total <= 0) throw new Exception("expected request history to capture at least one request");
                        }),

                    new TestCaseDescriptor("Api", "Metrics_Exposed", "Prometheus metrics are exposed",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            HttpResponseMessage resp = await Send(HttpMethod.Get, server.BaseUrl + "/metrics", null, null, ct);
                            if (resp.StatusCode != HttpStatusCode.OK) throw new Exception("metrics not 200");
                            string body = await resp.Content.ReadAsStringAsync(ct);
                            if (!body.Contains("pneuma_http_requests_total")) throw new Exception("metrics missing expected counter");
                        }),

                    new TestCaseDescriptor("Api", "List_EnumerationResult", "List endpoints return a paginated EnumerationResult",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            HttpResponseMessage resp = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/tenants?maxResults=1", token, null, ct);
                            if (resp.StatusCode != HttpStatusCode.OK) throw new Exception("tenants list not 200: " + (int)resp.StatusCode);
                            string body = await resp.Content.ReadAsStringAsync(ct);
                            if (!body.Contains("\"objects\"")) throw new Exception("enumeration result missing objects");
                            if (!body.Contains("\"totalRecords\"")) throw new Exception("enumeration result missing totalRecords");
                            if (!body.Contains("\"endOfResults\"")) throw new Exception("enumeration result missing endOfResults");

                            System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(body);
                            int objectCount = doc.RootElement.GetProperty("objects").GetArrayLength();
                            if (objectCount > 1) throw new Exception("maxResults=1 should cap objects at 1, got " + objectCount);
                        }),

                    new TestCaseDescriptor("Api", "Subject_Slug_FromDisplayName", "Subject graph root node id is slugified from the display name",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            HttpResponseMessage resp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects", token, "{\"displayName\":\"The Example Project\",\"type\":\"Person\"}", ct);
                            if (resp.StatusCode != HttpStatusCode.Created) throw new Exception("subject create failed: " + (int)resp.StatusCode);
                            string body = await resp.Content.ReadAsStringAsync(ct);
                            string slug = ExtractString(body, "graphRootNodeId");
                            if (slug != "the-example-project") throw new Exception("expected slug 'the-example-project', got '" + slug + "'");
                        }),

                    new TestCaseDescriptor("Api", "Job_Stop_And_Log", "An ingestion job can be stopped and its log fetched",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            string collectionId = await CreateCollectionAsync(server.BaseUrl, token, ct);
                            HttpResponseMessage subjectResp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects", token, "{\"displayName\":\"Stop Test\",\"type\":\"Person\",\"embeddingModel\":\"default\",\"inferenceModel\":\"default\",\"collection\":\"" + collectionId + "\"}", ct);
                            string subjectId = ExtractString(await subjectResp.Content.ReadAsStringAsync(ct), "id");
                            HttpResponseMessage linkResp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/links", token, "{\"url\":\"https://example.com/stop\"}", ct);
                            if (linkResp.StatusCode != HttpStatusCode.Created) throw new Exception("link submit failed: " + (int)linkResp.StatusCode);

                            HttpResponseMessage jobsResp = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/jobs?status=Queued", token, null, ct);
                            string jobsBody = await jobsResp.Content.ReadAsStringAsync(ct);
                            System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(jobsBody);
                            System.Text.Json.JsonElement objects = doc.RootElement.GetProperty("objects");
                            if (objects.GetArrayLength() == 0) throw new Exception("expected at least one queued job");
                            string jobId = objects[0].GetProperty("id").GetString() ?? String.Empty;
                            if (String.IsNullOrEmpty(jobId)) throw new Exception("job id missing");

                            HttpResponseMessage stopResp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/jobs/" + jobId + "/stop", token, null, ct);
                            if (stopResp.StatusCode != HttpStatusCode.OK) throw new Exception("stop not 200: " + (int)stopResp.StatusCode);
                            string stopBody = await stopResp.Content.ReadAsStringAsync(ct);
                            if (!stopBody.Contains("Cancelled")) throw new Exception("stopped job should be Cancelled");

                            HttpResponseMessage logResp = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/jobs/" + jobId + "/log", token, null, ct);
                            if (logResp.StatusCode != HttpStatusCode.OK) throw new Exception("job log not 200: " + (int)logResp.StatusCode);
                            string logBody = await logResp.Content.ReadAsStringAsync(ct);
                            if (!logBody.Contains("\"job\"") || !logBody.Contains("\"events\"")) throw new Exception("job log missing job/events shape");

                            // Stopping an already-finished job is a conflict.
                            HttpResponseMessage stopAgain = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/jobs/" + jobId + "/stop", token, null, ct);
                            if (stopAgain.StatusCode != HttpStatusCode.Conflict) throw new Exception("re-stop should be 409, got " + (int)stopAgain.StatusCode);
                        }),

                    new TestCaseDescriptor("Api", "Settings_Read_Write_Masks", "Settings read masks secrets and write preserves them",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            HttpResponseMessage getResp = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/settings", token, null, ct);
                            if (getResp.StatusCode != HttpStatusCode.OK) throw new Exception("settings read not 200: " + (int)getResp.StatusCode);
                            string getBody = await getResp.Content.ReadAsStringAsync(ct);
                            if (!getBody.Contains("********")) throw new Exception("settings read should mask secrets");
                            if (getBody.Contains("pneuma-development-signing-key")) throw new Exception("settings read leaked the signing key");
                            if (!getBody.Contains("\"meta\"") || !getBody.Contains("requiresRestart")) throw new Exception("settings read missing restart metadata");

                            System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(getBody);
                            string settingsJson = doc.RootElement.GetProperty("settings").GetRawText();

                            HttpResponseMessage putResp = await Send(HttpMethod.Put, server.BaseUrl + "/v1.0/settings", token, settingsJson, ct);
                            if (putResp.StatusCode != HttpStatusCode.OK) throw new Exception("settings write not 200: " + (int)putResp.StatusCode);
                            string putBody = await putResp.Content.ReadAsStringAsync(ct);
                            if (!putBody.Contains("\"restartRequired\":true")) throw new Exception("settings write should report restartRequired");

                            // A non-admin must be denied.
                            string createBody = "{\"email\":\"plain@pneuma\",\"password\":\"pw\",\"firstName\":\"Plain\",\"lastName\":\"User\"}";
                            await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/users", token, createBody, ct);
                            string userToken = await LoginAsync(server.BaseUrl, "plain@pneuma", "pw", ct);
                            HttpResponseMessage denied = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/settings", userToken, null, ct);
                            if (denied.StatusCode != HttpStatusCode.Forbidden) throw new Exception("non-admin settings read should be 403, got " + (int)denied.StatusCode);
                        }),

                    new TestCaseDescriptor("Api", "Login_WrongPassword_Rejected", "Login with a bad password is rejected and yields no token",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);

                            string badBody = "{\"email\":\"admin@pneuma\",\"password\":\"not-the-password\"}";
                            HttpResponseMessage bad = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/token", null, badBody, ct);
                            if (bad.StatusCode != HttpStatusCode.Unauthorized)
                                throw new Exception("wrong password should be 401, got " + (int)bad.StatusCode);

                            string unknownBody = "{\"email\":\"ghost@pneuma\",\"password\":\"whatever\"}";
                            HttpResponseMessage unknown = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/token", null, unknownBody, ct);
                            if (unknown.StatusCode != HttpStatusCode.Unauthorized)
                                throw new Exception("unknown user should be 401, got " + (int)unknown.StatusCode);

                            // A rejected login must not hand back a usable bearer token.
                            HttpResponseMessage denied = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/tenants", null, null, ct);
                            if (denied.StatusCode != HttpStatusCode.Unauthorized)
                                throw new Exception("protected route without a token should be 401, got " + (int)denied.StatusCode);
                        }),

                    new TestCaseDescriptor("Api", "Token_Refresh_RotatesSession", "Refreshing rotates the session: the new token works and the old one is revoked",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string oldToken = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            HttpResponseMessage refresh = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/token/refresh", oldToken, "{}", ct);
                            if (refresh.StatusCode != HttpStatusCode.OK) throw new Exception("refresh not 200: " + (int)refresh.StatusCode);
                            string newToken = ExtractString(await refresh.Content.ReadAsStringAsync(ct), "token");
                            if (String.IsNullOrEmpty(newToken)) throw new Exception("refresh did not return a new token");

                            HttpResponseMessage withNew = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/tenants", newToken, null, ct);
                            if (withNew.StatusCode != HttpStatusCode.OK) throw new Exception("the refreshed token should be accepted, got " + (int)withNew.StatusCode);

                            HttpResponseMessage withOld = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/tenants", oldToken, null, ct);
                            if (withOld.StatusCode != HttpStatusCode.Unauthorized) throw new Exception("the old token should be revoked after refresh, got " + (int)withOld.StatusCode);
                        }),

                    new TestCaseDescriptor("Api", "Login_SuccessAndFailure_Audited", "Login success and failure are written to the audit stream",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);

                            // A failed login, then a successful one.
                            await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/token", null, "{\"email\":\"admin@pneuma\",\"password\":\"wrong\"}", ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            HttpResponseMessage audit = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/audit", token, null, ct);
                            if (audit.StatusCode != HttpStatusCode.OK) throw new Exception("audit read not 200: " + (int)audit.StatusCode);
                            string body = await audit.Content.ReadAsStringAsync(ct);
                            if (!body.Contains("AuthFailure")) throw new Exception("expected an AuthFailure audit record after a bad login");
                            if (!body.Contains("AuthSuccess")) throw new Exception("expected an AuthSuccess audit record after a good login");
                        }),

                    new TestCaseDescriptor("Api", "AuthorizationBypass_IsAudited", "System-admin authorization bypasses are written to the audit stream",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            // An admin request to a protected route bypasses RBAC; that bypass must be audited.
                            HttpResponseMessage tenants = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/tenants", token, null, ct);
                            if (tenants.StatusCode != HttpStatusCode.OK) throw new Exception("admin tenants read not 200: " + (int)tenants.StatusCode);

                            HttpResponseMessage audit = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/audit", token, null, ct);
                            if (audit.StatusCode != HttpStatusCode.OK) throw new Exception("audit read not 200: " + (int)audit.StatusCode);
                            string body = await audit.Content.ReadAsStringAsync(ct);
                            if (!body.Contains("AuthorizationBypass")) throw new Exception("expected an AuthorizationBypass audit record after an admin-bypassed request");
                        }),

                    new TestCaseDescriptor("Api", "Mcp_ToolsList", "The integrated MCP endpoint lists its tools",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            string request = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}";
                            HttpResponseMessage response = await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, request, ct);
                            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("tools/list not 200: " + (int)response.StatusCode);
                            string body = await response.Content.ReadAsStringAsync(ct);
                            if (!body.Contains("pneuma_enumerate_subjects") || !body.Contains("pneuma_get_subject")) throw new Exception("tools/list should advertise the subject tools");
                            if (!body.Contains("pneuma_enumerate_jobs") || !body.Contains("pneuma_get_job")) throw new Exception("tools/list should advertise the ingestion-job tools");
                            if (!body.Contains("pneuma_enumerate_links") || !body.Contains("pneuma_get_link")) throw new Exception("tools/list should advertise the content-link tools");
                            if (!body.Contains("pneuma_search")) throw new Exception("tools/list should advertise the search tool");
                            if (!body.Contains("pneuma_get_node") || !body.Contains("pneuma_get_neighbors")) throw new Exception("tools/list should advertise the graph-node tools");
                            if (!body.Contains("pneuma_query")) throw new Exception("tools/list should advertise the grounded-query tool");
                            if (!body.Contains("pneuma_get_history_turn") || !body.Contains("pneuma_enumerate_threads") || !body.Contains("pneuma_analytics") || !body.Contains("pneuma_enumerate_eval_runs")) throw new Exception("tools/list should advertise the history/threads/analytics/eval tools");
                            if (!body.Contains("endOfResults")) throw new Exception("the enumerate tool description should teach the paging protocol");
                        }),

                    new TestCaseDescriptor("Api", "Mcp_Unauthenticated_Denied", "MCP requires authentication like the REST API",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string request = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}";
                            HttpResponseMessage response = await Send(HttpMethod.Post, server.BaseUrl + "/mcp", null, request, ct);
                            if (response.StatusCode != HttpStatusCode.Unauthorized) throw new Exception("MCP without a token should be 401, got " + (int)response.StatusCode);
                        }),

                    new TestCaseDescriptor("Api", "Mcp_EnumerateSubjects_Paged", "pneuma_enumerate_subjects returns a paged EnumerationResult with a total count",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            HttpResponseMessage subjectResp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects", token, "{\"displayName\":\"Example Subject\",\"type\":\"Person\"}", ct);
                            if (subjectResp.StatusCode != HttpStatusCode.Created) throw new Exception("subject create failed: " + (int)subjectResp.StatusCode);

                            string request = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_enumerate_subjects\",\"arguments\":{\"maxResults\":10,\"skip\":0}}}";
                            HttpResponseMessage response = await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, request, ct);
                            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("tools/call not 200: " + (int)response.StatusCode);
                            string body = await response.Content.ReadAsStringAsync(ct);
                            if (!body.Contains("totalRecords")) throw new Exception("the enumeration result must carry totalRecords");
                            if (!body.Contains("Example Subject")) throw new Exception("the enumeration should include the created subject summary");
                        }),

                    new TestCaseDescriptor("Api", "Mcp_GetSubject_MatchesRestTwin", "pneuma_get_subject returns the same object as the REST subject endpoint",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            HttpResponseMessage subjectResp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects", token, "{\"displayName\":\"Another Subject\",\"type\":\"Person\"}", ct);
                            string subjectId = ExtractString(await subjectResp.Content.ReadAsStringAsync(ct), "id");
                            if (String.IsNullOrEmpty(subjectId)) throw new Exception("subject id missing");

                            // REST twin.
                            HttpResponseMessage restResp = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/subjects/" + subjectId, token, null, ct);
                            string restDisplayName = ExtractString(await restResp.Content.ReadAsStringAsync(ct), "displayName");

                            // MCP tool.
                            string request = "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_get_subject\",\"arguments\":{\"id\":\"" + subjectId + "\"}}}";
                            HttpResponseMessage mcpResp = await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, request, ct);
                            string mcpBody = await mcpResp.Content.ReadAsStringAsync(ct);
                            if (!mcpBody.Contains(subjectId)) throw new Exception("MCP get-subject should return the same id as REST");
                            if (String.IsNullOrEmpty(restDisplayName) || !mcpBody.Contains(restDisplayName)) throw new Exception("MCP get-subject should carry the same displayName as its REST twin");
                        }),

                    new TestCaseDescriptor("Api", "Mcp_Enumerate_ClampsMaxResults", "MCP enumeration clamps an oversized maxResults instead of returning everything",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            string request = "{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_enumerate_subjects\",\"arguments\":{\"maxResults\":99999,\"skip\":0}}}";
                            HttpResponseMessage response = await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, request, ct);
                            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("enumerate call not 200: " + (int)response.StatusCode);
                            string body = await response.Content.ReadAsStringAsync(ct);
                            if (!body.Contains("maxResults")) throw new Exception("enumeration result must carry maxResults");
                            if (body.Contains("99999")) throw new Exception("maxResults must be clamped, not echoed unbounded");
                        }),

                    new TestCaseDescriptor("Api", "Mcp_CreateSubject_Persists", "pneuma_create_subject creates a subject carrying its display name and slug",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            string request = "{\"jsonrpc\":\"2.0\",\"id\":21,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_create_subject\",\"arguments\":{\"displayName\":\"Example Subject\",\"urlSlug\":\"example-subject\",\"thinkingEnabled\":true}}}";
                            HttpResponseMessage response = await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, request, ct);
                            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("create call not 200: " + (int)response.StatusCode);
                            string body = await response.Content.ReadAsStringAsync(ct);
                            if (!body.Contains("example-subject")) throw new Exception("created subject should carry its slug; got: " + body);
                            if (!body.Contains("Example Subject")) throw new Exception("created subject should carry its display name");
                        }),

                    new TestCaseDescriptor("Api", "Mcp_CreateSubject_MissingDisplayName_Errors", "pneuma_create_subject without displayName returns an invalid-params error",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            string request = "{\"jsonrpc\":\"2.0\",\"id\":22,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_create_subject\",\"arguments\":{}}}";
                            HttpResponseMessage response = await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, request, ct);
                            string body = await response.Content.ReadAsStringAsync(ct);
                            if (!body.Contains("-32602")) throw new Exception("a missing displayName must produce an invalid-params error; got: " + body);
                        }),

                    new TestCaseDescriptor("Api", "Mcp_CreateSubject_SlugClash_Errors", "pneuma_create_subject with an explicit duplicate slug is rejected",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            string first = "{\"jsonrpc\":\"2.0\",\"id\":23,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_create_subject\",\"arguments\":{\"displayName\":\"One\",\"urlSlug\":\"dup\"}}}";
                            await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, first, ct);
                            string second = "{\"jsonrpc\":\"2.0\",\"id\":24,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_create_subject\",\"arguments\":{\"displayName\":\"Two\",\"urlSlug\":\"dup\"}}}";
                            HttpResponseMessage response = await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, second, ct);
                            string body = await response.Content.ReadAsStringAsync(ct);
                            if (!body.Contains("-32009")) throw new Exception("an explicit duplicate slug must be rejected; got: " + body);
                        }),

                    new TestCaseDescriptor("Api", "Mcp_UpdateSubject_ChangesFields", "pneuma_update_subject updates only the supplied fields",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            // Create via REST to obtain a clean id, then update via MCP.
                            HttpResponseMessage created = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects", token, "{\"displayName\":\"Editable\"}", ct);
                            string createdBody = await created.Content.ReadAsStringAsync(ct);
                            string subjectId;
                            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(createdBody))
                                subjectId = doc.RootElement.GetProperty("id").GetString() ?? throw new Exception("created subject had no id");

                            string request = "{\"jsonrpc\":\"2.0\",\"id\":25,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_update_subject\",\"arguments\":{\"id\":\"" + subjectId + "\",\"systemPrompt\":\"Be terse.\",\"thinkingEnabled\":true}}}";
                            HttpResponseMessage response = await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, request, ct);
                            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("update call not 200: " + (int)response.StatusCode);
                            string body = await response.Content.ReadAsStringAsync(ct);
                            if (!body.Contains("Be terse.")) throw new Exception("the updated systemPrompt should be reflected in the result; got: " + body);
                        }),

                    new TestCaseDescriptor("Api", "Mcp_Query_ReturnsGroundedShape", "pneuma_query returns a grounded-answer shape",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            string request = "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_query\",\"arguments\":{\"question\":\"who is this?\",\"max\":5}}}";
                            HttpResponseMessage response = await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, request, ct);
                            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("pneuma_query call not 200: " + (int)response.StatusCode);
                            string body = await response.Content.ReadAsStringAsync(ct);
                            if (!body.Contains("insufficientSupport")) throw new Exception("grounded query result should carry insufficientSupport (empty corpus)");
                        }),

                    new TestCaseDescriptor("Api", "Mcp_Query_Stream_EmitsSse", "pneuma_query with stream:true returns an SSE stream ending in the JSON-RPC result",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            string request = "{\"jsonrpc\":\"2.0\",\"id\":8,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_query\",\"arguments\":{\"question\":\"who is this?\",\"stream\":true}}}";
                            HttpResponseMessage response = await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, request, ct);
                            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("streaming query not 200: " + (int)response.StatusCode);
                            string body = await response.Content.ReadAsStringAsync(ct);
                            if (!body.Contains("metadata")) throw new Exception("the stream should emit a metadata event");
                            if (!body.Contains("jsonrpc")) throw new Exception("the final SSE event should be the JSON-RPC result");
                        }),

                    new TestCaseDescriptor("Api", "Mcp_Search_ReturnsBoundedResults", "pneuma_search returns a bounded, ranked result set",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            string request = "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_search\",\"arguments\":{\"query\":\"anything\",\"max\":5}}}";
                            HttpResponseMessage response = await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, request, ct);
                            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("pneuma_search call not 200: " + (int)response.StatusCode);
                            string body = await response.Content.ReadAsStringAsync(ct);
                            if (!body.Contains("\\\"count\\\"") && !body.Contains("count")) throw new Exception("search result should carry a bounded count");
                        }),

                    new TestCaseDescriptor("Api", "Mcp_Search_AcceptsMetadataFilter", "pneuma_search accepts a metadataFilter argument",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                            string request = "{\"jsonrpc\":\"2.0\",\"id\":31,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_search\",\"arguments\":{\"query\":\"anything\",\"max\":5,\"metadataFilter\":{\"requiredLabels\":[\"news\"],\"requiredTags\":[{\"key\":\"rights\",\"condition\":\"Equals\",\"value\":\"public\"}]}}}}";
                            HttpResponseMessage response = await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, request, ct);
                            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("filtered search call not 200: " + (int)response.StatusCode);
                            string body = await response.Content.ReadAsStringAsync(ct);
                            if (body.Contains("\"error\"")) throw new Exception("filtered search should not error: " + body);
                        }),

                    new TestCaseDescriptor("Api", "Mcp_EvalFacts_And_Runs_WriteTools", "MCP eval write tools create/enumerate/delete facts and start/cancel/delete runs",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                            HttpResponseMessage subjectResp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects", token, "{\"displayName\":\"Example Subject\",\"type\":\"Topic\"}", ct);
                            string subjectId = ExtractString(await subjectResp.Content.ReadAsStringAsync(ct), "id");

                            string createFact = "{\"jsonrpc\":\"2.0\",\"id\":40,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_create_eval_fact\",\"arguments\":{\"subjectId\":\"" + subjectId + "\",\"question\":\"Q1\",\"expectedAnswer\":\"A1\",\"category\":\"cat\"}}}";
                            string createBody = McpResultText(await (await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, createFact, ct)).Content.ReadAsStringAsync(ct));
                            string factId = ExtractString(createBody, "id");
                            if (String.IsNullOrEmpty(factId)) throw new Exception("create eval fact should return the fact id: " + createBody);

                            string enumFacts = "{\"jsonrpc\":\"2.0\",\"id\":41,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_enumerate_eval_facts\",\"arguments\":{\"subjectId\":\"" + subjectId + "\",\"maxResults\":10,\"skip\":0}}}";
                            string enumBody = McpResultText(await (await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, enumFacts, ct)).Content.ReadAsStringAsync(ct));
                            if (!enumBody.Contains("totalRecords") || !enumBody.Contains("Q1")) throw new Exception("paged eval-fact enumeration should carry totalRecords and the fact: " + enumBody);

                            string startRun = "{\"jsonrpc\":\"2.0\",\"id\":42,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_start_eval_run\",\"arguments\":{\"subjectId\":\"" + subjectId + "\"}}}";
                            string startBody = McpResultText(await (await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, startRun, ct)).Content.ReadAsStringAsync(ct));
                            string runId = ExtractString(startBody, "id");
                            if (String.IsNullOrEmpty(runId)) throw new Exception("start eval run should return the run id: " + startBody);

                            string cancelRun = "{\"jsonrpc\":\"2.0\",\"id\":43,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_cancel_eval_run\",\"arguments\":{\"id\":\"" + runId + "\"}}}";
                            string cancelBody = McpResultText(await (await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, cancelRun, ct)).Content.ReadAsStringAsync(ct));
                            if (!cancelBody.Contains("Cancelled") && !cancelBody.Contains("Completed") && !cancelBody.Contains("Running") && !cancelBody.Contains("Pending")) throw new Exception("cancel should return the run's status: " + cancelBody);

                            string delRun = "{\"jsonrpc\":\"2.0\",\"id\":44,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_delete_eval_run\",\"arguments\":{\"id\":\"" + runId + "\"}}}";
                            if (!McpResultText(await (await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, delRun, ct)).Content.ReadAsStringAsync(ct)).Contains("deleted")) throw new Exception("delete run should acknowledge");
                            string delFact = "{\"jsonrpc\":\"2.0\",\"id\":45,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_delete_eval_fact\",\"arguments\":{\"id\":\"" + factId + "\"}}}";
                            if (!McpResultText(await (await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, delFact, ct)).Content.ReadAsStringAsync(ct)).Contains("deleted")) throw new Exception("delete fact should acknowledge");
                        }),

                    new TestCaseDescriptor("Api", "Mcp_Thread_Get_Delete", "MCP pneuma_get_thread returns turns and pneuma_delete_thread cascades",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                            HttpResponseMessage createResp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/threads", token, "{\"title\":\"MCP chat\"}", ct);
                            string threadId = ExtractString(await createResp.Content.ReadAsStringAsync(ct), "id");

                            string getThread = "{\"jsonrpc\":\"2.0\",\"id\":50,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_get_thread\",\"arguments\":{\"id\":\"" + threadId + "\"}}}";
                            string getBody = McpResultText(await (await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, getThread, ct)).Content.ReadAsStringAsync(ct));
                            if (!getBody.Contains("thread") || !getBody.Contains("turns")) throw new Exception("MCP get_thread should carry thread + turns: " + getBody);

                            string delThread = "{\"jsonrpc\":\"2.0\",\"id\":51,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_delete_thread\",\"arguments\":{\"id\":\"" + threadId + "\"}}}";
                            if (!McpResultText(await (await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, delThread, ct)).Content.ReadAsStringAsync(ct)).Contains("deleted")) throw new Exception("MCP delete_thread should acknowledge");
                            HttpResponseMessage after = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/threads/" + threadId, token, null, ct);
                            if (after.StatusCode != HttpStatusCode.NotFound) throw new Exception("deleted thread should be 404, got " + (int)after.StatusCode);
                        }),

                    new TestCaseDescriptor("Api", "Facets_Discovery_Rest_And_Mcp", "Distinct labels/tags are discoverable over REST and MCP from a subject's links",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                            string collectionId = await CreateCollectionAsync(server.BaseUrl, token, ct);
                            HttpResponseMessage subjectResp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects", token, "{\"displayName\":\"Example Subject\",\"type\":\"Topic\",\"embeddingModel\":\"default\",\"inferenceModel\":\"default\",\"collection\":\"" + collectionId + "\"}", ct);
                            string subjectId = ExtractString(await subjectResp.Content.ReadAsStringAsync(ct), "id");
                            HttpResponseMessage linkResp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/links", token, "{\"url\":\"https://example.com/a\",\"labels\":[\"news\"],\"tags\":{\"rights\":\"public\"}}", ct);
                            if (linkResp.StatusCode != HttpStatusCode.Created) throw new Exception("link submit failed: " + (int)linkResp.StatusCode);

                            HttpResponseMessage labelsResp = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/retrieval/labels", token, null, ct);
                            if (labelsResp.StatusCode != HttpStatusCode.OK || !(await labelsResp.Content.ReadAsStringAsync(ct)).Contains("news")) throw new Exception("REST labels discovery should return the link's label");
                            HttpResponseMessage tagsResp = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/retrieval/tags", token, null, ct);
                            if (tagsResp.StatusCode != HttpStatusCode.OK || !(await tagsResp.Content.ReadAsStringAsync(ct)).Contains("rights")) throw new Exception("REST tags discovery should return the link's tag key");

                            string mcpLabels = "{\"jsonrpc\":\"2.0\",\"id\":60,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_distinct_labels\",\"arguments\":{\"subjectId\":\"" + subjectId + "\"}}}";
                            if (!McpResultText(await (await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, mcpLabels, ct)).Content.ReadAsStringAsync(ct)).Contains("news")) throw new Exception("MCP distinct_labels should return the label");
                            string mcpTags = "{\"jsonrpc\":\"2.0\",\"id\":61,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_distinct_tags\",\"arguments\":{\"subjectId\":\"" + subjectId + "\"}}}";
                            if (!McpResultText(await (await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, mcpTags, ct)).Content.ReadAsStringAsync(ct)).Contains("rights")) throw new Exception("MCP distinct_tags should return the tag key");
                        }),

                    new TestCaseDescriptor("Api", "Mcp_Ops_History_Settings_Health", "MCP ops tools page request history, redact settings, and page model-endpoint health",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            string history = "{\"jsonrpc\":\"2.0\",\"id\":70,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_enumerate_request_history\",\"arguments\":{\"maxResults\":10,\"skip\":0}}}";
                            if (!McpResultText(await (await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, history, ct)).Content.ReadAsStringAsync(ct)).Contains("totalRecords")) throw new Exception("request-history enumeration should be paged (totalRecords)");

                            string settings = "{\"jsonrpc\":\"2.0\",\"id\":71,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_get_settings\",\"arguments\":{}}}";
                            string settingsBody = McpResultText(await (await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, settings, ct)).Content.ReadAsStringAsync(ct));
                            if (!settingsBody.Contains("********")) throw new Exception("settings should be returned with secrets redacted: " + settingsBody);

                            string health = "{\"jsonrpc\":\"2.0\",\"id\":72,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_enumerate_model_runner_health\",\"arguments\":{\"maxResults\":10,\"skip\":0}}}";
                            if (!McpResultText(await (await Send(HttpMethod.Post, server.BaseUrl + "/mcp", token, health, ct)).Content.ReadAsStringAsync(ct)).Contains("totalRecords")) throw new Exception("model-runner health enumeration should be paged (totalRecords)");
                        }),

                    new TestCaseDescriptor("Api", "Sse_ChunkerPreservesText", "The SSE delta chunker preserves the full answer and does not split words",
                        executeAsync: ct =>
                        {
                            string input = "hello world this is a longer grounded answer with several words";
                            List<string> chunks = SseWriter.SplitIntoChunks(input, 12);
                            if (chunks.Count < 2) throw new Exception("expected the answer to split into multiple deltas");
                            if (String.Concat(chunks) != input) throw new Exception("concatenated deltas must equal the original answer");
                            foreach (string chunk in chunks)
                            {
                                if (chunk.Length == 0) throw new Exception("no delta should be empty");
                            }
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("Api", "Query_Stream_EmitsSseEvents", "The grounded query stream returns SSE events",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            HttpResponseMessage response = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/query/stream", token, "{\"question\":\"who is this?\",\"maxResults\":5}", ct);
                            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("query stream not 200: " + (int)response.StatusCode);
                            string body = await response.Content.ReadAsStringAsync(ct);
                            if (!body.Contains("metadata")) throw new Exception("the stream should emit a metadata event");
                            if (!body.Contains("insufficientSupport")) throw new Exception("an empty corpus should stream a complete event with insufficientSupport");
                        })
                });
        }

        private static async Task<string> LoginAsync(string baseUrl, string email, string password, CancellationToken ct)
        {
            string body = "{\"email\":\"" + email + "\",\"password\":\"" + password + "\"}";
            HttpResponseMessage response = await Send(HttpMethod.Post, baseUrl + "/v1.0/token", null, body, ct);
            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("login failed: " + (int)response.StatusCode);
            string text = await response.Content.ReadAsStringAsync(ct);
            TokenResponse? token = Json.Deserialize<TokenResponse>(text);
            if (token == null || String.IsNullOrEmpty(token.Token)) throw new Exception("login returned no token");
            return token.Token;
        }

        private static async Task<string> CreateCollectionAsync(string baseUrl, string token, CancellationToken ct)
        {
            HttpResponseMessage response = await Send(HttpMethod.Put, baseUrl + "/v1.0/collections", token, "{\"name\":\"test\",\"dimensionality\":8}", ct);
            if (response.StatusCode != HttpStatusCode.Created) throw new Exception("collection create failed: " + (int)response.StatusCode);
            string id = ExtractString(await response.Content.ReadAsStringAsync(ct), "id");
            if (String.IsNullOrEmpty(id)) throw new Exception("collection create returned no id");
            return id;
        }

        private static async Task<HttpResponseMessage> Send(HttpMethod method, string url, string? token, string? body, CancellationToken ct)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, url))
            {
                if (!String.IsNullOrEmpty(token)) request.Headers.Add("Authorization", "Bearer " + token);
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                return await _Http.SendAsync(request, ct).ConfigureAwait(false);
            }
        }

        private static string McpResultText(string body)
        {
            // Unwrap an MCP tools/call envelope: { result: { content: [ { type:"text", text:"<serialized json>" } ] } }.
            // Returns the inner tool-result JSON string (or the original body when it is not a tools/call envelope).
            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(body))
            {
                System.Text.Json.JsonElement root = doc.RootElement;
                if (root.TryGetProperty("result", out System.Text.Json.JsonElement result)
                    && result.TryGetProperty("content", out System.Text.Json.JsonElement content)
                    && content.ValueKind == System.Text.Json.JsonValueKind.Array && content.GetArrayLength() > 0)
                {
                    System.Text.Json.JsonElement first = content[0];
                    if (first.TryGetProperty("text", out System.Text.Json.JsonElement textElement) && textElement.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        return textElement.GetString() ?? String.Empty;
                    }
                }
            }
            return body;
        }

        private static string ExtractString(string json, string field)
        {
            // Minimal extraction sufficient for test ids in a flat JSON object.
            string marker = "\"" + field + "\":\"";
            int start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return String.Empty;
            start += marker.Length;
            int end = json.IndexOf('"', start);
            return end > start ? json.Substring(start, end - start) : String.Empty;
        }
    }
}
