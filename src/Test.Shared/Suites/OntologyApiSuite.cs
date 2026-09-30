namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// The ontology REST and MCP surface: the version lifecycle (draft, approve, retire, delete), pinning a subject and
    /// what it sees, role separation between authoring and approval, validation errors and problems, SKOS import and
    /// OWL export, operations, graph export, prompt scoping (tenant copies of system prompts), and the MCP tools.
    /// </summary>
    public static class OntologyApiSuite
    {
        /// <summary>Build the suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "OntologyApi",
                displayName: "Ontology REST and MCP surface",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("OntologyApi", "Lifecycle_DraftApproveCopyDiff", "An ontology from the template is a draft; approval freezes it; a new draft copies it; the diff and definition describe it",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await AdminAsync(server, ct);
                                string url = server.BaseUrl + "/v1.0";
                                Expect(await Call(HttpMethod.Get, url + "/ontology-templates", token, null, ct), 200, "\"Default\"");
                                ApiResult created = Expect(await Call(HttpMethod.Post, url + "/ontologies", token, "{\"name\":\"Products\",\"template\":\"Default\"}", ct), 201, "\"status\":\"Draft\"");
                                string ontologyId = Json(created.Body).GetProperty("ontology").GetProperty("id").GetString()!;
                                string v1 = Json(created.Body).GetProperty("versions")[0].GetProperty("id").GetString()!;

                                ApiResult draft = Expect(await Call(HttpMethod.Get, url + "/ontology-versions/" + v1, token, null, ct), 200, "\"problems\":[]");
                                string edited = draft.Body.Replace("\"nodeTypes\":[", "\"nodeTypes\":[{\"name\":\"Product\",\"description\":\"Something sold.\"},");
                                Expect(await Call(HttpMethod.Put, url + "/ontology-versions/" + v1, token, edited, ct), 200, "\"Product\"");
                                Expect(await Call(HttpMethod.Post, url + "/ontology-versions/" + v1 + "/approve", token, "{\"changeSummary\":\"First release.\"}", ct), 200, "\"status\":\"Approved\"");
                                Expect(await Call(HttpMethod.Post, url + "/ontology-versions/" + v1 + "/approve", token, null, ct), 409, null);
                                Expect(await Call(HttpMethod.Put, url + "/ontology-versions/" + v1, token, edited, ct), 409, null);
                                Expect(await Call(HttpMethod.Delete, url + "/ontology-versions/" + v1, token, null, ct), 409, null);

                                ApiResult copy = Expect(await Call(HttpMethod.Post, url + "/ontologies/" + ontologyId + "/versions", token, "{}", ct), 201, "\"versionNumber\":2");
                                string v2 = ApiClientHelper.ExtractString(copy.Body, "id");
                                string withPlace = copy.Body.Replace("\"nodeTypes\":[", "\"nodeTypes\":[{\"name\":\"Store\"},");
                                Expect(await Call(HttpMethod.Put, url + "/ontology-versions/" + v2, token, withPlace, ct), 200, null);
                                Expect(await Call(HttpMethod.Get, url + "/ontology-versions/" + v2 + "/diff", token, null, ct), 200, "\"addedNodeTypes\":[\"Store\"]");
                                Expect(await Call(HttpMethod.Get, url + "/ontology-versions/" + v1 + "/definition", token, null, ct), 200, "Product: Something sold.");
                                Expect(await Call(HttpMethod.Delete, url + "/ontology-versions/" + v2, token, null, ct), 204, null);
                                Expect(await Call(HttpMethod.Get, url + "/ontologies/" + ontologyId + "/versions", token, null, ct), 200, "\"totalRecords\":1");
                            }
                        }),

                    new TestCaseDescriptor("OntologyApi", "Pin_ViewRetireAndAudit", "Only an approved version can be pinned; a pinned version cannot be retired or its ontology deleted; pin changes are audited",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await AdminAsync(server, ct);
                                string url = server.BaseUrl + "/v1.0";
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                ApiResult created = Expect(await Call(HttpMethod.Post, url + "/ontologies", token, "{\"name\":\"Pinned\",\"template\":\"Default\"}", ct), 201, null);
                                string ontologyId = Json(created.Body).GetProperty("ontology").GetProperty("id").GetString()!;
                                string v1 = Json(created.Body).GetProperty("versions")[0].GetProperty("id").GetString()!;
                                string pin = "{\"ontologyVersionId\":\"" + v1 + "\"}";

                                Expect(await Call(HttpMethod.Get, url + "/subjects/" + subjectId + "/ontology", token, null, ct), 200, "\"source\":\"Prompt\"");
                                Expect(await Call(HttpMethod.Put, url + "/subjects/" + subjectId + "/ontology", token, pin, ct), 400, "approved");
                                Expect(await Call(HttpMethod.Post, url + "/ontology-versions/" + v1 + "/approve", token, null, ct), 200, null);
                                Expect(await Call(HttpMethod.Put, url + "/subjects/" + subjectId + "/ontology", token, pin, ct), 200, "\"source\":\"Version\"");
                                Expect(await Call(HttpMethod.Get, url + "/subjects/" + subjectId + "/ontology", token, null, ct), 200, "Node types:");
                                Expect(await Call(HttpMethod.Get, url + "/ontologies/" + ontologyId, token, null, ct), 200, "\"subjectId\":\"" + subjectId + "\"");
                                Expect(await Call(HttpMethod.Post, url + "/ontology-versions/" + v1 + "/retire", token, null, ct), 409, "pin");
                                Expect(await Call(HttpMethod.Delete, url + "/ontologies/" + ontologyId, token, null, ct), 409, "Unpin");
                                ApiResult updated = Expect(await Call(HttpMethod.Put, url + "/subjects/" + subjectId, token, "{\"displayName\":\"Renamed\",\"ontologyVersionId\":null,\"classificationTemperature\":0.3}", ct), 200, null);
                                Check(updated.Body.Contains("\"ontologyVersionId\":\"" + v1 + "\"") && updated.Body.Contains("\"classificationTemperature\":0.3"), "the subject update keeps the pin and sets the temperature");

                                Expect(await Call(HttpMethod.Put, url + "/subjects/" + subjectId + "/ontology", token, "{\"ontologyVersionId\":null}", ct), 200, "\"source\":\"Prompt\"");
                                Expect(await Call(HttpMethod.Post, url + "/ontology-versions/" + v1 + "/retire", token, null, ct), 200, "\"status\":\"Retired\"");
                                Expect(await Call(HttpMethod.Put, url + "/subjects/" + subjectId + "/ontology", token, pin, ct), 400, "Retired");
                                List<AuditRecord> audit = await server.Database.Audit.EnumerateAsync(null, 500, ct);
                                Check(audit.Count(a => a.EventType == AuditEventTypeEnum.OntologyGovernance) >= 4, "approval, pins, and retirement are audited");
                                Expect(await Call(HttpMethod.Delete, url + "/ontologies/" + ontologyId, token, null, ct), 204, null);
                            }
                        }),

                    new TestCaseDescriptor("OntologyApi", "Rbac_EditorAuthorsButCannotApprove", "An editor can create and edit drafts but approving needs Ontology Execute; a user without a role is refused",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string admin = await AdminAsync(server, ct);
                                string editor = await UserWithRoleAsync(server, admin, "editor@pneuma", "Editor", ct);
                                string url = server.BaseUrl + "/v1.0";
                                ApiResult created = Expect(await Call(HttpMethod.Post, url + "/ontologies", editor, "{\"name\":\"Authored\",\"template\":\"Default\"}", ct), 201, null);
                                string v1 = Json(created.Body).GetProperty("versions")[0].GetProperty("id").GetString()!;
                                Expect(await Call(HttpMethod.Post, url + "/ontology-versions/" + v1 + "/approve", editor, null, ct), 403, null);
                                Expect(await Call(HttpMethod.Post, url + "/ontology-versions/" + v1 + "/approve", admin, null, ct), 200, null);

                                string plain = await UserWithRoleAsync(server, admin, "plain@pneuma", null, ct);
                                Expect(await Call(HttpMethod.Get, url + "/ontologies", plain, null, ct), 403, null);
                                Expect(await Call(HttpMethod.Get, url + "/ontologies", null, null, ct), 401, null);
                            }
                        }),

                    new TestCaseDescriptor("OntologyApi", "Validation_ErrorsAndProblems", "Missing and duplicate names, unknown templates, invalid drafts, and unapprovable drafts are refused with their problems",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await AdminAsync(server, ct);
                                string url = server.BaseUrl + "/v1.0";
                                Expect(await Call(HttpMethod.Post, url + "/ontologies", token, "{\"name\":\"  \"}", ct), 400, null);
                                Expect(await Call(HttpMethod.Post, url + "/ontologies", token, "{\"name\":\"X\",\"template\":\"Nope\"}", ct), 400, "Unknown template");
                                ApiResult created = Expect(await Call(HttpMethod.Post, url + "/ontologies", token, "{\"name\":\"Empty\"}", ct), 201, null);
                                Expect(await Call(HttpMethod.Post, url + "/ontologies", token, "{\"name\":\"Empty\"}", ct), 409, null);
                                string v1 = Json(created.Body).GetProperty("versions")[0].GetProperty("id").GetString()!;
                                Expect(await Call(HttpMethod.Put, url + "/ontology-versions/" + v1, token, "{\"nodeTypes\":[{\"name\":\"Person\"},{\"name\":\"PERSON\"}]}", ct), 400, "\"problems\":[");
                                Expect(await Call(HttpMethod.Put, url + "/ontology-versions/" + v1, token,
                                    "{\"nodeTypes\":[{\"name\":\"Person\"}],\"rules\":[{\"ruleType\":\"EdgeEndpoints\",\"edgeType\":\"KNOWS\",\"fromNodeType\":\"Person\",\"toNodeType\":\"Person\"}]}", ct), 200, "is not declared");
                                Expect(await Call(HttpMethod.Post, url + "/ontology-versions/" + v1 + "/approve", token, null, ct), 400, "\"problems\":[");
                                Expect(await Call(HttpMethod.Put, url + "/ontology-versions/" + v1, token, "{\"rules\":[{\"ruleType\":\"Bogus\"}]}", ct), 400, "Invalid request body");
                                Expect(await Call(HttpMethod.Get, url + "/ontology-versions/onv_missing", token, null, ct), 404, null);
                            }
                        }),

                    new TestCaseDescriptor("OntologyApi", "Taxonomy_ImportAndExport", "A SKOS taxonomy imports into a draft (merge and replace); versions export as OWL and SKOS; bad formats are refused",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await AdminAsync(server, ct);
                                string url = server.BaseUrl + "/v1.0";
                                ApiResult created = Expect(await Call(HttpMethod.Post, url + "/ontologies", token, "{\"name\":\"Tech\",\"template\":\"Default\"}", ct), 201, null);
                                string v1 = Json(created.Body).GetProperty("versions")[0].GetProperty("id").GetString()!;
                                string import = url + "/ontology-versions/" + v1 + "/taxonomy/import";
                                Expect(await Raw(HttpMethod.Post, import + "?format=turtle", token, OntologyTestData.SkosTurtle, ct), 200, "\"added\":2");
                                Expect(await Raw(HttpMethod.Post, import + "?format=turtle&mode=merge", token, OntologyTestData.SkosTurtle, ct), 200, "\"updated\":2");
                                Expect(await Raw(HttpMethod.Post, import + "?format=turtle&mode=replace", token, OntologyTestData.SkosTurtle, ct), 200, "\"removed\":2");
                                Expect(await Raw(HttpMethod.Post, import + "?format=turtle", token, "not turtle", ct), 400, null);
                                Expect(await Raw(HttpMethod.Post, import + "?format=xml", token, OntologyTestData.SkosTurtle, ct), 400, null);
                                Expect(await Raw(HttpMethod.Post, import + "?mode=sometimes", token, OntologyTestData.SkosTurtle, ct), 400, null);
                                ApiResult exported = Expect(await Call(HttpMethod.Get, url + "/ontology-versions/" + v1 + "/export?format=turtle", token, null, ct), 200, "skos:Concept");
                                Check(exported.Body.Contains("owl:Class") && exported.Body.Contains("PostgreSQL"), "classes and concepts are exported");
                                Expect(await Call(HttpMethod.Get, url + "/ontology-versions/" + v1 + "/export?format=jsonld", token, null, ct), 200, "@id");
                                Expect(await Call(HttpMethod.Get, url + "/ontology-versions/" + v1 + "/export?format=rdfxml", token, null, ct), 400, null);
                                Expect(await Call(HttpMethod.Get, url + "/ontology-versions/" + v1 + "/export?baseIri=not-absolute", token, null, ct), 400, null);
                                Expect(await Call(HttpMethod.Post, url + "/ontology-versions/" + v1 + "/approve", token, null, ct), 200, null);
                                Expect(await Raw(HttpMethod.Post, import + "?format=turtle", token, OntologyTestData.SkosTurtle, ct), 409, null);
                            }
                        }),

                    new TestCaseDescriptor("OntologyApi", "Operations_ExportAndCache", "Operations queue and finish; bad requests are refused; the graph exports in every format; the cache can be cleared",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await AdminAsync(server, ct);
                                string url = server.BaseUrl + "/v1.0";
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                string ops = url + "/subjects/" + subjectId + "/ontology-operations";
                                Expect(await Call(HttpMethod.Post, ops, token, "{\"kind\":\"Validate\"}", ct), 400, "Pin");
                                Expect(await Call(HttpMethod.Post, ops, token, "{\"kind\":\"DriftCheck\",\"sampleSize\":0}", ct), 400, null);
                                Expect(await Call(HttpMethod.Post, ops, token, "{\"kind\":\"Sometimes\"}", ct), 400, null);
                                ApiResult queued = Expect(await Call(HttpMethod.Post, ops, token, "{\"kind\":\"Retag\"}", ct), 202, "\"status\":\"Queued\"");
                                string operationId = ApiClientHelper.ExtractString(queued.Body, "id");
                                string status = await WaitForOperationAsync(server, token, operationId, ct);
                                Check(status == "Succeeded", "the worker finished the retag, got " + status);
                                Expect(await Call(HttpMethod.Get, ops, token, null, ct), 200, operationId);
                                Expect(await Call(HttpMethod.Get, url + "/ontology-operations/oop_missing", token, null, ct), 404, null);
                                Expect(await Call(HttpMethod.Get, url + "/subjects/" + subjectId + "/ontology-violations?status=Quarantined", token, null, ct), 200, "\"totalRecords\":0");
                                Expect(await Call(HttpMethod.Get, url + "/subjects/" + subjectId + "/ontology-violations?status=Sometimes", token, null, ct), 400, null);
                                Expect(await Call(HttpMethod.Post, url + "/ontology-violations/ovl_missing/release", token, null, ct), 404, null);

                                string export = url + "/subjects/" + subjectId + "/graph/export";
                                Expect(await Call(HttpMethod.Get, export, token, null, ct), 200, "\"nodes\":[]");
                                Expect(await Call(HttpMethod.Get, export + "?format=graphml", token, null, ct), 200, "<graphml");
                                Expect(await Call(HttpMethod.Get, export + "?format=turtle", token, null, ct), 200, null);
                                Expect(await Call(HttpMethod.Get, export + "?format=jsonld", token, null, ct), 200, null);
                                Expect(await Call(HttpMethod.Get, export + "?format=pdf", token, null, ct), 400, null);
                                Expect(await Call(HttpMethod.Delete, url + "/subjects/" + subjectId + "/classification-cache", token, null, ct), 200, "\"removed\":0");
                                Expect(await Call(HttpMethod.Get, url + "/subjects/sub_missing/ontology", token, null, ct), 404, null);
                            }
                        }),

                    new TestCaseDescriptor("OntologyApi", "Prompts_TenantCopiesOfSystemDefaults", "A tenant user's edit of a system prompt becomes a tenant copy; the system default is unchanged; deleting the copy resets it",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string admin = await AdminAsync(server, ct);
                                string editor = await UserWithRoleAsync(server, admin, "prompts@pneuma", "Editor", ct);
                                string url = server.BaseUrl + "/v1.0/prompts";
                                ApiResult list = Expect(await Call(HttpMethod.Get, url + "?maxResults=1000", editor, null, ct), 200, "ontology.classify.format");
                                Check(list.Body.Contains("ontology.propose") && list.Body.Contains("taxonomy.hint"), "the new prompts are seeded and listed");
                                Prompt? system = (await server.Database.Prompts.EnumerateAsync(null, ct)).FirstOrDefault(p => p.Key == "taxonomy.hint") ?? throw new Exception("seed missing");

                                ApiResult copy = Expect(await Call(HttpMethod.Put, url + "/" + system.Id, editor, "{\"content\":\"Tenant hint:\",\"active\":true}", ct), 200, "\"isSystemDefault\":false");
                                string copyId = ApiClientHelper.ExtractString(copy.Body, "id");
                                Check(copyId != system.Id, "a separate tenant row was created");
                                Prompt? unchanged = await server.Database.Prompts.ReadAsync(system.Id, ct);
                                Check(unchanged != null && unchanged.Content == system.Content, "the system default is unchanged");
                                Expect(await Call(HttpMethod.Put, url + "/" + system.Id, editor, "{\"content\":\"Tenant hint v2:\",\"active\":true}", ct), 200, copyId);
                                ApiResult effective = Expect(await Call(HttpMethod.Get, url + "?maxResults=1000", editor, null, ct), 200, "Tenant hint v2:");
                                Check(effective.Body.Split("\"key\":\"taxonomy.hint\"").Length == 2, "the effective list shows one row per key");
                                Expect(await Call(HttpMethod.Get, url + "?scope=system&maxResults=1000", editor, null, ct), 200, system.Content.Substring(0, 20));

                                Expect(await Call(HttpMethod.Delete, url + "/" + system.Id, editor, null, ct), 400, "protected");
                                Expect(await Call(HttpMethod.Delete, url + "/" + copyId, editor, null, ct), 204, null);
                                Expect(await Call(HttpMethod.Put, url + "/" + system.Id, admin, "{\"content\":\"" + system.Content.Replace("\"", "\\\"") + " Always.\",\"active\":true}", ct), 200, "\"isSystemDefault\":true");
                                Expect(await Call(HttpMethod.Post, url, editor, "{\"key\":\"custom.note\",\"name\":\"Note\",\"content\":\"x\"}", ct), 201, null);
                                Expect(await Call(HttpMethod.Post, url, editor, "{\"key\":\"custom.note\",\"name\":\"Note\",\"content\":\"y\"}", ct), 409, null);
                            }
                        }),

                    new TestCaseDescriptor("OntologyApi", "Mcp_OntologyTools", "The MCP ontology tools list, read, and queue, and refuse bad input",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await AdminAsync(server, ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                Expect(await Call(HttpMethod.Post, server.BaseUrl + "/v1.0/ontologies", token, "{\"name\":\"Mcp\",\"template\":\"Default\"}", ct), 201, null);
                                string mcp = server.BaseUrl + "/mcp";
                                Check((await Call(HttpMethod.Post, mcp, token, Rpc("tools/list", "{}"), ct)).Body.Contains("pneuma_get_subject_ontology"), "the tools are listed");
                                Check((await Call(HttpMethod.Post, mcp, token, Tool("pneuma_enumerate_ontologies", "{}"), ct)).Body.Contains("Mcp"), "the ontology is enumerated");
                                Check((await Call(HttpMethod.Post, mcp, token, Tool("pneuma_get_subject_ontology", "{\"subjectId\":\"" + subjectId + "\"}"), ct)).Body.Contains("Prompt"), "the subject view is returned");
                                Check((await Call(HttpMethod.Post, mcp, token, Tool("pneuma_start_ontology_operation", "{\"subjectId\":\"" + subjectId + "\",\"kind\":\"Retag\"}"), ct)).Body.Contains("oop_"), "an operation is queued");
                                Check((await Call(HttpMethod.Post, mcp, token, Tool("pneuma_start_ontology_operation", "{\"subjectId\":\"" + subjectId + "\",\"kind\":\"Bogus\"}"), ct)).Body.Contains("\"error\""), "a bad kind is refused");
                                Check((await Call(HttpMethod.Post, mcp, token, Tool("pneuma_get_ontology_version", "{\"id\":\"onv_missing\"}"), ct)).Body.Contains("\"error\""), "a missing version is an error");
                            }
                        })
                });
        }

        private static async Task<string> WaitForOperationAsync(TestServer server, string token, string operationId, CancellationToken ct)
        {
            for (int i = 0; i < 100; i++)
            {
                ApiResult read = await Call(HttpMethod.Get, server.BaseUrl + "/v1.0/ontology-operations/" + operationId, token, null, ct);
                string status = Json(read.Body).GetProperty("operation").GetProperty("status").GetString() ?? String.Empty;
                if (status == "Succeeded" || status == "Failed") return status;
                await Task.Delay(200, ct);
            }
            return "Timeout";
        }

        private static async Task<string> UserWithRoleAsync(TestServer server, string adminToken, string email, string? roleName, CancellationToken ct)
        {
            Expect(await Call(HttpMethod.Post, server.BaseUrl + "/v1.0/users", adminToken, "{\"email\":\"" + email + "\",\"password\":\"pw\",\"firstName\":\"Test\",\"lastName\":\"User\"}", ct), 201, null);
            if (roleName != null)
            {
                List<Tenant> tenants = await server.Database.Tenants.EnumerateAsync(ct);
                string tenantId = (tenants.FirstOrDefault(t => t.Name == "System") ?? tenants.First()).Id;
                User user = await server.Database.Users.ReadByEmailAsync(tenantId, email, ct) ?? throw new Exception("user missing");
                UserRole role = await server.Database.Roles.ReadByNameAsync(null, roleName, ct) ?? throw new Exception(roleName + " role missing");
                await server.Database.UserRoleAssignments.CreateAsync(new UserRoleAssignment { TenantId = tenantId, UserId = user.Id, RoleId = role.Id, RoleName = role.Name }, ct);
            }
            return await ApiClientHelper.LoginAsync(server.BaseUrl, email, "pw", ct);
        }

        private static Task<string> AdminAsync(TestServer server, CancellationToken ct)
        {
            return ApiClientHelper.LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
        }

        private static Task<ApiResult> Call(HttpMethod method, string url, string? token, string? body, CancellationToken ct)
        {
            return ApiClientHelper.CallAsync(method, url, token, body, ct);
        }

        private static async Task<ApiResult> Raw(HttpMethod method, string url, string? token, string body, CancellationToken ct)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, url))
            {
                request.Content = new StringContent(body, System.Text.Encoding.UTF8, "text/turtle");
                if (token != null) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                using (HttpClient client = new HttpClient())
                using (HttpResponseMessage response = await client.SendAsync(request, ct))
                {
                    return new ApiResult { StatusCode = (int)response.StatusCode, Body = await response.Content.ReadAsStringAsync(ct) };
                }
            }
        }

        private static string Rpc(string method, string parameters)
        {
            return "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"" + method + "\",\"params\":" + parameters + "}";
        }

        private static string Tool(string name, string arguments)
        {
            return Rpc("tools/call", "{\"name\":\"" + name + "\",\"arguments\":" + arguments + "}");
        }

        private static JsonElement Json(string body)
        {
            using (JsonDocument doc = JsonDocument.Parse(body))
            {
                return doc.RootElement.Clone();
            }
        }

        private static ApiResult Expect(ApiResult result, int status, string? contains)
        {
            if (result.StatusCode != status) throw new Exception("expected " + status + ", got " + result.StatusCode + " " + result.Body);
            if (contains != null && !result.Body.Contains(contains, StringComparison.Ordinal)) throw new Exception("expected the body to contain " + contains + ": " + result.Body);
            return result;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
