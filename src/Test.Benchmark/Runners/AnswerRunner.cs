namespace Test.Benchmark.Runners
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Client;
    using Test.Benchmark.Datasets;
    using Test.Benchmark.Metrics;

    /// <summary>
    /// End-to-end answering benchmark over Pneuma's grounded query (<c>/v1.0/query</c>) or agentic chat
    /// (<c>/v1.0/chat/stream</c>): LLM-judged accuracy, correct declines on unanswerable questions, whether the
    /// evidence reached the model (reported separately so retrieval misses are told apart from generation misses),
    /// citation precision and recall, and optional per-claim faithfulness. <c>--repeat</c> reruns the whole set to
    /// expose judge and generation variance.
    /// </summary>
    public class AnswerRunner
    {
        #region Private-Members

        private static readonly Regex _Citation = new Regex("\\[(\\d+(?:\\s*[,;-]\\s*\\d+)*)\\]", RegexOptions.Compiled);
        private static readonly string[] _FallbackAnswers = new string[]
        {
            "An answer could not be generated",
            "No answering model is configured"
        };
        private readonly BenchmarkContext _Context;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Benchmark context.</param>
        /// <exception cref="ArgumentNullException">Thrown when the context is null.</exception>
        public AnswerRunner(BenchmarkContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the benchmark.
        /// </summary>
        /// <param name="dataset">Dataset.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The report.</returns>
        public async Task<AnswerReport> RunAsync(BenchmarkDataset dataset, CancellationToken token)
        {
            BenchmarkArguments args = _Context.Arguments;
            string endpoint = args.Get("endpoint", "query").ToLowerInvariant();
            if (endpoint != "query" && endpoint != "chat") throw new ArgumentException("--endpoint must be query or chat.");
            int repeat = Math.Clamp(args.GetInt("repeat", 1), 1, 10);
            int concurrency = Math.Clamp(args.GetInt("concurrency", 2), 1, 16);
            int limit = args.GetInt("limit", 0);
            int? maxResults = args.GetOptional("k") != null ? args.GetInt("k", 8) : (int?)null;
            bool faithfulness = args.GetFlag("faithfulness");
            bool useFilter = !args.GetFlag("no-filter");
            ModelSettings judgeSettings = ModelSettings.FromArguments(args, "judge", _Context.Inference.Model, _Context.Inference);

            AnswerReport report = new AnswerReport
            {
                Dataset = dataset.Name,
                Endpoint = endpoint,
                Label = args.GetOptional("label"),
                Environment = _Context.Environment
            };
            report.Environment.Judge = judgeSettings.Description;
            report.Config["endpoint"] = endpoint;
            report.Config["repeat"] = repeat.ToString();
            report.Config["concurrency"] = concurrency.ToString();
            report.Config["maxResults"] = maxResults.HasValue ? maxResults.Value.ToString() : "server default";
            report.Config["faithfulness"] = faithfulness ? "on" : "off";
            report.Config["filters"] = useFilter ? "on" : "off";
            if (limit > 0) report.Config["limitPerCorpus"] = limit.ToString();
            foreach (string option in new string[] { "rerank", "rewrite", "scope-suffix" })
            {
                string? value = args.GetOptional(option);
                if (value != null) report.Config[option] = value;
            }

            List<ProvisionedSubject> subjects = await new SubjectProvisioner(_Context).ProvisionAsync(dataset, report.Ingest, token).ConfigureAwait(false);
            Console.WriteLine("[answer] ingest: " + report.Ingest.Succeeded + "/" + report.Ingest.Documents + " documents" + (report.Ingest.Complete ? string.Empty : "  ** INCOMPLETE **"));
            foreach (ProvisionedSubject subject in subjects) await _Context.Client.WarmAnswerAsync(subject.SubjectId, token).ConfigureAwait(false);

            PrometheusSnapshot before = await PrometheusSnapshot.CaptureAsync(_Context.Client, token).ConfigureAwait(false);
            using (JudgeClient judge = new JudgeClient(judgeSettings))
            {
                for (int run = 0; run < repeat; run++)
                {
                    ConcurrentBag<AnswerItem> items = new ConcurrentBag<AnswerItem>();
                    using (SemaphoreSlim gate = new SemaphoreSlim(concurrency))
                    {
                        List<Task> tasks = new List<Task>();
                        int total = subjects.Sum(s => Select(s.Corpus.Queries, limit).Count);
                        int done = 0;
                        foreach (ProvisionedSubject subject in subjects)
                        {
                            foreach (BenchmarkQuery query in Select(subject.Corpus.Queries, limit))
                            {
                                await gate.WaitAsync(token).ConfigureAwait(false);
                                ProvisionedSubject s = subject;
                                BenchmarkQuery q = query;
                                int runIndex = run;
                                tasks.Add(Task.Run(async () =>
                                {
                                    try
                                    {
                                        AnswerItem item = await AnswerOneAsync(s, q, endpoint, maxResults, useFilter, faithfulness, judge, runIndex, token).ConfigureAwait(false);
                                        items.Add(item);
                                        int n = Interlocked.Increment(ref done);
                                        if (n % 10 == 0 || n == total) Console.WriteLine("[answer] run " + (runIndex + 1) + ": " + n + "/" + total);
                                    }
                                    finally
                                    {
                                        gate.Release();
                                    }
                                }, token));
                            }
                        }

                        await Task.WhenAll(tasks).ConfigureAwait(false);
                    }

                    List<AnswerItem> ordered = items.OrderBy(i => i.Corpus, StringComparer.Ordinal).ThenBy(i => i.QueryId, StringComparer.Ordinal).ToList();
                    report.Items.AddRange(ordered);
                    List<AnswerItem> graded = ordered.Where(i => i.Answerable && i.Correct.HasValue).ToList();
                    double accuracy = graded.Count > 0 ? graded.Average(i => i.Correct == true ? 1.0 : 0.0) : 0.0;
                    report.AccuracyByRun.Add(Math.Round(accuracy, 4));
                    Console.WriteLine("[answer] run " + (run + 1) + " accuracy " + accuracy.ToString("F3"));
                }
            }

            PrometheusSnapshot after = await PrometheusSnapshot.CaptureAsync(_Context.Client, token).ConfigureAwait(false);
            foreach (KeyValuePair<string, StageBreakdown> stage in after.Since(before))
            {
                if (!stage.Key.StartsWith("ingestion", StringComparison.Ordinal)) report.Stages[stage.Key] = stage.Value;
            }

            Summarize(report);
            Console.WriteLine("[answer] accuracy " + Get(report.Summary, "accuracy") + " (with evidence " + Get(report.Summary, "accuracyWithEvidence") + ", without " + Get(report.Summary, "accuracyWithoutEvidence")
                + ")  evidence-in-context " + Get(report.Summary, "evidenceInContext") + "  declines " + Get(report.Summary, "abstention") + "  citation P/R "
                + Get(report.Summary, "citationPrecision") + "/" + Get(report.Summary, "citationRecall") + "  faithfulness " + Get(report.Summary, "faithfulness") + "  p50 " + report.Latency.P50 + " ms");
            return report;
        }

        #endregion

        #region Private-Methods

        private static List<BenchmarkQuery> Select(List<BenchmarkQuery> queries, int limit)
        {
            if (limit <= 0 || queries.Count <= limit) return queries;

            // Stratified by type (round-robin in id order) so a limited run keeps every question type.
            List<Queue<BenchmarkQuery>> buckets = queries.GroupBy(q => q.Type).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new Queue<BenchmarkQuery>(g.OrderBy(q => q.Id, StringComparer.Ordinal))).ToList();
            List<BenchmarkQuery> selected = new List<BenchmarkQuery>();
            while (selected.Count < limit && buckets.Any(b => b.Count > 0))
            {
                foreach (Queue<BenchmarkQuery> bucket in buckets)
                {
                    if (selected.Count >= limit) break;
                    if (bucket.Count > 0) selected.Add(bucket.Dequeue());
                }
            }

            return selected;
        }

        private async Task<AnswerItem> AnswerOneAsync(ProvisionedSubject subject, BenchmarkQuery query, string endpoint, int? maxResults, bool useFilter, bool faithfulness, JudgeClient judge, int run, CancellationToken token)
        {
            bool unanswerable = !query.Answerable || DatasetStore.IsUnanswerableMarker(query.Answer);
            AnswerItem item = new AnswerItem
            {
                Run = run,
                Corpus = subject.Corpus.Id,
                QueryId = query.Id,
                Type = query.Type,
                Question = query.Text,
                Gold = unanswerable ? DatasetStore.NotInCorpus : (query.Answer ?? string.Empty),
                Answerable = !unanswerable,
                Relevant = new List<string>(query.Relevant)
            };

            QueryFilter? filter = RetrievalRunner.FilterFor(query, useFilter);
            string sourceText = string.Empty;
            if (endpoint == "query")
            {
                TimedResponse<QueryResult> response = await _Context.Client.QueryAsync(new QueryBody { Question = query.Text, SubjectId = subject.SubjectId, MaxResults = maxResults, MetadataFilter = filter }, false, token).ConfigureAwait(false);
                item.StatusCode = response.StatusCode;
                item.LatencyMs = Math.Round(response.ElapsedMs, 1);
                if (!response.IsSuccess)
                {
                    item.Error = response.Error;
                    return item;
                }

                QueryResult result = response.Value!;
                item.Answer = result.Answer ?? string.Empty;
                item.Grounded = result.Grounded;
                item.InsufficientSupport = result.InsufficientSupport;
                StringBuilder context = new StringBuilder();
                List<string?> sourceDocs = new List<string?>();
                for (int i = 0; i < result.Sources.Count; i++)
                {
                    GraphNodeInfo node = result.Sources[i];
                    string? doc = subject.ResolveDocument(node);
                    sourceDocs.Add(doc);
                    if (doc != null && !item.SourceDocuments.Contains(doc)) item.SourceDocuments.Add(doc);
                    context.Append('[').Append(i + 1).Append("] ").AppendLine(string.IsNullOrWhiteSpace(node.Content) ? node.Name : node.Content);
                }

                sourceText = context.ToString();
                foreach (int index in CitedIndexes(item.Answer))
                {
                    if (index >= 1 && index <= sourceDocs.Count && sourceDocs[index - 1] != null && !item.CitedDocuments.Contains(sourceDocs[index - 1]!)) item.CitedDocuments.Add(sourceDocs[index - 1]!);
                }

                item.EvidenceCoverage = EvidenceMatcher.Coverage(query.Evidence, result.Sources.Select(s => s.Content));
            }
            else
            {
                ChatBody body = new ChatBody { SubjectId = subject.SubjectId, MaxResults = maxResults, MetadataFilter = filter };
                body.Messages.Add(new ChatMessageBody { Role = "user", Content = query.Text });
                TimedResponse<ChatEvent> response = await _Context.Client.ChatAsync(body, token).ConfigureAwait(false);
                item.StatusCode = response.StatusCode;
                item.LatencyMs = Math.Round(response.ElapsedMs, 1);
                if (response.Value == null)
                {
                    item.Error = response.Error ?? "no complete event";
                    return item;
                }

                item.Answer = response.Value.Answer ?? string.Empty;
                item.ToolCalls = response.Value.ToolCalls?.Count ?? 0;
                foreach (ChatCitation citation in response.Value.Citations ?? new List<ChatCitation>())
                {
                    string? doc = subject.ResolveDocument(citation.LinkId, citation.Url);
                    if (doc == null) continue;
                    if (!item.SourceDocuments.Contains(doc)) item.SourceDocuments.Add(doc);
                    if (!item.CitedDocuments.Contains(doc)) item.CitedDocuments.Add(doc);
                }
            }

            item.EvidenceInContext = item.Answerable && (item.SourceDocuments.Any(d => item.Relevant.Contains(d)) || (item.EvidenceCoverage ?? 0.0) > 0.0);

            // Pneuma's own fallback texts mean the answering model call failed (for example a rate-limited
            // endpoint); that is an error, not a wrong answer, and must not be graded.
            foreach (string fallback in _FallbackAnswers)
            {
                if (item.Answer.StartsWith(fallback, StringComparison.Ordinal))
                {
                    item.Error = "answer generation failed: " + fallback;
                    return item;
                }
            }

            try
            {
                item.Correct = await judge.GradeAsync(query.Text, item.Gold, item.Answer, unanswerable, token).ConfigureAwait(false);
                if (faithfulness && item.Answerable && endpoint == "query" && sourceText.Length > 0)
                {
                    item.Faithfulness = await judge.FaithfulnessAsync(item.Answer, sourceText, token).ConfigureAwait(false);
                }
            }
            catch (InvalidOperationException e)
            {
                // A judge that stays unavailable leaves this item ungraded (reported as an unparsed verdict)
                // rather than aborting the run.
                Console.WriteLine("[answer] judge failed for " + query.Id + ": " + e.Message);
            }

            return item;
        }

        private static List<int> CitedIndexes(string answer)
        {
            List<int> indexes = new List<int>();
            foreach (Match match in _Citation.Matches(answer ?? string.Empty))
            {
                string inner = match.Groups[1].Value;
                if (inner.Contains('-'))
                {
                    string[] range = inner.Split('-');
                    if (range.Length == 2 && int.TryParse(range[0].Trim(), out int from) && int.TryParse(range[1].Trim(), out int to) && to >= from && to - from < 20)
                    {
                        for (int i = from; i <= to; i++) indexes.Add(i);
                    }

                    continue;
                }

                foreach (string part in inner.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(part.Trim(), out int value)) indexes.Add(value);
                }
            }

            return indexes;
        }

        private static void Summarize(AnswerReport report)
        {
            List<AnswerItem> ok = report.Items.Where(i => i.Error == null).ToList();
            List<AnswerItem> answerable = ok.Where(i => i.Answerable && i.Correct.HasValue).ToList();
            List<AnswerItem> negatives = ok.Where(i => !i.Answerable && i.Correct.HasValue).ToList();
            Dictionary<string, double> s = report.Summary;
            s["items"] = report.Items.Count;
            s["errors"] = report.Items.Count(i => i.Error != null);
            s["unparsedVerdicts"] = ok.Count(i => !i.Correct.HasValue);
            if (answerable.Count > 0)
            {
                List<double> correct = answerable.Select(i => i.Correct == true ? 1.0 : 0.0).ToList();
                s["accuracy"] = Math.Round(correct.Average(), 4);
                report.AccuracyInterval = Bootstrap.MeanInterval(correct);
                s["evidenceInContext"] = Math.Round(answerable.Average(i => i.EvidenceInContext ? 1.0 : 0.0), 4);
                List<AnswerItem> with = answerable.Where(i => i.EvidenceInContext).ToList();
                List<AnswerItem> without = answerable.Where(i => !i.EvidenceInContext).ToList();
                if (with.Count > 0) s["accuracyWithEvidence"] = Math.Round(with.Average(i => i.Correct == true ? 1.0 : 0.0), 4);
                if (without.Count > 0) s["accuracyWithoutEvidence"] = Math.Round(without.Average(i => i.Correct == true ? 1.0 : 0.0), 4);
                s["answerableWithEvidence"] = with.Count;
                s["answerableWithoutEvidence"] = without.Count;
                List<AnswerItem> covered = answerable.Where(i => i.EvidenceCoverage.HasValue).ToList();
                if (covered.Count > 0) s["evidenceCoverage"] = Math.Round(covered.Average(i => i.EvidenceCoverage!.Value), 4);
                List<AnswerItem> cited = answerable.Where(i => i.CitedDocuments.Count > 0).ToList();
                s["citationRate"] = Math.Round((double)cited.Count / answerable.Count, 4);
                if (cited.Count > 0) s["citationPrecision"] = Math.Round(cited.Average(i => (double)i.CitedDocuments.Count(d => i.Relevant.Contains(d)) / i.CitedDocuments.Count), 4);
                s["citationRecall"] = Math.Round(answerable.Average(i => i.Relevant.Count == 0 ? 0.0 : (double)i.CitedDocuments.Count(d => i.Relevant.Contains(d)) / i.Relevant.Count), 4);
                List<AnswerItem> faithful = answerable.Where(i => i.Faithfulness.HasValue).ToList();
                if (faithful.Count > 0) s["faithfulness"] = Math.Round(faithful.Average(i => i.Faithfulness!.Value), 4);
            }

            if (negatives.Count > 0)
            {
                s["abstention"] = Math.Round(negatives.Average(i => i.Correct == true ? 1.0 : 0.0), 4);
                s["negatives"] = negatives.Count;
                List<AnswerItem> flagged = negatives.Where(i => i.InsufficientSupport.HasValue).ToList();
                if (flagged.Count > 0) s["insufficientSupportOnNegatives"] = Math.Round(flagged.Average(i => i.InsufficientSupport == true ? 1.0 : 0.0), 4);
            }

            if (report.AccuracyByRun.Count > 1)
            {
                double mean = report.AccuracyByRun.Average();
                s["accuracyRunStdDev"] = Math.Round(Math.Sqrt(report.AccuracyByRun.Sum(a => (a - mean) * (a - mean)) / (report.AccuracyByRun.Count - 1)), 4);
            }

            List<AnswerItem> tooled = ok.Where(i => i.ToolCalls.HasValue).ToList();
            if (tooled.Count > 0) s["meanToolCalls"] = Math.Round(tooled.Average(i => i.ToolCalls!.Value), 2);
            report.Latency = LatencyStats.From(ok.Select(i => i.LatencyMs));

            foreach (IGrouping<string, AnswerItem> group in ok.Where(i => i.Correct.HasValue).GroupBy(i => i.Type).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                List<AnswerItem> items = group.ToList();
                Dictionary<string, double> metrics = new Dictionary<string, double>
                {
                    ["count"] = items.Count,
                    ["correct"] = Math.Round(items.Average(i => i.Correct == true ? 1.0 : 0.0), 4)
                };
                List<AnswerItem> answerableGroup = items.Where(i => i.Answerable).ToList();
                if (answerableGroup.Count > 0) metrics["evidenceInContext"] = Math.Round(answerableGroup.Average(i => i.EvidenceInContext ? 1.0 : 0.0), 4);
                report.ByType[group.Key] = metrics;
            }
        }

        private static string Get(Dictionary<string, double> values, string name)
        {
            return values.TryGetValue(name, out double value) ? value.ToString("F3") : "n/a";
        }

        #endregion
    }
}
