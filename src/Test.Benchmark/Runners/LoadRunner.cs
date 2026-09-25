namespace Test.Benchmark.Runners
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Client;
    using Test.Benchmark.Datasets;
    using Test.Benchmark.Metrics;

    /// <summary>
    /// Closed-loop load: at each concurrency level, N workers issue subject searches back to back for a fixed
    /// window (after a warm-up), cycling through the dataset's query texts. Reports throughput, latency
    /// percentiles, error rate, and the server-side stage breakdown per level. With <c>--stub</c> the subject is
    /// built and queried with the stub embedding server, so the numbers measure Pneuma and RecallDB rather than the
    /// embedding model.
    /// </summary>
    public class LoadRunner
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
        public LoadRunner(BenchmarkContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the benchmark.
        /// </summary>
        /// <param name="dataset">Dataset whose corpus is searched (the first corpus).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The report.</returns>
        public async Task<LoadReport> RunAsync(BenchmarkDataset dataset, CancellationToken token)
        {
            BenchmarkArguments args = _Context.Arguments;
            List<int> levels = args.GetList("concurrency", "1,4,16,64").Select(l => Math.Max(1, int.Parse(l, System.Globalization.CultureInfo.InvariantCulture))).ToList();
            int duration = Math.Max(5, args.GetInt("duration", 30));
            int warmup = Math.Max(0, args.GetInt("warmup", 5));
            string mode = args.Get("mode", "hybrid");
            int maxResults = args.GetInt("k", 10);

            BenchmarkDataset single = new BenchmarkDataset { Name = dataset.Name, Description = dataset.Description, Corpora = new List<BenchmarkCorpus> { dataset.Corpora[0] } };
            LoadReport report = new LoadReport { Dataset = dataset.Name, Label = args.GetOptional("label"), Environment = _Context.Environment };
            report.Config["levels"] = string.Join(",", levels);
            report.Config["durationSeconds"] = duration.ToString();
            report.Config["warmupSeconds"] = warmup.ToString();
            report.Config["mode"] = mode;
            report.Config["embedding"] = _Context.Embedding.Description;

            List<ProvisionedSubject> subjects = await new SubjectProvisioner(_Context).ProvisionAsync(single, report.Ingest, token).ConfigureAwait(false);
            ProvisionedSubject subject = subjects[0];
            List<string> queries = subject.Corpus.Queries.Select(q => q.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (queries.Count == 0) queries = subject.Corpus.Documents.Select(d => d.Title ?? d.Id).ToList();
            await _Context.Client.WarmSearchAsync(subject.SubjectId, token).ConfigureAwait(false);

            foreach (int level in levels)
            {
                await RunWindowAsync(subject, queries, mode, maxResults, level, warmup, null, token).ConfigureAwait(false);
                PrometheusSnapshot before = await PrometheusSnapshot.CaptureAsync(_Context.Client, token).ConfigureAwait(false);
                LoadLevel result = await RunWindowAsync(subject, queries, mode, maxResults, level, duration, before, token).ConfigureAwait(false);
                report.Levels.Add(result);
                Console.WriteLine("[load] c=" + level.ToString().PadLeft(3) + "  " + result.Throughput.ToString("F1") + " ops/s  p50 " + result.Latency.P50 + " ms  p95 " + result.Latency.P95
                    + " ms  p99 " + result.Latency.P99 + " ms  errors " + result.ErrorRate.ToString("P2"));
            }

            return report;
        }

        #endregion

        #region Private-Methods

        private async Task<LoadLevel> RunWindowAsync(ProvisionedSubject subject, List<string> queries, string mode, int maxResults, int concurrency, int seconds, PrometheusSnapshot? before, CancellationToken token)
        {
            ConcurrentBag<double> latencies = new ConcurrentBag<double>();
            long operations = 0;
            long errors = 0;
            int cursor = 0;
            Stopwatch window = Stopwatch.StartNew();
            TimeSpan length = TimeSpan.FromSeconds(seconds);
            List<Task> workers = new List<Task>();
            for (int w = 0; w < concurrency; w++)
            {
                workers.Add(Task.Run(async () =>
                {
                    while (window.Elapsed < length && !token.IsCancellationRequested)
                    {
                        string query = queries[(int)((uint)Interlocked.Increment(ref cursor) % (uint)queries.Count)];
                        TimedResponse<EnumerationPage<SubjectSearchHit>> response = await _Context.Client.SubjectSearchAsync(subject.SubjectId, query, new SearchOptions { Mode = mode, MaxResults = maxResults }, token).ConfigureAwait(false);
                        Interlocked.Increment(ref operations);
                        if (!response.IsSuccess) Interlocked.Increment(ref errors);
                        latencies.Add(response.ElapsedMs);
                    }
                }, token));
            }

            await Task.WhenAll(workers).ConfigureAwait(false);
            double elapsed = window.Elapsed.TotalSeconds;
            LoadLevel level = new LoadLevel
            {
                Concurrency = concurrency,
                Operations = operations,
                Errors = errors,
                Throughput = Math.Round(operations / Math.Max(0.001, elapsed), 2),
                ErrorRate = operations > 0 ? Math.Round((double)errors / operations, 4) : 0.0,
                Latency = LatencyStats.From(latencies)
            };
            if (before != null)
            {
                PrometheusSnapshot after = await PrometheusSnapshot.CaptureAsync(_Context.Client, token).ConfigureAwait(false);
                foreach (KeyValuePair<string, StageBreakdown> stage in after.Since(before))
                {
                    if (!stage.Key.StartsWith("ingestion", StringComparison.Ordinal)) level.Stages[stage.Key] = stage.Value;
                }
            }

            return level;
        }

        #endregion
    }
}
