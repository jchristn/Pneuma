namespace Test.Benchmark.Runners
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Client;
    using Test.Benchmark.Datasets;
    using Test.Benchmark.Metrics;
    using Test.Benchmark.Reference;

    /// <summary>
    /// Retrieval accuracy benchmark: provision a labelled dataset, run every query in each Pneuma search mode and
    /// each reference-arm mode, and score the document rankings (Hit@1, Recall@1/5/10, All@5/10, MRR@10, nDCG@10,
    /// Hits@4, MAP@10, and passage-level Evidence@10) with bootstrap intervals, broken down by query type, alongside
    /// latency, a server-side stage breakdown, and how well scores separate answerable from unanswerable questions.
    /// </summary>
    public class RetrievalRunner
    {
        #region Public-Members

        /// <summary>
        /// Metric names reported, in display order.
        /// </summary>
        public static readonly string[] MetricNames = new string[] { "hit@1", "recall@1", "recall@5", "recall@10", "all@5", "all@10", "mrr@10", "ndcg@10", "hits@4", "map@10", "evidence@10" };

        #endregion

        #region Private-Members

        private readonly BenchmarkContext _Context;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Benchmark context.</param>
        /// <exception cref="ArgumentNullException">Thrown when the context is null.</exception>
        public RetrievalRunner(BenchmarkContext context)
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
        public async Task<RetrievalReport> RunAsync(BenchmarkDataset dataset, CancellationToken token)
        {
            BenchmarkArguments args = _Context.Arguments;
            List<string> modes = args.GetList("modes", "text,vector,hybrid");
            bool reference = !args.GetFlag("no-reference");
            int topK = Math.Max(10, args.GetInt("k", 10));
            int concurrency = Math.Max(1, args.GetInt("concurrency", 1));
            bool useFilter = !args.GetFlag("no-filter");
            bool evidence = !args.GetFlag("no-evidence");
            bool referenceOnly = args.GetFlag("reference-only");
            if (referenceOnly)
            {
                reference = true;
                modes = new List<string>();
            }

            RetrievalReport report = new RetrievalReport
            {
                Dataset = dataset.Name,
                Description = dataset.Description,
                Label = args.GetOptional("label"),
                Environment = _Context.Environment
            };
            report.Config["modes"] = string.Join(",", modes) + (reference ? "," + string.Join(",", ReferenceArm.Modes) : string.Empty);
            report.Config["topK"] = topK.ToString();
            report.Config["concurrency"] = concurrency.ToString();
            report.Config["filters"] = useFilter ? "on" : "off";
            report.Config["ingestProfile"] = _Context.IngestProfile;
            foreach (KeyValuePair<string, string> option in args.Options)
            {
                if (option.Key.StartsWith("chunk-", StringComparison.Ordinal) || option.Key.StartsWith("override-", StringComparison.Ordinal)
                    || option.Key == "index-summaries" || option.Key == "rerank" || option.Key == "rewrite" || option.Key == "scope-suffix")
                {
                    report.Config[option.Key] = option.Value;
                }
            }

            Console.WriteLine("[retrieval] " + dataset.Name + ": " + dataset.Corpora.Count + " corpora, " + dataset.Corpora.Sum(c => c.Documents.Count)
                + " documents, " + dataset.Corpora.Sum(c => c.Queries.Count) + " queries");

            List<ProvisionedSubject> subjects = new List<ProvisionedSubject>();
            if (!referenceOnly)
            {
                SubjectProvisioner provisioner = new SubjectProvisioner(_Context);
                subjects = await provisioner.ProvisionAsync(dataset, report.Ingest, token).ConfigureAwait(false);
                foreach (ProvisionedSubject subject in subjects) report.Subjects[subject.SubjectName] = subject.SubjectId;
                Console.WriteLine("[retrieval] ingest: " + report.Ingest.Succeeded + "/" + report.Ingest.Documents + " documents, " + report.Ingest.ReusedSubjects
                    + " subjects reused, " + report.Ingest.Failures + " failures" + (report.Ingest.Complete ? string.Empty : "  ** INCOMPLETE: results are not comparable **"));

                foreach (ProvisionedSubject subject in subjects)
                {
                    await _Context.Client.WarmSearchAsync(subject.SubjectId, token).ConfigureAwait(false);
                }
            }
            else
            {
                report.Ingest.Documents = dataset.Corpora.Sum(c => c.Documents.Count);
                report.Ingest.Succeeded = report.Ingest.Documents;
            }

            foreach (string mode in modes)
            {
                PrometheusSnapshot before = await PrometheusSnapshot.CaptureAsync(_Context.Client, token).ConfigureAwait(false);
                List<QueryOutcome> outcomes = await RunServerModeAsync(subjects, mode, topK, concurrency, useFilter, evidence, token).ConfigureAwait(false);
                PrometheusSnapshot after = await PrometheusSnapshot.CaptureAsync(_Context.Client, token).ConfigureAwait(false);
                ModeSummary summary = Summarize(mode, outcomes);
                summary.Stages = after.Since(before);
                report.Modes.Add(summary);
                report.Outcomes.AddRange(outcomes);
                Print(summary);
            }

            if (reference)
            {
                using (DirectModelClient embedder = _Context.EmbeddingClient())
                {
                    EmbeddingCache cache = new EmbeddingCache(_Context.CacheDirectory, _Context.Embedding.Model);
                    Dictionary<string, ReferenceArm> arms = new Dictionary<string, ReferenceArm>(StringComparer.Ordinal);
                    bool bm25Only = args.GetFlag("no-dense");
                    foreach (BenchmarkCorpus corpus in dataset.Corpora)
                    {
                        arms[corpus.Id] = await ReferenceArm.BuildAsync(corpus, bm25Only ? null : embedder, bm25Only ? null : cache,
                            args.GetDouble("bm25-k1", 0.9), args.GetDouble("bm25-b", 0.4), args.GetInt("ref-chunk-words", 190), token).ConfigureAwait(false);
                    }

                    report.Config["reference"] = "bm25(k1=" + args.GetDouble("bm25-k1", 0.9) + ",b=" + args.GetDouble("bm25-b", 0.4) + "), dense(" + _Context.Embedding.Model
                        + ", " + args.GetInt("ref-chunk-words", 190) + "-word chunks, max-pooled), hybrid(RRF k=60); embedding cache " + cache.Hits + " hits / " + cache.Misses + " computed";
                    foreach (string mode in ReferenceArm.Modes)
                    {
                        List<QueryOutcome> outcomes = await RunReferenceModeAsync(dataset.Corpora, arms, mode, topK, token).ConfigureAwait(false);
                        ModeSummary summary = Summarize(mode, outcomes);
                        report.Modes.Add(summary);
                        report.Outcomes.AddRange(outcomes);
                        Print(summary);
                    }
                }
            }

            return report;
        }

        /// <summary>
        /// Score a ranking against a query's labels.
        /// </summary>
        /// <param name="outcome">Outcome holding the ranking; metrics are written into it.</param>
        /// <param name="query">The query.</param>
        /// <param name="evidenceCoverage">Evidence coverage among the top passages, when measured.</param>
        public static void Score(QueryOutcome outcome, BenchmarkQuery query, double? evidenceCoverage)
        {
            if (!query.Answerable || outcome.StatusCode < 200 || outcome.StatusCode >= 300) return;
            HashSet<string> relevant = new HashSet<string>(query.Relevant, StringComparer.Ordinal);
            Dictionary<string, int> grades = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string id in query.Relevant) grades[id] = 1;
            if (query.Grades != null)
            {
                foreach (KeyValuePair<string, int> grade in query.Grades) grades[grade.Key] = grade.Value;
            }

            List<string> ranked = outcome.Ranked;
            outcome.Metrics["hit@1"] = ranked.Count > 0 && relevant.Contains(ranked[0]) ? 1.0 : 0.0;
            outcome.Metrics["recall@1"] = RetrievalMetrics.RecallAtK(ranked, relevant, 1);
            outcome.Metrics["recall@5"] = RetrievalMetrics.RecallAtK(ranked, relevant, 5);
            outcome.Metrics["recall@10"] = RetrievalMetrics.RecallAtK(ranked, relevant, 10);
            outcome.Metrics["all@5"] = RetrievalMetrics.AllAtK(ranked, relevant, 5);
            outcome.Metrics["all@10"] = RetrievalMetrics.AllAtK(ranked, relevant, 10);
            outcome.Metrics["mrr@10"] = RetrievalMetrics.ReciprocalRank(ranked, relevant, 10);
            outcome.Metrics["ndcg@10"] = RetrievalMetrics.NdcgAtK(ranked, grades, 10);
            outcome.Metrics["hits@4"] = RetrievalMetrics.HitAtK(ranked, relevant, 4);
            outcome.Metrics["map@10"] = RetrievalMetrics.AveragePrecisionAtK(ranked, relevant, 10);
            if (evidenceCoverage.HasValue) outcome.Metrics["evidence@10"] = Math.Round(evidenceCoverage.Value, 4);
        }

        /// <summary>
        /// Summarize outcomes of one mode.
        /// </summary>
        /// <param name="mode">Mode.</param>
        /// <param name="outcomes">Outcomes.</param>
        /// <returns>The summary.</returns>
        public static ModeSummary Summarize(string mode, List<QueryOutcome> outcomes)
        {
            List<QueryOutcome> scored = outcomes.Where(o => o.Metrics.Count > 0).ToList();
            List<QueryOutcome> negatives = outcomes.Where(o => o.Relevant.Count == 0 && o.StatusCode >= 200 && o.StatusCode < 300).ToList();
            ModeSummary summary = new ModeSummary
            {
                Mode = mode,
                Queries = scored.Count,
                NegativeQueries = negatives.Count,
                Errors = outcomes.Count(o => o.StatusCode < 200 || o.StatusCode >= 300),
                Latency = LatencyStats.From(outcomes.Where(o => o.LatencyMs > 0).Select(o => o.LatencyMs))
            };

            foreach (string name in MetricNames)
            {
                List<double> values = scored.Where(o => o.Metrics.ContainsKey(name)).Select(o => o.Metrics[name]).ToList();
                if (values.Count == 0) continue;
                summary.Metrics[name] = Math.Round(values.Average(), 4);
                ConfidenceInterval? interval = Bootstrap.MeanInterval(values);
                if (interval != null) summary.Intervals[name] = interval;
            }

            foreach (IGrouping<string, QueryOutcome> group in scored.GroupBy(o => o.Type).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                Dictionary<string, double> metrics = new Dictionary<string, double>();
                foreach (string name in MetricNames)
                {
                    List<double> values = group.Where(o => o.Metrics.ContainsKey(name)).Select(o => o.Metrics[name]).ToList();
                    if (values.Count > 0) metrics[name] = Math.Round(values.Average(), 4);
                }

                metrics["count"] = group.Count();
                summary.ByType[group.Key] = metrics;
            }

            if (scored.Any(o => o.TopScore.HasValue) && negatives.Any(o => o.TopScore.HasValue))
            {
                summary.MeanTopScoreAnswerable = Math.Round(scored.Where(o => o.TopScore.HasValue).Average(o => o.TopScore!.Value), 4);
                summary.MeanTopScoreNegative = Math.Round(negatives.Where(o => o.TopScore.HasValue).Average(o => o.TopScore!.Value), 4);
                summary.ScoreAuroc = RetrievalMetrics.Auroc(scored.Select(o => o.TopScore ?? 0.0).ToList(), negatives.Select(o => o.TopScore ?? 0.0).ToList());
            }

            if (scored.Any(o => o.TopVectorScore.HasValue) && negatives.Any(o => o.TopVectorScore.HasValue))
                summary.VectorScoreAuroc = RetrievalMetrics.Auroc(scored.Select(o => o.TopVectorScore ?? 0.0).ToList(), negatives.Select(o => o.TopVectorScore ?? 0.0).ToList());
            if (scored.Any(o => o.TopFusedScore.HasValue) && negatives.Any(o => o.TopFusedScore.HasValue))
                summary.FusedScoreAuroc = RetrievalMetrics.Auroc(scored.Select(o => o.TopFusedScore ?? 0.0).ToList(), negatives.Select(o => o.TopFusedScore ?? 0.0).ToList());

            int chunkHits = outcomes.Sum(o => o.ChunkHits ?? 0);
            if (chunkHits > 0) summary.SummaryShare = Math.Round((double)outcomes.Sum(o => o.SummaryHits ?? 0) / chunkHits, 4);
            return summary;
        }

        /// <summary>
        /// The filter a query is searched with (its explicit filter, else its Isis category as a required label).
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="useFilter">False disables filters.</param>
        /// <returns>The filter or null.</returns>
        public static QueryFilter? FilterFor(BenchmarkQuery query, bool useFilter)
        {
            if (!useFilter) return null;
            if (query.Filter != null) return query.Filter;
            if (!string.IsNullOrWhiteSpace(query.Category)) return new QueryFilter { RequiredLabels = new List<string> { query.Category! } };
            return null;
        }

        #endregion

        #region Private-Methods

        private async Task<List<QueryOutcome>> RunServerModeAsync(List<ProvisionedSubject> subjects, string mode, int topK, int concurrency, bool useFilter, bool evidence, CancellationToken token)
        {
            ConcurrentBag<QueryOutcome> outcomes = new ConcurrentBag<QueryOutcome>();
            Dictionary<string, string> overrides = Overrides();
            using (SemaphoreSlim gate = new SemaphoreSlim(concurrency))
            {
                List<Task> tasks = new List<Task>();
                foreach (ProvisionedSubject subject in subjects)
                {
                    foreach (BenchmarkQuery query in subject.Corpus.Queries)
                    {
                        await gate.WaitAsync(token).ConfigureAwait(false);
                        ProvisionedSubject s = subject;
                        BenchmarkQuery q = query;
                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                outcomes.Add(await RunServerQueryAsync(s, q, mode, topK, useFilter, evidence, overrides, token).ConfigureAwait(false));
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

            return outcomes.OrderBy(o => o.Corpus, StringComparer.Ordinal).ThenBy(o => o.QueryId, StringComparer.Ordinal).ToList();
        }

        private async Task<QueryOutcome> RunServerQueryAsync(ProvisionedSubject subject, BenchmarkQuery query, string mode, int topK, bool useFilter, bool evidence, Dictionary<string, string> overrides, CancellationToken token)
        {
            SearchOptions options = new SearchOptions { Mode = mode, MaxResults = topK, Filter = FilterFor(query, useFilter) };
            foreach (KeyValuePair<string, string> entry in overrides) options.Extra[entry.Key] = entry.Value;
            TimedResponse<EnumerationPage<SubjectSearchHit>> response = await _Context.Client.SubjectSearchAsync(subject.SubjectId, query.Text, options, token).ConfigureAwait(false);

            QueryOutcome outcome = new QueryOutcome
            {
                Corpus = subject.Corpus.Id,
                QueryId = query.Id,
                Type = query.Type,
                Mode = mode,
                StatusCode = response.StatusCode,
                LatencyMs = Math.Round(response.ElapsedMs, 2),
                Relevant = new List<string>(query.Relevant),
                Error = response.IsSuccess ? null : response.Error
            };
            if (!response.IsSuccess) return outcome;

            List<SubjectSearchHit> hits = response.Value!.Objects;
            foreach (SubjectSearchHit hit in hits)
            {
                string? docId = subject.ResolveDocument(hit.LinkId, hit.LinkUrl);
                if (docId != null && !outcome.Ranked.Contains(docId)) outcome.Ranked.Add(docId);
            }

            if (hits.Count > 0)
            {
                outcome.TopScore = hits[0].Score;
                if (hits.Any(h => h.VectorScore.HasValue)) outcome.TopVectorScore = hits.Max(h => h.VectorScore ?? 0.0);
                if (hits[0].FusedScore.HasValue) outcome.TopFusedScore = hits[0].FusedScore;
            }

            double? coverage = null;
            if (evidence && query.Evidence != null && query.Evidence.Count > 0)
            {
                // Passage-level evidence: ask for chunk-granularity hits (builds without it return document-level
                // hits, so this falls back to each document's best snippet, a lower bound).
                SearchOptions chunkOptions = new SearchOptions { Mode = mode, MaxResults = topK, Filter = options.Filter };
                foreach (KeyValuePair<string, string> entry in options.Extra) chunkOptions.Extra[entry.Key] = entry.Value;
                chunkOptions.Extra["granularity"] = "chunk";
                TimedResponse<EnumerationPage<SubjectSearchHit>> chunks = await _Context.Client.SubjectSearchAsync(subject.SubjectId, query.Text, chunkOptions, token).ConfigureAwait(false);
                if (chunks.IsSuccess)
                {
                    List<SubjectSearchHit> top = chunks.Value!.Objects.Take(topK).ToList();
                    coverage = EvidenceMatcher.Coverage(query.Evidence, top.Select(h => h.Snippet));
                    if (top.Any(h => h.ChunkKind != null))
                    {
                        outcome.ChunkHits = top.Count;
                        outcome.SummaryHits = top.Count(h => string.Equals(h.ChunkKind, "summary", StringComparison.OrdinalIgnoreCase));
                    }
                }
            }

            Score(outcome, query, coverage);
            return outcome;
        }

        private async Task<List<QueryOutcome>> RunReferenceModeAsync(List<BenchmarkCorpus> corpora, Dictionary<string, ReferenceArm> arms, string mode, int topK, CancellationToken token)
        {
            List<QueryOutcome> outcomes = new List<QueryOutcome>();
            foreach (BenchmarkCorpus corpus in corpora)
            {
                ReferenceArm arm = arms[corpus.Id];
                if (mode != "ref-bm25" && !arm.HasDense) continue;
                foreach (BenchmarkQuery query in corpus.Queries)
                {
                    DateTime started = DateTime.UtcNow;
                    List<string> ranked = await arm.SearchAsync(query.Text, mode, topK, token).ConfigureAwait(false);
                    QueryOutcome outcome = new QueryOutcome
                    {
                        Corpus = corpus.Id,
                        QueryId = query.Id,
                        Type = query.Type,
                        Mode = mode,
                        StatusCode = 200,
                        LatencyMs = Math.Round((DateTime.UtcNow - started).TotalMilliseconds, 2),
                        Ranked = ranked,
                        Relevant = new List<string>(query.Relevant)
                    };
                    Score(outcome, query, null);
                    outcomes.Add(outcome);
                }
            }

            return outcomes;
        }

        private Dictionary<string, string> Overrides()
        {
            // --override-<name> <value> passes a per-request retrieval override through as a query parameter
            // (for example --override-rrfK 20); builds without overrides ignore them.
            Dictionary<string, string> overrides = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> option in _Context.Arguments.Options)
            {
                if (option.Key.StartsWith("override-", StringComparison.Ordinal)) overrides[option.Key.Substring(9)] = option.Value;
            }

            return overrides;
        }

        private static void Print(ModeSummary summary)
        {
            Console.WriteLine("[retrieval] " + summary.Mode.PadRight(10) + " recall@5 " + Format(summary.Metrics, "recall@5") + "  mrr@10 " + Format(summary.Metrics, "mrr@10")
                + "  ndcg@10 " + Format(summary.Metrics, "ndcg@10") + "  evidence@10 " + Format(summary.Metrics, "evidence@10")
                + "  p50 " + summary.Latency.P50 + "ms  errors " + summary.Errors);
        }

        private static string Format(Dictionary<string, double> metrics, string name)
        {
            return metrics.TryGetValue(name, out double value) ? value.ToString("F3") : "n/a";
        }

        #endregion
    }
}
