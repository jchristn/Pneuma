namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
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
    /// File upload: the files route stores the original bytes as an inline link with its file name and content type, an
    /// external key upserts, limits and invalid input are refused, an unrecognized file is refused at upload when a type
    /// detector is configured, the original is downloadable, request history records a placeholder for binary bodies,
    /// and an uploaded binary file runs type detection in the pipeline.
    /// </summary>
    public static class FileUploadSuite
    {
        private static readonly HttpClient _Http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        /// <summary>Build the suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "FileUpload",
                displayName: "File upload API",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("FileUpload", "Upload_CreatesInlineLinkWithFileName", "Uploading a Markdown file creates an inline link with its file name, type, size, labels, tags, and a queued job",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await ApiClientHelper.LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                byte[] file = Encoding.UTF8.GetBytes("# Notes\n\nHello from a file.");
                                ApiResult up = await UploadAsync(server.BaseUrl + "/v1.0/subjects/" + subjectId + "/files?filename=" + Uri.EscapeDataString("Release notes.md") +
                                    "&labels=one&labels=" + Uri.EscapeDataString("two,three") + "&tag.team=finance", token, file, "text/markdown", ct);
                                Expect(up, 201);
                                Check(up.Body.Contains("\"fileName\":\"Release notes.md\""), "the link carries the file name: " + up.Body);
                                Check(up.Body.Contains("\"title\":\"Release notes.md\""), "the title defaults to the file name: " + up.Body);
                                Check(up.Body.Contains("pneuma-inline://") && up.Body.Contains("/Release%20notes.md"), "the URI ends with the escaped file name: " + up.Body);
                                Check(up.Body.Contains("\"sourceKind\":\"Inline\"") && up.Body.Contains("\"contentType\":\"text/markdown\""), "inline with its content type: " + up.Body);
                                Check(up.Body.Contains("\"sizeBytes\":" + file.Length), "the size is recorded: " + up.Body);
                                Check(up.Body.Contains("\"one\"") && up.Body.Contains("\"two,three\""), "each labels parameter is one label: " + up.Body);
                                Check(up.Body.Contains("\"team\":\"finance\""), "tag.{key} parameters become tags: " + up.Body);
                                Check(up.Body.Contains("\"jobId\":\"job_"), "a job is queued: " + up.Body);
                            }
                        }),

                    new TestCaseDescriptor("FileUpload", "Upload_InfersTypeAndReplacesByKey", "A generic content type is inferred from the extension, and the same externalKey replaces the file",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await ApiClientHelper.LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                string url = server.BaseUrl + "/v1.0/subjects/" + subjectId + "/files?externalKey=handbook&filename=";
                                ApiResult first = await UploadAsync(url + "handbook.md", token, Encoding.UTF8.GetBytes("# One"), "application/octet-stream", ct);
                                Expect(first, 201);
                                Check(first.Body.Contains("\"contentType\":\"text/markdown\""), "octet-stream with .md is recorded as Markdown: " + first.Body);
                                ApiResult second = await UploadAsync(url + "handbook-v2.txt", token, Encoding.UTF8.GetBytes("Two"), null, ct);
                                Expect(second, 200);
                                Check(second.Body.Contains("\"replaced\":true") && second.Body.Contains("\"fileName\":\"handbook-v2.txt\"") && second.Body.Contains("\"contentType\":\"text/plain\""), "the second upload replaces the first: " + second.Body);
                                ApiResult links = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/subjects/" + subjectId + "/links?maxResults=100", token, null, ct);
                                int occurrences = links.Body.Split("\"externalKey\":\"handbook\"").Length - 1;
                                Check(occurrences == 1, "one key means one link, found " + occurrences);
                            }
                        }),

                    new TestCaseDescriptor("FileUpload", "Upload_RejectsInvalidInput", "A missing file name, an empty body, a long key, an unknown subject, and no token are refused",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await ApiClientHelper.LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                string url = server.BaseUrl + "/v1.0/subjects/" + subjectId + "/files";
                                byte[] file = Encoding.UTF8.GetBytes("Hello.");
                                Expect(await UploadAsync(url, token, file, "text/plain", ct), 400);
                                Expect(await UploadAsync(url + "?filename=%20%20", token, file, "text/plain", ct), 400);
                                Expect(await UploadAsync(url + "?filename=a.txt", token, Array.Empty<byte>(), "text/plain", ct), 400);
                                Expect(await UploadAsync(url + "?filename=a.txt&externalKey=" + new string('k', 300), token, file, "text/plain", ct), 400);
                                Expect(await UploadAsync(url + "?filename=" + new string('n', 300) + ".txt", token, file, "text/plain", ct), 400);
                                Expect(await UploadAsync(server.BaseUrl + "/v1.0/subjects/sub_missing/files?filename=a.txt", token, file, "text/plain", ct), 404);
                                Expect(await UploadAsync(url + "?filename=a.txt", null, file, "text/plain", ct), 401);
                            }
                        }),

                    new TestCaseDescriptor("FileUpload", "Source_DownloadsOriginal", "The source route returns the uploaded bytes with their type and file name; download=true makes it an attachment",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await ApiClientHelper.LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                byte[] file = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A, 0x00, 0xFF, 0x10, 0x80 };
                                ApiResult up = await UploadAsync(server.BaseUrl + "/v1.0/subjects/" + subjectId + "/files?filename=" + Uri.EscapeDataString("Q3 résumé.pdf"), token, file, "application/pdf", ct);
                                Expect(up, 201);
                                string linkId = LinkIdOf(up.Body);

                                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, server.BaseUrl + "/v1.0/links/" + linkId + "/source"))
                                {
                                    request.Headers.Add("Authorization", "Bearer " + token);
                                    using (HttpResponseMessage response = await _Http.SendAsync(request, ct))
                                    {
                                        byte[] body = await response.Content.ReadAsByteArrayAsync(ct);
                                        Check((int)response.StatusCode == 200, "source should be 200, got " + (int)response.StatusCode);
                                        Check(body.SequenceEqual(file), "the source is the original bytes");
                                        Check(response.Content.Headers.ContentType?.MediaType == "application/pdf", "the source keeps its type, got " + response.Content.Headers.ContentType);
                                        string disposition = DispositionOf(response);
                                        Check(disposition.StartsWith("inline", StringComparison.Ordinal) && disposition.Contains("filename*=UTF-8''Q3%20r%C3%A9sum%C3%A9.pdf"), "the source names the file: " + disposition);
                                    }
                                }

                                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, server.BaseUrl + "/v1.0/links/" + linkId + "/source?download=true"))
                                {
                                    request.Headers.Add("Authorization", "Bearer " + token);
                                    using (HttpResponseMessage response = await _Http.SendAsync(request, ct))
                                    {
                                        Check(DispositionOf(response).StartsWith("attachment", StringComparison.Ordinal), "download=true sends an attachment: " + DispositionOf(response));
                                    }
                                }
                            }
                        }),

                    new TestCaseDescriptor("FileUpload", "UploadSettings_ReportsLimits", "The upload settings route reports the limits and types, and OpenAPI describes the binary upload body",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await ApiClientHelper.LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                                ApiResult settings = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/ingestion/upload-settings", token, null, ct);
                                Expect(settings, 200);
                                Check(settings.Body.Contains("\"maxUploadBytes\":104857600") && settings.Body.Contains("\"maxFileNameLength\":256"), "the limits are reported: " + settings.Body);
                                Check(settings.Body.Contains("\"documentType\":\"Pdf\"") && settings.Body.Contains("\"docx\""), "the types are reported: " + settings.Body);
                                Expect(await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/ingestion/upload-settings", null, null, ct), 401);

                                ApiResult openApi = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/openapi.json", null, null, ct);
                                Expect(openApi, 200);
                                Check(openApi.Body.Contains("/v1.0/subjects/{subjectId}/files") && openApi.Body.Contains("application/octet-stream") && openApi.Body.Contains("\"binary\""), "OpenAPI describes the upload as a binary body");
                            }
                        }),

                    new TestCaseDescriptor("FileUpload", "RequestHistory_BinaryPlaceholder", "Request history records a binary upload as a size placeholder, not its bytes",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await ApiClientHelper.LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                byte[] file = Encoding.ASCII.GetBytes("%PDF-1.4 secret-marker");
                                Expect(await UploadAsync(server.BaseUrl + "/v1.0/subjects/" + subjectId + "/files?filename=a.pdf", token, file, "application/pdf", ct), 201);

                                string? recorded = null;
                                for (int attempt = 0; attempt < 30 && recorded == null; attempt++)
                                {
                                    RequestHistoryPage page = await server.Database.RequestHistory.EnumerateAsync(new RequestHistoryFilter { PathContains = "/files", PageSize = 50 }, ct);
                                    foreach (RequestHistoryEntry entry in page.Items.Where(e => e.Method == "POST"))
                                    {
                                        RequestHistoryEntry? full = await server.Database.RequestHistory.ReadAsync(null, entry.Id, ct);
                                        if (full?.RequestBody != null) recorded = full.RequestBody;
                                    }
                                    if (recorded == null) await Task.Delay(100, ct);
                                }

                                Check(recorded != null, "the upload should be captured");
                                Check(recorded == "[binary " + file.Length + " bytes]", "the body should be a placeholder: " + recorded);
                            }
                        }),

                    new TestCaseDescriptor("FileUpload", "Service_EnforcesSizeAndDetection", "Over the limit is 413, an unrecognized or unextractable file is 415 and nothing is stored, and an unreachable detector still accepts",
                        executeAsync: async ct =>
                        {
                            await using (IngestionHarness h = await IngestionHarness.CreateAsync(null, ct))
                            {
                                Subject subject = await ConfiguredSubjectAsync(h, ct);
                                byte[] file = Encoding.ASCII.GetBytes("PK\u0003\u0004 not really a document");

                                ContentSubmissionService small = new ContentSubmissionService(h.Db, h.Blobs, new IngestionSettings { MaxUploadBytes = 1024 });
                                ContentSubmitResult big = await small.SubmitFileAsync(null, subject, new SubmitFileRequest { FileName = "big.bin" }, new byte[2000], ct);
                                Check(big.StatusCode == 413, "an oversized file should be 413, got " + big.StatusCode);

                                ContentSubmissionService unknown = new ContentSubmissionService(h.Db, h.Blobs, new IngestionSettings(), new FakeDocumentAtomClient("Unknown"));
                                ContentSubmitResult refused = await unknown.SubmitFileAsync(null, subject, new SubmitFileRequest { FileName = "mystery.bin" }, file, ct);
                                Check(refused.StatusCode == 415 && refused.Link == null, "an unrecognized file should be 415, got " + refused.StatusCode + " " + refused.Error);

                                ContentSubmissionService zip = new ContentSubmissionService(h.Db, h.Blobs, new IngestionSettings(), new FakeDocumentAtomClient("Zip"));
                                ContentSubmitResult archive = await zip.SubmitFileAsync(null, subject, new SubmitFileRequest { FileName = "files.zip" }, file, ct);
                                Check(archive.StatusCode == 415 && (archive.Error ?? "").Contains("Zip"), "a type with no extraction route should be 415: " + archive.Error);

                                List<SubjectLink> links = await h.Db.SubjectLinks.EnumerateBySubjectAsync(h.TenantId, subject.Id, ct);
                                Check(!links.Any(l => l.SourceKind == SourceKindEnum.Inline), "a refused file must not create a link");

                                ThrowingAtomizer down = new ThrowingAtomizer();
                                ContentSubmissionService offline = new ContentSubmissionService(h.Db, h.Blobs, new IngestionSettings(), down);
                                ContentSubmitResult accepted = await offline.SubmitFileAsync(null, subject, new SubmitFileRequest { FileName = "report.pdf" }, file, ct);
                                Check(accepted.StatusCode == 201 && down.DetectCalls == 1, "an unreachable detector should not block the upload, got " + accepted.StatusCode);

                                ContentSubmitResult text = await unknown.SubmitFileAsync(null, subject, new SubmitFileRequest { FileName = "notes.md" }, Encoding.UTF8.GetBytes("# Notes"), ct);
                                Check(text.StatusCode == 201 && text.Link!.ContentType == "text/markdown", "a text-family file keeps its declared type without detection, got " + text.StatusCode);
                            }
                        }),

                    new TestCaseDescriptor("FileUpload", "Pipeline_BinaryFileRunsTypeDetection", "An uploaded binary file is type-detected by the job and ingests with the detected type",
                        executeAsync: async ct =>
                        {
                            await using (IngestionHarness h = await IngestionHarness.CreateAsync(null, ct))
                            {
                                Subject subject = await ConfiguredSubjectAsync(h, ct);
                                ContentSubmissionService service = new ContentSubmissionService(h.Db, h.Blobs, new IngestionSettings(), new FakeDocumentAtomClient("Pdf"));
                                byte[] file = Encoding.UTF8.GetBytes("Quarterly results improved. Revenue grew in every region.");
                                ContentSubmitResult up = await service.SubmitFileAsync(null, subject, new SubmitFileRequest { FileName = "results.pdf", ContentType = "application/pdf" }, file, ct);
                                Check(up.StatusCode == 201, "upload should be 201, got " + up.StatusCode + " " + up.Error);

                                string jobId = await h.RunQueuedAsync(new FakeSemanticProcessor(), ct, new FakeDocumentAtomClient("Pdf"));
                                IngestionJob job = await h.Db.IngestionJobs.ReadAsync(h.TenantId, jobId, ct) ?? throw new Exception("job gone");
                                Check(job.Status == IngestionStatusEnum.Completed, "expected Completed, got " + job.Status + " (" + job.Error + ")");
                                Check(job.DocumentType == "Pdf", "the detected type should be used, got " + job.DocumentType);
                                Check(h.ChunkCount(jobId) > 0, "the uploaded file should be indexed");
                                Check(job.SourceUrl.EndsWith("/results.pdf", StringComparison.Ordinal), "the job's source names the file: " + job.SourceUrl);
                            }
                        }),

                    new TestCaseDescriptor("FileUpload", "UploadFileTypes_NormalizesAndInfers", "File names lose their path and control characters, and content types are inferred from extensions",
                        executeAsync: ct =>
                        {
                            Check(UploadFileTypes.NormalizeFileName("C:\\docs\\a\\Report.PDF") == "Report.PDF", "a Windows path is reduced to its name");
                            Check(UploadFileTypes.NormalizeFileName("x/y/../notes.md") == "notes.md", "a Unix path is reduced to its name");
                            Check(UploadFileTypes.NormalizeFileName(" a\u0001b.txt ") == "ab.txt", "control characters and padding are removed");
                            Check(UploadFileTypes.NormalizeFileName("dir/") == null && UploadFileTypes.NormalizeFileName("..") == null && UploadFileTypes.NormalizeFileName(null) == null, "nothing usable is null");
                            Check(UploadFileTypes.ResolveContentType(null, "Report.PDF") == "application/pdf", "a missing type is inferred from the extension");
                            Check(UploadFileTypes.ResolveContentType("application/octet-stream", "deck.pptx") == "application/vnd.openxmlformats-officedocument.presentationml.presentation", "octet-stream is inferred");
                            Check(UploadFileTypes.ResolveContentType("Text/Markdown; charset=utf-8", "x.bin") == "text/markdown", "a declared type wins, without parameters");
                            Check(UploadFileTypes.ResolveContentType(null, "archive.xyz") == "application/octet-stream", "an unknown extension stays generic");
                            Check(InlineContentKeys.UriFor("lnk_1", "a b.pdf") == "pneuma-inline://lnk_1/a%20b.pdf" && InlineContentKeys.UriFor("lnk_1", null) == "pneuma-inline://lnk_1", "the inline URI carries the escaped name");
                            return Task.CompletedTask;
                        })
                });
        }

        private static async Task<ApiResult> UploadAsync(string url, string? token, byte[] data, string? contentType, CancellationToken ct)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                if (!String.IsNullOrEmpty(token)) request.Headers.Add("Authorization", "Bearer " + token);
                ByteArrayContent content = new ByteArrayContent(data);
                if (contentType != null) content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                request.Content = content;
                using (HttpResponseMessage response = await _Http.SendAsync(request, ct).ConfigureAwait(false))
                {
                    string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    return new ApiResult { StatusCode = (int)response.StatusCode, Body = text };
                }
            }
        }

        private static string DispositionOf(HttpResponseMessage response)
        {
            if (response.Content.Headers.TryGetValues("Content-Disposition", out IEnumerable<string>? values)) return String.Join(",", values);
            if (response.Headers.TryGetValues("Content-Disposition", out IEnumerable<string>? other)) return String.Join(",", other);
            return String.Empty;
        }

        private static string LinkIdOf(string body)
        {
            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(body))
            {
                return doc.RootElement.GetProperty("link").GetProperty("id").GetString() ?? throw new Exception("no link id: " + body);
            }
        }

        private static void Expect(ApiResult result, int status)
        {
            if (result.StatusCode != status) throw new Exception("expected " + status + ", got " + result.StatusCode + " " + result.Body);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
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
