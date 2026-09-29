namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Ingestion.Stages;
    using Pneuma.Core.Integrations.Implementations;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Security;
    using SyslogLogging;
    using Test.Shared.Support;
    using TextChunker.Tokenization;
    using Touchstone.Core;

    /// <summary>
    /// Chunk context and budgets: heading paths from DocumentAtom, context headers on the embedded text only, chunks that
    /// fit the model's input limit with a margin, and re-chunking when the model rejects a chunk as too long.
    /// </summary>
    public static class ChunkContextSuite
    {
        private const string _LongText =
            "Pneuma turns documents into a knowledge graph and a search index. Each document is fetched, typed, split into cells, " +
            "classified into the subject's ontology, merged into the graph, summarized, chunked, embedded, and indexed. " +
            "Operators watch every stage in the job log, and failures carry a category that says whether a retry could help. " +
            "Crawlers keep subjects current by enumerating web sites, sitemaps, buckets, and file shares on a schedule, " +
            "comparing each object with what they saw last time, and ingesting only what changed. Retrieval blends keyword and " +
            "semantic search and can expand to neighboring graph nodes before an answer is written with citations.";

        /// <summary>Build the suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "ChunkContext",
                displayName: "Chunk headers, model budgets, and re-chunking",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("ChunkContext", "Atoms_CarryHeadingPaths", "Cells carry the heading path above them, and heading cells record their level",
                        executeAsync: async ct =>
                        {
                            string atomsJson = "[{\"Type\":\"Text\",\"HeaderLevel\":1,\"Text\":\"# Guide\",\"Quarks\":[" +
                                "{\"Type\":\"Text\",\"Text\":\"Intro paragraph.\"}," +
                                "{\"Type\":\"Text\",\"HeaderLevel\":2,\"Text\":\"## Install\"}," +
                                "{\"Type\":\"Text\",\"Text\":\"Run the installer.\"}," +
                                "{\"Type\":\"Text\",\"HeaderLevel\":3,\"Text\":\"### Linux\"}," +
                                "{\"Type\":\"Text\",\"Text\":\"Use apt.\"}," +
                                "{\"Type\":\"Text\",\"HeaderLevel\":2,\"Text\":\"## Upgrade\"}," +
                                "{\"Type\":\"Text\",\"Text\":\"Pull the new image.\"}]}]";
                            using (DocumentAtomClient client = new DocumentAtomClient("http://127.0.0.1:8000", retryCount: 0, handler: new RecordingHttpMessageHandler(atomsJson)))
                            {
                                List<ExtractedCell> cells = await client.ExtractCellsAsync("Markdown", new byte[] { 1 }, ct);
                                ExtractedCell apt = cells.First(c => c.Text == "Use apt.");
                                if (apt.HeadingPath != "Guide > Install > Linux") throw new Exception("unexpected path for 'Use apt.': " + apt.HeadingPath);
                                ExtractedCell pull = cells.First(c => c.Text == "Pull the new image.");
                                if (pull.HeadingPath != "Guide > Upgrade") throw new Exception("a same-level heading must replace the deeper ones: " + pull.HeadingPath);
                                ExtractedCell title = cells.First(c => c.Text == "# Guide");
                                if (title.HeaderLevel != 1) throw new Exception("the title heading should record level 1");
                            }
                        }),

                    new TestCaseDescriptor("ChunkContext", "Header_IsEmbeddedNotStored", "A context header is in the embedded text of every chunk and never in the stored text",
                        executeAsync: async ct =>
                        {
                            List<SemanticChunk> chunks = await ChunkAsync(_LongText, new ChunkingOptions { MaxTokens = 48, OverlapCount = 0, ContextHeader = "Pneuma Guide > Architecture" }, ct);
                            if (chunks.Count < 2) throw new Exception("expected several chunks");
                            foreach (SemanticChunk chunk in chunks)
                            {
                                if (chunk.Text.Contains("Pneuma Guide > Architecture")) throw new Exception("the stored text must not contain the header");
                                if (chunk.EmbeddingText == null || !chunk.EmbeddingText.StartsWith("Pneuma Guide > Architecture", StringComparison.Ordinal)) throw new Exception("the embedded text should start with the header");
                                if (!_LongText.Contains(chunk.Text.Substring(0, Math.Min(20, chunk.Text.Length)))) throw new Exception("the stored text should come from the source");
                            }
                        }),

                    new TestCaseDescriptor("ChunkContext", "Header_AndChunk_FitModelBudget", "With a 100-token model limit, header plus chunk stays within the limit in the model's own tokens",
                        executeAsync: async ct =>
                        {
                            ChunkingOptions options = new ChunkingOptions { MaxTokens = 8192, OverlapCount = 0, ModelId = "nomic-embed-text", EffectiveInputBudget = 100, ContextHeader = "Pneuma Guide > Architecture > Pipeline" };
                            List<SemanticChunk> chunks = await ChunkAsync(_LongText + " " + _LongText, options, ct);
                            BertWordPieceTokenizerAdapter wordPiece = new BertWordPieceTokenizerAdapter();
                            foreach (SemanticChunk chunk in chunks)
                            {
                                int count = wordPiece.CountTokens(chunk.EmbeddingText ?? chunk.Text);
                                if (count > 100) throw new Exception("header plus chunk has " + count + " WordPiece tokens; the limit is 100");
                            }
                        }),

                    new TestCaseDescriptor("ChunkContext", "NoHeader_EmbedsBareText", "Without a header, chunks have no separate embedded text",
                        executeAsync: async ct =>
                        {
                            List<SemanticChunk> chunks = await ChunkAsync(_LongText, new ChunkingOptions { MaxTokens = 64, OverlapCount = 0 }, ct);
                            if (chunks.Any(c => c.EmbeddingText != null)) throw new Exception("no header means no separate embedded text");
                        }),

                    new TestCaseDescriptor("ChunkContext", "BuildHeader_ModesAndTruncation", "Headers follow the mode, avoid repeating the title, and are cut at a word boundary",
                        executeAsync: ct =>
                        {
                            if (ChunkingStage.BuildHeader(ChunkHeaderModeEnum.None, "Title", "A > B", 256, 0.25) != null) throw new Exception("None means no header");
                            if (ChunkingStage.BuildHeader(ChunkHeaderModeEnum.Title, "Guide", "Guide > Install", 256, 0.25) != "Guide") throw new Exception("Title mode uses the title only");
                            if (ChunkingStage.BuildHeader(ChunkHeaderModeEnum.TitleAndHeadings, "Guide", "Guide > Install", 256, 0.25) != "Guide > Install") throw new Exception("a path that starts with the title must not repeat it");
                            if (ChunkingStage.BuildHeader(ChunkHeaderModeEnum.TitleAndHeadings, "Manual", "Install", 256, 0.25) != "Manual > Install") throw new Exception("title and path should join");
                            if (ChunkingStage.BuildHeader(ChunkHeaderModeEnum.TitleAndHeadings, null, null, 256, 0.25) != null) throw new Exception("nothing to say means no header");
                            string longTitle = String.Join(" ", Enumerable.Repeat("word", 200));
                            string? cut = ChunkingStage.BuildHeader(ChunkHeaderModeEnum.Title, longTitle, null, 64, 0.25);
                            if (cut == null || cut.Length > 64 || cut.EndsWith(" ", StringComparison.Ordinal) || !cut.EndsWith("word", StringComparison.Ordinal)) throw new Exception("a long header should be cut at a word boundary within its share: '" + cut + "'");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("ChunkContext", "Ingest_RechunksWhenModelRejectsLength", "A chunk the embedding model rejects as too long is re-chunked smaller and the job completes",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer stub = new StubModelServer())
                            {
                                stub.MaxInputCharacters = 700;
                                await using (IngestionHarness h = await IngestionHarness.CreateAsync(null, ct))
                                {
                                    ModelRunner runner = await CreateEmbeddingRunnerAsync(h.Db, stub.BaseUrl, ct);
                                    h.EmbeddingEndpointId = runner.Id;
                                    string jobId = await h.IngestAsync(new FakeContentFetcher(_LongText + " " + _LongText), Processor(h.Db), ct);
                                    IngestionJob job = await h.Db.IngestionJobs.ReadAsync(h.TenantId, jobId, ct) ?? throw new Exception("job gone");
                                    if (job.Status != IngestionStatusEnum.Completed) throw new Exception("expected Completed, got " + job.Status + " (" + job.Error + ")");
                                    if (job.Warnings.Any(w => w.Contains("too long"))) throw new Exception("the smaller chunks should have fit: " + String.Join("; ", job.Warnings));
                                    if (h.ChunkCount(jobId) < 2) throw new Exception("the rejected chunk should have been split into several");
                                }
                            }
                        }),

                    new TestCaseDescriptor("ChunkContext", "Ingest_DropsChunkRejectedAtSmallestScale", "A chunk the model rejects even at 30% of the size is left out with a warning",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer stub = new StubModelServer())
                            {
                                stub.MaxInputCharacters = 20;
                                await using (IngestionHarness h = await IngestionHarness.CreateAsync(null, ct))
                                {
                                    ModelRunner runner = await CreateEmbeddingRunnerAsync(h.Db, stub.BaseUrl, ct);
                                    h.EmbeddingEndpointId = runner.Id;
                                    string jobId = await h.IngestAsync(new FakeContentFetcher(_LongText), Processor(h.Db), ct);
                                    IngestionJob job = await h.Db.IngestionJobs.ReadAsync(h.TenantId, jobId, ct) ?? throw new Exception("job gone");
                                    if (!job.Warnings.Any(w => w.Contains("too long"))) throw new Exception("expected a too-long warning, got: " + String.Join("; ", job.Warnings) + " status " + job.Status + " " + job.Error);
                                }
                            }
                        }),

                    new TestCaseDescriptor("ChunkContext", "NonContextRejection_IsNotRechunked", "A 400 that is not a context-length error fails the job as ModelRejected without re-chunking",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer stub = new StubModelServer())
                            {
                                stub.FailureStatus = 400;
                                stub.FailNext(1000);
                                await using (IngestionHarness h = await IngestionHarness.CreateAsync(null, ct))
                                {
                                    ModelRunner runner = await CreateEmbeddingRunnerAsync(h.Db, stub.BaseUrl, ct);
                                    h.EmbeddingEndpointId = runner.Id;
                                    string jobId = await h.IngestAsync(new FakeContentFetcher(_LongText), Processor(h.Db), ct);
                                    IngestionJob job = await h.Db.IngestionJobs.ReadAsync(h.TenantId, jobId, ct) ?? throw new Exception("job gone");
                                    if (job.Status != IngestionStatusEnum.Failed || job.FailureCategory != IngestionFailureCategoryEnum.ModelRejected) throw new Exception("expected Failed/ModelRejected, got " + job.Status + "/" + job.FailureCategory);
                                    List<IngestionJobAttempt> attempts = await h.Db.IngestionJobAttempts.EnumerateByJobAsync(h.TenantId, jobId, ct);
                                    if (attempts.Count != 1) throw new Exception("a rejected request must not be retried; attempts " + attempts.Count);
                                }
                            }
                        })
                });
        }

        private static async Task<List<SemanticChunk>> ChunkAsync(string text, ChunkingOptions options, CancellationToken ct)
        {
            await using (DatabaseDriverBase db = await TestDatabase.CreateAsync(ct))
            {
                return await Processor(db).ChunkAsync(text, options, ct);
            }
        }

        private static NativeSemanticProcessor Processor(DatabaseDriverBase db)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return new NativeSemanticProcessor(db, new Aes256Cipher("test-signing-key"), logging);
        }

        private static async Task<ModelRunner> CreateEmbeddingRunnerAsync(DatabaseDriverBase db, string baseUrl, CancellationToken ct)
        {
            return await db.ModelRunners.CreateAsync(new ModelRunner
            {
                Name = "stub-embed-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Provider = ModelRunnerProviderEnum.Ollama,
                BaseUrl = baseUrl,
                ApiType = "Ollama",
                Capabilities = new List<ModelCapabilityEnum> { ModelCapabilityEnum.Embedding },
                DefaultEmbeddingModel = "stub",
                MaxRetries = 0,
                MaxConcurrentRequests = 4,
                Active = true,
                HealthCheckEnabled = false
            }, ct);
        }
    }
}
