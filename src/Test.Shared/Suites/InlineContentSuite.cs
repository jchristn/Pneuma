namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Ingestion.Configuration;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Server.Services;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Pushed content: the content routes and MCP tool create inline links whose content lives in the blob store, a
    /// declared content type skips type detection, an external key upserts, batches report per item, and limits and
    /// invalid input are refused.
    /// </summary>
    public static class InlineContentSuite
    {
        private const string _Markdown = "# Release notes\n\nVersion 2 adds crawl plans.\n\n## Fixes\n\nRe-ingest replaces the old version.";

        /// <summary>Build the suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "InlineContent",
                displayName: "Inline content push API",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("InlineContent", "Push_CreatesInlineLinkAndJob", "Pushing Markdown creates an inline link with its content type and queues a job",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await ApiClientHelper.LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                ApiResult pushed = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/content", token,
                                    "{\"title\":\"Release notes\",\"content\":\"# Notes\\n\\nHello.\",\"contentType\":\"text/markdown\",\"labels\":[\"release\"]}", ct);
                                if (pushed.StatusCode != 201) throw new Exception("expected 201, got " + pushed.StatusCode + " " + pushed.Body);
                                if (!pushed.Body.Contains("\"sourceKind\":\"Inline\"") || !pushed.Body.Contains("pneuma-inline://") || !pushed.Body.Contains("text/markdown")) throw new Exception("the link should be inline with its content type: " + pushed.Body);
                                if (!pushed.Body.Contains("\"jobId\":\"job_")) throw new Exception("a job should be queued: " + pushed.Body);
                            }
                        }),

                    new TestCaseDescriptor("InlineContent", "Push_SameKeyReplaces", "Pushing twice with the same externalKey replaces the content on one link",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await ApiClientHelper.LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                string url = server.BaseUrl + "/v1.0/subjects/" + subjectId + "/content";
                                ApiResult first = await ApiClientHelper.CallAsync(HttpMethod.Post, url, token, "{\"content\":\"First.\",\"contentType\":\"text/plain\",\"externalKey\":\"notes-1\"}", ct);
                                ApiResult second = await ApiClientHelper.CallAsync(HttpMethod.Post, url, token, "{\"content\":\"Second.\",\"contentType\":\"text/plain\",\"externalKey\":\"notes-1\"}", ct);
                                if (first.StatusCode != 201 || second.StatusCode != 200) throw new Exception("expected 201 then 200, got " + first.StatusCode + " then " + second.StatusCode);
                                if (!second.Body.Contains("\"replaced\":true")) throw new Exception("the second push should report a replacement");
                                ApiResult links = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/links?maxResults=100", token, null, ct);
                                int occurrences = links.Body.Split("\"externalKey\":\"notes-1\"").Length - 1;
                                if (occurrences != 1) throw new Exception("one key means one link, found " + occurrences);
                            }
                        }),

                    new TestCaseDescriptor("InlineContent", "Batch_ReportsPerItem", "A batch with one invalid item creates the valid ones and reports each item",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await ApiClientHelper.LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                string body = "{\"items\":[" +
                                    "{\"content\":\"One.\",\"contentType\":\"text/plain\"}," +
                                    "{\"content\":\"{\\\"a\\\":1}\",\"contentType\":\"application/json\"}," +
                                    "{\"content\":\"Bad type.\",\"contentType\":\"application/pdf\"}," +
                                    "{\"content\":\"<p>Three.</p>\",\"contentType\":\"text/html\"}]}";
                                ApiResult result = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/content/batch", token, body, ct);
                                if (result.StatusCode != 200) throw new Exception("expected 200, got " + result.StatusCode + " " + result.Body);
                                if (!result.Body.Contains("\"accepted\":3") || !result.Body.Contains("\"rejected\":1")) throw new Exception("expected 3 accepted and 1 rejected: " + result.Body);
                                if (!result.Body.Contains("\"index\":2") || !result.Body.Contains("application/pdf")) throw new Exception("the rejected item should be reported with its index and reason");
                            }
                        }),

                    new TestCaseDescriptor("InlineContent", "Push_RejectsInvalidInput", "Empty content, an unsupported type, an oversized batch, and a missing subject are refused",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await ApiClientHelper.LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                string url = server.BaseUrl + "/v1.0/subjects/" + subjectId + "/content";
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, url, token, "{\"content\":\"   \",\"contentType\":\"text/plain\"}", ct), 400);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, url, token, "{\"content\":\"x\",\"contentType\":\"application/pdf\"}", ct), 400);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, url, token, "{\"content\":\"x\",\"contentType\":\"text/plain\",\"externalKey\":\"" + new string('k', 300) + "\"}", ct), 400);
                                string many = "{\"items\":[" + String.Join(",", Enumerable.Range(0, 101).Select(i => "{\"content\":\"x\",\"contentType\":\"text/plain\"}")) + "]}";
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, url + "/batch", token, many, ct), 400);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/v1.0/subjects/sub_missing/content", token, "{\"content\":\"x\",\"contentType\":\"text/plain\"}", ct), 404);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Post, url, null, "{\"content\":\"x\",\"contentType\":\"text/plain\"}", ct), 401);
                            }
                        }),

                    new TestCaseDescriptor("InlineContent", "Service_EnforcesSizeAndCleansText", "The size limit returns 413, a lone surrogate is stored as U+FFFD, and the same key on another subject is a separate link",
                        executeAsync: async ct =>
                        {
                            await using (IngestionHarness h = await IngestionHarness.CreateAsync(null, ct))
                            {
                                Subject subject = await ConfiguredSubjectAsync(h, ct);
                                ContentSubmissionService service = new ContentSubmissionService(h.Db, h.Blobs, new IngestionSettings { MaxInlineContentBytes = 1024 });
                                ContentSubmitResult big = await service.SubmitAsync(null, subject, new SubmitContentRequest { Content = new string('x', 2000), ContentType = "text/plain" }, 0, ct);
                                if (big.StatusCode != 413) throw new Exception("oversized content should be 413, got " + big.StatusCode);

                                ContentSubmitResult dirty = await service.SubmitAsync(null, subject, new SubmitContentRequest { Content = "bad \uD800 surrogate\u0000", ContentType = "text/plain", ExternalKey = "k" }, 0, ct);
                                byte[]? stored = await h.Blobs.ReadAsync(InlineContentKeys.KeyFor(h.TenantId, dirty.Link!.Id), ct);
                                string text = Encoding.UTF8.GetString(stored ?? Array.Empty<byte>());
                                if (!text.Contains('�') || text.Contains('\0')) throw new Exception("the stored text should have U+FFFD and no NUL: " + text);

                                Subject other = await h.Db.Subjects.CreateAsync(new Subject { TenantId = h.TenantId, DisplayName = "Other", Collection = h.CollectionId, EmbeddingModel = "e", InferenceModel = "i" }, ct);
                                ContentSubmitResult elsewhere = await service.SubmitAsync(null, other, new SubmitContentRequest { Content = "Same key.", ContentType = "text/plain", ExternalKey = "k" }, 0, ct);
                                if (elsewhere.StatusCode != 201 || elsewhere.Link!.Id == dirty.Link.Id) throw new Exception("the same key on another subject must create a separate link");
                            }
                        }),

                    new TestCaseDescriptor("InlineContent", "Pipeline_DeclaredTypeSkipsDetection", "Pushed Markdown ingests without type detection (a detector that says Unknown is never consulted)",
                        executeAsync: async ct =>
                        {
                            await using (IngestionHarness h = await IngestionHarness.CreateAsync(null, ct))
                            {
                                Subject subject = await ConfiguredSubjectAsync(h, ct);
                                ContentSubmissionService service = new ContentSubmissionService(h.Db, h.Blobs, new IngestionSettings());
                                ContentSubmitResult pushed = await service.SubmitAsync(null, subject, new SubmitContentRequest { Title = "Release notes", Content = _Markdown, ContentType = "text/markdown" }, 0, ct);
                                string jobId = await h.RunQueuedAsync(new FakeSemanticProcessor(), ct, new FakeDocumentAtomClient("Unknown"));
                                IngestionJob job = await h.Db.IngestionJobs.ReadAsync(h.TenantId, jobId, ct) ?? throw new Exception("job gone");
                                if (job.Status != IngestionStatusEnum.Completed) throw new Exception("expected Completed, got " + job.Status + " (" + job.Error + ")");
                                if (job.DocumentType != "Markdown") throw new Exception("the declared type should be used, got " + job.DocumentType);
                                if (h.ChunkCount(jobId) == 0) throw new Exception("the pushed content should be indexed");
                                if (pushed.Link!.SourceKind != SourceKindEnum.Inline) throw new Exception("the link should be inline");
                            }
                        }),

                    new TestCaseDescriptor("InlineContent", "Mcp_SubmitContent", "The MCP pneuma_submit_content tool creates content and refuses an unknown subject",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await ApiClientHelper.LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                string call = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"pneuma_submit_content\",\"arguments\":{\"subjectId\":\"" + subjectId +
                                    "\",\"content\":\"Agent notes.\",\"contentType\":\"text/plain\",\"externalKey\":\"agent-1\"}}}";
                                ApiResult ok = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/mcp", token, call, ct);
                                if (!ok.Body.Contains("created") || !ok.Body.Contains("lnk_")) throw new Exception("the tool should create content: " + ok.Body);

                                string missing = call.Replace(subjectId, "sub_missing");
                                ApiResult bad = await ApiClientHelper.CallAsync(HttpMethod.Post, server.BaseUrl + "/mcp", token, missing, ct);
                                if (!bad.Body.Contains("\"error\"")) throw new Exception("an unknown subject should be an error: " + bad.Body);
                            }
                        }),

                    new TestCaseDescriptor("InlineContent", "TextSanitizer_KeepsPairsAndDropsControls", "Valid emoji survive, lone surrogates become U+FFFD, and control characters other than whitespace are removed",
                        executeAsync: ct =>
                        {
                            string emoji = "ok \U0001F680 done\t\n";
                            if (TextSanitizer.Clean(emoji) != emoji) throw new Exception("valid text must be unchanged");
                            if (TextSanitizer.Clean("a\uDC00b") != "a�b") throw new Exception("a lone low surrogate should become U+FFFD");
                            if (TextSanitizer.Clean("a\u0001b\u0000c") != "abc") throw new Exception("control characters should be removed");
                            if (TextSanitizer.Clean(null) != null) throw new Exception("null stays null");
                            return Task.CompletedTask;
                        })
                });
        }

        private static void Expect(ApiResult result, int status)
        {
            if (result.StatusCode != status) throw new Exception("expected " + status + ", got " + result.StatusCode + " " + result.Body);
        }

        private static async Task<Subject> ConfiguredSubjectAsync(IngestionHarness h, CancellationToken ct)
        {
            Subject subject = await h.Db.Subjects.ReadAsync(h.TenantId, h.SubjectId, ct) ?? throw new Exception("subject gone");
            subject.Collection = h.CollectionId;
            subject.EmbeddingModel = "stub-embedding";
            subject.InferenceModel = "stub-inference";
            return await h.Db.Subjects.UpdateAsync(subject, ct);
        }
    }
}
