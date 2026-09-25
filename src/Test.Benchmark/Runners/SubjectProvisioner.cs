namespace Test.Benchmark.Runners
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Client;
    using Test.Benchmark.Datasets;
    using Test.Benchmark.Metrics;
    using Test.Benchmark.Servers;

    /// <summary>
    /// Provisions each corpus as a Pneuma subject with its own collection, serves the documents from the in-process
    /// corpus server, submits them as links, and waits for ingestion. Subject names are deterministic (dataset,
    /// corpus, embedding model, ingest profile, chunk settings, suffix), so a rerun reuses a fully ingested subject;
    /// <c>--reingest</c> rebuilds it. Query-time settings (reranker, rewrite) are applied to the subject on every run.
    /// </summary>
    public class SubjectProvisioner
    {
        #region Private-Members

        private readonly BenchmarkContext _Context;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Benchmark context.</param>
        /// <exception cref="ArgumentNullException">Thrown when the context is null.</exception>
        public SubjectProvisioner(BenchmarkContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Provision every corpus of a dataset.
        /// </summary>
        /// <param name="dataset">Dataset.</param>
        /// <param name="summary">Summary to fill.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The provisioned subjects, one per corpus.</returns>
        public async Task<List<ProvisionedSubject>> ProvisionAsync(BenchmarkDataset dataset, IngestSummary summary, CancellationToken token)
        {
            List<ProvisionedSubject> subjects = new List<ProvisionedSubject>();
            PrometheusSnapshot before = await PrometheusSnapshot.CaptureAsync(_Context.Client, token).ConfigureAwait(false);
            Stopwatch wall = Stopwatch.StartNew();
            foreach (BenchmarkCorpus corpus in dataset.Corpora)
            {
                summary.Documents += corpus.Documents.Count;
                subjects.Add(await ProvisionCorpusAsync(dataset, corpus, summary, token).ConfigureAwait(false));
            }

            summary.WallSeconds = Math.Round(wall.Elapsed.TotalSeconds, 1);
            summary.DocumentsPerSecond = summary.Submitted > 0 && summary.WallSeconds > 0 ? Math.Round(summary.Submitted / summary.WallSeconds, 3) : 0.0;
            PrometheusSnapshot after = await PrometheusSnapshot.CaptureAsync(_Context.Client, token).ConfigureAwait(false);
            foreach (KeyValuePair<string, StageBreakdown> stage in after.Since(before))
            {
                if (stage.Key.StartsWith("ingestion_stage", StringComparison.Ordinal)) summary.Stages[stage.Key] = stage.Value;
            }

            return subjects;
        }

        /// <summary>
        /// The deterministic subject name for a corpus under the current arguments.
        /// </summary>
        /// <param name="dataset">Dataset.</param>
        /// <param name="corpus">Corpus.</param>
        /// <returns>The name.</returns>
        public string SubjectName(BenchmarkDataset dataset, BenchmarkCorpus corpus)
        {
            BenchmarkArguments args = _Context.Arguments;
            StringBuilder name = new StringBuilder("bench-");
            name.Append(Slug(dataset.Name));
            if (!string.Equals(corpus.Id, dataset.Name, StringComparison.OrdinalIgnoreCase)) name.Append('-').Append(Slug(corpus.Id));
            name.Append('-').Append(Slug(_Context.Embedding.Model));
            name.Append('-').Append(_Context.IngestProfile);
            foreach (string option in new string[] { "chunk-strategy", "chunk-max-tokens", "chunk-overlap", "index-summaries" })
            {
                string? value = args.GetOptional(option);
                if (value != null) name.Append('-').Append(Slug(option.Replace("chunk-", string.Empty))).Append(Slug(value));
            }

            string? suffix = args.GetOptional("scope-suffix");
            if (!string.IsNullOrEmpty(suffix)) name.Append('-').Append(Slug(suffix));
            return name.ToString();
        }

        #endregion

        #region Private-Methods

        private async Task<ProvisionedSubject> ProvisionCorpusAsync(BenchmarkDataset dataset, BenchmarkCorpus corpus, IngestSummary summary, CancellationToken token)
        {
            BenchmarkArguments args = _Context.Arguments;
            PneumaClient client = _Context.Client;
            string name = SubjectName(dataset, corpus);
            bool reingest = args.GetFlag("reingest");

            SubjectBody? existing = (await client.SearchSubjectsAsync(name, token).ConfigureAwait(false))
                .FirstOrDefault(s => string.Equals(s.DisplayName, name, StringComparison.Ordinal) && (s.DeletionStatus == null || s.DeletionStatus == "None"));

            ProvisionedSubject provisioned = new ProvisionedSubject { Corpus = corpus, SubjectName = name };
            if (existing != null && !reingest)
            {
                List<LinkInfo> links = await client.ListSubjectLinksAsync(existing.Id!, token).ConfigureAwait(false);
                HashSet<string> wanted = new HashSet<string>(corpus.Documents.Select(d => d.Id), StringComparer.Ordinal);
                int ingested = links.Count(l => string.Equals(l.Status, "Ingested", StringComparison.OrdinalIgnoreCase) && wanted.Contains(CorpusServer.DocumentIdFromUrl(l.Url) ?? string.Empty));
                int failed = links.Count(l => string.Equals(l.Status, "Failed", StringComparison.OrdinalIgnoreCase) && wanted.Contains(CorpusServer.DocumentIdFromUrl(l.Url) ?? string.Empty));
                if (failed > 0 && ingested + failed == corpus.Documents.Count)
                {
                    links = await RetryTransientFailuresAsync(dataset, corpus, existing.Id!, links, token).ConfigureAwait(false);
                    ingested = links.Count(l => string.Equals(l.Status, "Ingested", StringComparison.OrdinalIgnoreCase) && wanted.Contains(CorpusServer.DocumentIdFromUrl(l.Url) ?? string.Empty));
                    failed = links.Count(l => string.Equals(l.Status, "Failed", StringComparison.OrdinalIgnoreCase) && wanted.Contains(CorpusServer.DocumentIdFromUrl(l.Url) ?? string.Empty));
                }

                // Reuse when every document reached a terminal state. Documents that failed deterministically (for
                // example a type the pipeline rejects) would fail again on a rebuild, so they are reported instead.
                if (ingested + failed == corpus.Documents.Count)
                {
                    provisioned.SubjectId = existing.Id!;
                    provisioned.CollectionId = existing.Collection;
                    provisioned.Reused = true;
                    MapLinks(provisioned, links);
                    await MapJobsAsync(provisioned, token).ConfigureAwait(false);
                    await ApplyQuerySettingsAsync(provisioned, token).ConfigureAwait(false);
                    summary.ReusedSubjects++;
                    summary.Succeeded += ingested;
                    foreach (LinkInfo link in links.Where(l => string.Equals(l.Status, "Failed", StringComparison.OrdinalIgnoreCase)))
                    {
                        summary.Failures++;
                        summary.FailuresByStage["earlier run"] = (summary.FailuresByStage.TryGetValue("earlier run", out int count) ? count : 0) + 1;
                        if (summary.FailureSamples.Count < 20) summary.FailureSamples.Add((provisioned.ResolveDocument(link.Id, link.Url) ?? link.Id) + ": " + (link.LastError ?? "failed"));
                    }

                    Console.WriteLine("[ingest] " + name + ": reusing (" + ingested + " documents ingested, " + failed + " failed)");
                    return provisioned;
                }

                Console.WriteLine("[ingest] " + name + ": exists but incomplete (" + ingested + "/" + corpus.Documents.Count + "), rebuilding");
            }

            if (existing != null) await DeleteSubjectAsync(existing, token).ConfigureAwait(false);

            string embeddingRunner = await EnsureRunnerAsync(true, _Context.Embedding, token).ConfigureAwait(false);
            string answerRunner = await EnsureRunnerAsync(false, _Context.Inference, token).ConfigureAwait(false);
            int dimensionality = await ProbeDimensionalityAsync(token).ConfigureAwait(false);
            string ingestRunner = answerRunner;
            if (_Context.IngestProfile == "lean")
            {
                StubModelServer stub = _Context.Stub(dimensionality);
                ingestRunner = await EnsureRunnerAsync(false, new ModelSettings { Format = "ollama", Url = stub.BaseUrl, Model = "stub" }, token).ConfigureAwait(false);
            }

            string collectionName = name + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            CollectionInfo collection = await client.CreateCollectionAsync(collectionName, dimensionality, token).ConfigureAwait(false);

            SubjectBody body = new SubjectBody
            {
                DisplayName = name,
                Type = "Topic",
                Description = "Pneuma benchmark subject for dataset '" + dataset.Name + "', corpus '" + corpus.Id + "' (profile " + _Context.IngestProfile + "). Created by Test.Benchmark.",
                EmbeddingModel = embeddingRunner,
                InferenceModel = ingestRunner,
                Collection = collection.Id,
                ChunkStrategy = args.GetOptional("chunk-strategy"),
                ChunkMaxTokens = args.GetOptional("chunk-max-tokens") != null ? args.GetInt("chunk-max-tokens", 256) : (int?)null,
                ChunkOverlapTokens = args.GetOptional("chunk-overlap") != null ? args.GetInt("chunk-overlap", 32) : (int?)null,
                PublishedForChat = true
            };

            // Cell summaries are chunked and indexed next to content. --index-summaries false turns them off for
            // this subject (via the per-subject summarization threshold, so no LLM summary calls are made), and the
            // lean profile skips them because the stub would only return empty summaries anyway.
            bool summaries = args.GetOptional("index-summaries") == null ? _Context.IngestProfile != "lean" : args.GetFlag("index-summaries");
            if (!summaries) body.ConcurrencyOverrides = new Dictionary<string, int?> { { "summarizationMinCellLength", 100000 } };
            SubjectBody created = await client.CreateSubjectAsync(body, token).ConfigureAwait(false);
            provisioned.SubjectId = created.Id!;
            provisioned.CollectionId = collection.Id;
            Console.WriteLine("[ingest] " + name + ": created subject " + created.Id + ", collection " + collection.Id + " (" + dimensionality + " dims)");

            await SubmitAndWaitAsync(dataset, corpus, provisioned, summary, token).ConfigureAwait(false);

            // The lean profile ingests against the stub; answers still need a real model.
            if (_Context.IngestProfile == "lean")
            {
                SubjectBody? current = await client.GetSubjectAsync(provisioned.SubjectId, token).ConfigureAwait(false);
                if (current != null)
                {
                    current.InferenceModel = answerRunner;
                    await client.UpdateSubjectAsync(current, token).ConfigureAwait(false);
                }
            }

            await MapJobsAsync(provisioned, token).ConfigureAwait(false);
            await ApplyQuerySettingsAsync(provisioned, token).ConfigureAwait(false);
            return provisioned;
        }

        private async Task SubmitAndWaitAsync(BenchmarkDataset dataset, BenchmarkCorpus corpus, ProvisionedSubject provisioned, IngestSummary summary, CancellationToken token)
        {
            PneumaClient client = _Context.Client;
            CorpusServer server = _Context.Corpus();

            // Dated corpora are submitted in date order so ingestion order follows the order facts were recorded.
            List<BenchmarkDocument> ordered = corpus.Documents.Any(d => !string.IsNullOrEmpty(d.Date))
                ? corpus.Documents.OrderBy(d => d.Date ?? string.Empty, StringComparer.Ordinal).ThenBy(d => d.Id, StringComparer.Ordinal).ToList()
                : corpus.Documents;

            foreach (BenchmarkDocument document in ordered)
            {
                string url = server.Publish(Slug(dataset.Name), Slug(corpus.Id), document);
                List<string> labels = new List<string> { document.Category };
                if (document.Labels != null) labels.AddRange(document.Labels.Where(l => !labels.Contains(l)));
                Dictionary<string, string> tags = document.Tags != null ? new Dictionary<string, string>(document.Tags) : new Dictionary<string, string>();
                tags["benchDocId"] = document.Id;
                LinkInfo link = await client.SubmitLinkAsync(provisioned.SubjectId, new SubmitLinkBody
                {
                    Url = url,
                    Title = string.IsNullOrWhiteSpace(document.Title) ? document.Id : document.Title,
                    Labels = labels,
                    Tags = tags
                }, token).ConfigureAwait(false);
                provisioned.DocIdByLinkId[link.Id] = document.Id;
                provisioned.LinkIdByDocId[document.Id] = link.Id;
                summary.Submitted++;
            }

            Console.WriteLine("[ingest] " + provisioned.SubjectName + ": submitted " + ordered.Count + " documents; waiting for ingestion");
            TimeSpan timeout = TimeSpan.FromMinutes(_Context.Arguments.GetInt("ingest-timeout-min", 240));
            Stopwatch sw = Stopwatch.StartNew();
            DateTime lastReport = DateTime.MinValue;
            List<LinkInfo> links = new List<LinkInfo>();
            while (sw.Elapsed < timeout)
            {
                links = await client.ListSubjectLinksAsync(provisioned.SubjectId, token).ConfigureAwait(false);
                int done = links.Count(l => string.Equals(l.Status, "Ingested", StringComparison.OrdinalIgnoreCase));
                int failed = links.Count(l => string.Equals(l.Status, "Failed", StringComparison.OrdinalIgnoreCase));
                if (DateTime.UtcNow - lastReport > TimeSpan.FromSeconds(30))
                {
                    Console.WriteLine("[ingest]   " + done + " ingested, " + failed + " failed, " + (links.Count - done - failed) + " pending (" + Math.Round(sw.Elapsed.TotalMinutes, 1) + " min)");
                    lastReport = DateTime.UtcNow;
                }

                if (done + failed >= ordered.Count) break;
                await Task.Delay(5000, token).ConfigureAwait(false);
            }

            links = await RetryTransientFailuresAsync(dataset, corpus, provisioned.SubjectId, links, token).ConfigureAwait(false);

            Dictionary<string, JobInfo> latestJobByLink = new Dictionary<string, JobInfo>(StringComparer.Ordinal);
            foreach (JobInfo job in await client.ListSubjectJobsAsync(provisioned.SubjectId, token).ConfigureAwait(false))
            {
                if (string.IsNullOrEmpty(job.LinkId)) continue;
                if (!latestJobByLink.TryGetValue(job.LinkId!, out JobInfo? seen) || (job.CreatedUtc ?? DateTime.MinValue) > (seen.CreatedUtc ?? DateTime.MinValue)) latestJobByLink[job.LinkId!] = job;
            }

            foreach (LinkInfo link in links)
            {
                if (string.Equals(link.Status, "Ingested", StringComparison.OrdinalIgnoreCase))
                {
                    summary.Succeeded++;
                    continue;
                }

                summary.Failures++;
                string stage = "timeout";
                string error = link.LastError ?? "not finished within the ingest timeout";
                if (latestJobByLink.TryGetValue(link.Id, out JobInfo? job))
                {
                    stage = string.Equals(link.Status, "Failed", StringComparison.OrdinalIgnoreCase) ? (job.Stage ?? "unknown") : "timeout(" + (job.Stage ?? job.Status ?? "queued") + ")";
                    if (!string.IsNullOrEmpty(job.Error)) error = job.Error!;
                }

                summary.FailuresByStage[stage] = (summary.FailuresByStage.TryGetValue(stage, out int count) ? count : 0) + 1;
                if (summary.FailureSamples.Count < 20) summary.FailureSamples.Add((provisioned.ResolveDocument(link.Id, link.Url) ?? link.Id) + " [" + stage + "]: " + Truncate(error, 240));
            }

            Console.WriteLine("[ingest] " + provisioned.SubjectName + ": " + (links.Count - summary.Failures) + " ingested, " + summary.Failures + " failed in " + Math.Round(sw.Elapsed.TotalMinutes, 1) + " min");
        }

        private async Task<List<LinkInfo>> RetryTransientFailuresAsync(BenchmarkDataset dataset, BenchmarkCorpus corpus, string subjectId, List<LinkInfo> links, CancellationToken token)
        {
            // A shared model gateway or a busy stack can fail a document for reasons unrelated to the document
            // (429, 502, timeouts). Re-ingest those once so a transient blip does not cost a document; failures the
            // pipeline would repeat (type detection, cell extraction) are left for the report.
            if (_Context.Arguments.GetFlag("no-retry")) return links;
            List<LinkInfo> transient = links.Where(l => string.Equals(l.Status, "Failed", StringComparison.OrdinalIgnoreCase) && !IsDeterministicFailure(l.LastError)).ToList();
            if (transient.Count == 0) return links;

            CorpusServer server = _Context.Corpus();
            foreach (BenchmarkDocument document in corpus.Documents) server.Publish(Slug(dataset.Name), Slug(corpus.Id), document);
            Console.WriteLine("[ingest] re-ingesting " + transient.Count + " document(s) that failed transiently");
            int status = await _Context.Client.ReingestLinksAsync(transient.Select(l => l.Id).ToList(), token).ConfigureAwait(false);
            if (status < 200 || status >= 300) return links;

            HashSet<string> retried = new HashSet<string>(transient.Select(l => l.Id), StringComparer.Ordinal);
            Stopwatch sw = Stopwatch.StartNew();
            await Task.Delay(3000, token).ConfigureAwait(false);
            while (sw.Elapsed < TimeSpan.FromMinutes(_Context.Arguments.GetInt("ingest-timeout-min", 240)))
            {
                links = await _Context.Client.ListSubjectLinksAsync(subjectId, token).ConfigureAwait(false);
                if (links.Where(l => retried.Contains(l.Id)).All(l => string.Equals(l.Status, "Ingested", StringComparison.OrdinalIgnoreCase) || string.Equals(l.Status, "Failed", StringComparison.OrdinalIgnoreCase))) break;
                await Task.Delay(5000, token).ConfigureAwait(false);
            }

            int healed = links.Count(l => retried.Contains(l.Id) && string.Equals(l.Status, "Ingested", StringComparison.OrdinalIgnoreCase));
            Console.WriteLine("[ingest] retry healed " + healed + " of " + transient.Count);
            return links;
        }

        private static bool IsDeterministicFailure(string? error)
        {
            if (string.IsNullOrEmpty(error)) return false;
            return error.Contains("Unknown or unsupported document type", StringComparison.OrdinalIgnoreCase)
                || error.Contains("documentatom request", StringComparison.OrdinalIgnoreCase)
                || error.Contains("no extractable", StringComparison.OrdinalIgnoreCase);
        }

        private static void MapLinks(ProvisionedSubject provisioned, List<LinkInfo> links)
        {
            foreach (LinkInfo link in links)
            {
                string? docId = CorpusServer.DocumentIdFromUrl(link.Url);
                if (docId == null) continue;
                provisioned.DocIdByLinkId[link.Id] = docId;
                provisioned.LinkIdByDocId[docId] = link.Id;
            }
        }

        private async Task MapJobsAsync(ProvisionedSubject provisioned, CancellationToken token)
        {
            foreach (JobInfo job in await _Context.Client.ListSubjectJobsAsync(provisioned.SubjectId, token).ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(job.LinkId) && provisioned.DocIdByLinkId.TryGetValue(job.LinkId!, out string? docId)) provisioned.DocIdByJobId[job.Id] = docId;
            }
        }

        private async Task ApplyQuerySettingsAsync(ProvisionedSubject provisioned, CancellationToken token)
        {
            // Reranker and prompt rewrite only affect queries, so they are applied to a reused subject too. Absent
            // options leave the subject as it is.
            BenchmarkArguments args = _Context.Arguments;
            string? rerank = args.GetOptional("rerank");
            string? rewrite = args.GetOptional("rewrite");
            if (rerank == null && rewrite == null) return;

            SubjectBody? subject = await _Context.Client.GetSubjectAsync(provisioned.SubjectId, token).ConfigureAwait(false);
            if (subject == null) return;
            string answerRunner = await EnsureRunnerAsync(false, _Context.Inference, token).ConfigureAwait(false);
            if (rerank != null)
            {
                string mode = rerank.ToLowerInvariant();
                // Keep the server's own enum format: older builds serialize (and only accept) the integer value.
                bool numeric = subject.RerankerType != null && subject.RerankerType.Length > 0 && subject.RerankerType.All(char.IsDigit);
                bool crossEncoder = mode == "cross-encoder";
                subject.RerankerType = numeric ? (crossEncoder ? "1" : "0") : (crossEncoder ? "CrossEncoder" : "LlmListwise");
                subject.RerankingModel = mode == "none" ? null : answerRunner;
            }

            if (rewrite != null) subject.PromptRewriteModel = args.GetFlag("rewrite") ? answerRunner : null;
            await _Context.Client.UpdateSubjectAsync(subject, token).ConfigureAwait(false);
            Console.WriteLine("[ingest] " + provisioned.SubjectName + ": query settings rerank=" + (rerank ?? "unchanged") + " rewrite=" + (rewrite ?? "unchanged"));
        }

        private async Task DeleteSubjectAsync(SubjectBody existing, CancellationToken token)
        {
            Console.WriteLine("[ingest] deleting subject " + existing.DisplayName + " (" + existing.Id + ")");
            await _Context.Client.DeleteSubjectAsync(existing.Id!, token).ConfigureAwait(false);
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromMinutes(10))
            {
                if (await _Context.Client.GetSubjectAsync(existing.Id!, token).ConfigureAwait(false) == null) break;
                await Task.Delay(2000, token).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(existing.Collection))
            {
                try
                {
                    await _Context.Client.DeleteCollectionAsync(existing.Collection!, token).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        private async Task<string> EnsureRunnerAsync(bool embedding, ModelSettings model, CancellationToken token)
        {
            List<ModelRunnerInfo> runners = await _Context.Client.ListModelRunnersAsync(token).ConfigureAwait(false);
            foreach (ModelRunnerInfo runner in runners)
            {
                // Only reuse endpoints the harness created: they carry a generous timeout and a request queue.
                // Seeded endpoints default to a 60 s timeout and no queue, which fails local LLM classification.
                if (runner.Name == null || !runner.Name.StartsWith("bench-", StringComparison.Ordinal)) continue;
                if (!runner.Active || !runner.Serves(embedding)) continue;
                if (!string.Equals(runner.ModelFor(embedding), model.Model, StringComparison.Ordinal)) continue;
                if (!string.Equals(runner.Url().TrimEnd('/'), model.Url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) continue;
                return runner.Id;
            }

            ModelRunnerInfo created = await _Context.Client.CreateModelRunnerAsync(new CreateModelRunnerBody
            {
                Type = embedding ? "Embedding" : "Completion",
                Provider = model.Provider,
                Name = "bench-" + (embedding ? "embed-" : "complete-") + model.Model,
                Model = model.Model,
                Endpoint = model.Url
            }, token).ConfigureAwait(false);
            Console.WriteLine("[ingest] created " + (embedding ? "embedding" : "completion") + " endpoint " + created.Id + " for " + model.Description);
            return created.Id;
        }

        private async Task<int> ProbeDimensionalityAsync(CancellationToken token)
        {
            int configured = _Context.Arguments.GetInt("dimensionality", 0);
            if (configured > 0) return configured;
            using (DirectModelClient embedder = _Context.EmbeddingClient())
            {
                List<float[]> vectors = await embedder.EmbedAsync(new List<string> { "dimension probe" }, 1, token).ConfigureAwait(false);
                if (vectors.Count == 0 || vectors[0].Length == 0) throw new InvalidOperationException("Could not determine the embedding dimensionality of " + _Context.Embedding.Description + ".");
                return vectors[0].Length;
            }
        }

        private static string Slug(string text)
        {
            StringBuilder sb = new StringBuilder();
            bool dash = false;
            foreach (char c in (text ?? string.Empty).ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(c);
                    dash = false;
                }
                else if (!dash && sb.Length > 0)
                {
                    sb.Append('-');
                    dash = true;
                }
            }

            return sb.ToString().Trim('-');
        }

        private static string Truncate(string text, int max)
        {
            return text.Length > max ? text.Substring(0, max) + "..." : text;
        }

        #endregion
    }
}
