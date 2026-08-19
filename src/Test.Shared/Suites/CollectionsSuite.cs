namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Serialization;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// End-to-end HTTP tests for tenant-scoped vector collections: the collection CRUD proxy to RecallDB, the
    /// requirement that a link submission names a valid collection, and the automatic RecallDB tenant +
    /// default-collection provisioning that fires when a Pneuma tenant is created.
    /// </summary>
    public static class CollectionsSuite
    {
        private static readonly HttpClient _Http = new HttpClient();

        /// <summary>Build the collections suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Collections",
                displayName: "Collections & Provisioning",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("Collections", "Crud_And_RequiredForLink", "Collections can be created/listed/read/deleted and are required to submit a link",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            // Create a collection and confirm it is listable and readable.
                            HttpResponseMessage createResp = await Send(HttpMethod.Put, server.BaseUrl + "/v1.0/collections", token, "{\"name\":\"crud-test\",\"dimensionality\":16}", ct);
                            if (createResp.StatusCode != HttpStatusCode.Created) throw new Exception("collection create not 201: " + (int)createResp.StatusCode);
                            string collectionId = ExtractString(await createResp.Content.ReadAsStringAsync(ct), "id");
                            if (String.IsNullOrEmpty(collectionId)) throw new Exception("collection create returned no id");

                            HttpResponseMessage listResp = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/collections", token, null, ct);
                            string listBody = await listResp.Content.ReadAsStringAsync(ct);
                            if (!listBody.Contains(collectionId)) throw new Exception("created collection missing from list");

                            HttpResponseMessage readResp = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/collections/" + collectionId, token, null, ct);
                            if (readResp.StatusCode != HttpStatusCode.OK) throw new Exception("collection read not 200: " + (int)readResp.StatusCode);

                            HttpResponseMessage subjectResp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects", token, "{\"displayName\":\"Coll Test\",\"type\":\"Person\"}", ct);
                            string subjectId = ExtractString(await subjectResp.Content.ReadAsStringAsync(ct), "id");

                            // Negative: submitting a link without a collection is rejected.
                            HttpResponseMessage noColl = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/links", token, "{\"url\":\"https://example.com/x\",\"embeddingEndpointId\":\"default\",\"completionEndpointId\":\"default\"}", ct);
                            if (noColl.StatusCode != HttpStatusCode.BadRequest) throw new Exception("link submit without a collection should be 400, got " + (int)noColl.StatusCode);

                            // Negative: a non-existent collection is rejected.
                            HttpResponseMessage badColl = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/links", token, "{\"url\":\"https://example.com/x\",\"embeddingEndpointId\":\"default\",\"completionEndpointId\":\"default\",\"collectionId\":\"col_does_not_exist\"}", ct);
                            if (badColl.StatusCode != HttpStatusCode.BadRequest) throw new Exception("link submit with an unknown collection should be 400, got " + (int)badColl.StatusCode);

                            // Positive: a valid collection is accepted.
                            HttpResponseMessage ok = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/links", token, "{\"url\":\"https://example.com/x\",\"embeddingEndpointId\":\"default\",\"completionEndpointId\":\"default\",\"collectionId\":\"" + collectionId + "\"}", ct);
                            if (ok.StatusCode != HttpStatusCode.Created) throw new Exception("link submit with a valid collection should be 201, got " + (int)ok.StatusCode);

                            // Delete the collection.
                            HttpResponseMessage delResp = await Send(HttpMethod.Delete, server.BaseUrl + "/v1.0/collections/" + collectionId, token, null, ct);
                            if (delResp.StatusCode != HttpStatusCode.NoContent) throw new Exception("collection delete not 204: " + (int)delResp.StatusCode);
                            HttpResponseMessage readGone = await Send(HttpMethod.Get, server.BaseUrl + "/v1.0/collections/" + collectionId, token, null, ct);
                            if (readGone.StatusCode != HttpStatusCode.NotFound) throw new Exception("deleted collection read should be 404, got " + (int)readGone.StatusCode);
                        }),

                    new TestCaseDescriptor("Collections", "TenantCreate_ProvisionsRecallDb", "Creating a tenant provisions a RecallDB tenant and a default collection",
                        executeAsync: async ct =>
                        {
                            await using TestServer server = await TestServer.CreateAsync(ct);
                            string token = await LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);

                            HttpResponseMessage resp = await Send(HttpMethod.Post, server.BaseUrl + "/v1.0/tenants", token, "{\"name\":\"Acme\"}", ct);
                            if (resp.StatusCode != HttpStatusCode.Created) throw new Exception("tenant create not 201: " + (int)resp.StatusCode);
                            string tenantId = ExtractString(await resp.Content.ReadAsStringAsync(ct), "id");
                            if (String.IsNullOrEmpty(tenantId)) throw new Exception("tenant create returned no id");

                            if (!server.Recall.TenantExists(tenantId)) throw new Exception("a RecallDB tenant was not provisioned for the new Pneuma tenant");
                            List<RecallCollection> collections = await server.Recall.ListCollectionsAsync(tenantId, ct);
                            if (!collections.Exists(c => c.Name == "default")) throw new Exception("a default collection was not provisioned for the new tenant");
                        })
                });
        }

        #region Private-Methods

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

        private static async Task<HttpResponseMessage> Send(HttpMethod method, string url, string? token, string? body, CancellationToken ct)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, url))
            {
                if (!String.IsNullOrEmpty(token)) request.Headers.Add("Authorization", "Bearer " + token);
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                return await _Http.SendAsync(request, ct).ConfigureAwait(false);
            }
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

        #endregion
    }
}
