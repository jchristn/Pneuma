namespace Test.Benchmark
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Datasets;
    using Test.Benchmark.Reporting;
    using Test.Benchmark.Runners;
    using Test.Benchmark.Servers;

    /// <summary>
    /// Pneuma benchmark harness. See benchmarks/README.md for the full workflow.
    /// </summary>
    public static class Program
    {
        #region Public-Methods

        /// <summary>
        /// Entry point.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            BenchmarkArguments arguments = BenchmarkArguments.Parse(args);
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                try
                {
                    switch (arguments.Command)
                    {
                        case "prepare":
                            return Prepare(arguments);
                        case "retrieval":
                            return await RetrievalAsync(arguments, cts.Token).ConfigureAwait(false);
                        case "answer":
                            return await AnswerAsync(arguments, cts.Token).ConfigureAwait(false);
                        case "ingest":
                            return await IngestAsync(arguments, cts.Token).ConfigureAwait(false);
                        case "load":
                            return await LoadAsync(arguments, cts.Token).ConfigureAwait(false);
                        case "global":
                            return await GlobalAsync(arguments, cts.Token).ConfigureAwait(false);
                        case "agent":
                            return await AgentAsync(arguments, cts.Token).ConfigureAwait(false);
                        case "compare":
                            return ResultComparer.Compare(arguments);
                        case "stub":
                            return await StubAsync(arguments, cts.Token).ConfigureAwait(false);
                        default:
                            PrintUsage();
                            return string.IsNullOrEmpty(arguments.Command) || arguments.Command == "help" ? 0 : 2;
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.Error.WriteLine("Cancelled.");
                    return 130;
                }
                catch (Exception e) when (e is InvalidOperationException || e is InvalidDataException || e is FileNotFoundException || e is ArgumentException)
                {
                    Console.Error.WriteLine("error: " + e.Message);
                    return 1;
                }
            }
        }

        #endregion

        #region Private-Methods

        private static int Prepare(BenchmarkArguments args)
        {
            string format = args.Get("format", string.Empty).ToLowerInvariant();
            string input = args.Get("input", string.Empty);
            string output = args.Get("output", string.Empty);
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output)) throw new ArgumentException("prepare needs --input and --output.");
            string name = args.Get("name", Path.GetFileNameWithoutExtension(output));
            int limit = args.GetInt("limit", 0);
            int seed = args.GetInt("seed", 7);

            BenchmarkDataset dataset;
            if (format == "beir") dataset = BeirConverter.Convert(input, name, args.Get("split", "test"), limit, seed);
            else if (format == "multihoprag") dataset = MultiHopRagConverter.Convert(input, name, limit, seed);
            else throw new ArgumentException("--format must be beir or multihoprag.");

            DatasetStore.Save(dataset, output);
            Console.WriteLine("[prepare] " + output + ": " + dataset.Description);
            return 0;
        }

        private static async Task<int> RetrievalAsync(BenchmarkArguments args, CancellationToken token)
        {
            BenchmarkDataset dataset = DatasetStore.Load(args.Get("dataset", string.Empty));
            using (BenchmarkContext context = await BenchmarkContext.CreateAsync(args, token).ConfigureAwait(false))
            {
                RetrievalReport report = await new RetrievalRunner(context).RunAsync(dataset, token).ConfigureAwait(false);
                ReportWriter.Write(context.ResultsDirectory, "retrieval", dataset.Name, report.Label, report, ReportWriter.Retrieval(report));
                return report.Ingest.Complete ? 0 : 3;
            }
        }

        private static async Task<int> AnswerAsync(BenchmarkArguments args, CancellationToken token)
        {
            BenchmarkDataset dataset = DatasetStore.Load(args.Get("dataset", string.Empty));
            using (BenchmarkContext context = await BenchmarkContext.CreateAsync(args, token).ConfigureAwait(false))
            {
                AnswerReport report = await new AnswerRunner(context).RunAsync(dataset, token).ConfigureAwait(false);
                ReportWriter.Write(context.ResultsDirectory, "answer-" + report.Endpoint, dataset.Name, report.Label, report, ReportWriter.Answer(report));
                return report.Ingest.Complete ? 0 : 3;
            }
        }

        private static async Task<int> IngestAsync(BenchmarkArguments args, CancellationToken token)
        {
            BenchmarkDataset dataset = DatasetStore.Load(args.Get("dataset", string.Empty));
            using (BenchmarkContext context = await BenchmarkContext.CreateAsync(args, token).ConfigureAwait(false))
            {
                IngestReport report = await new IngestRunner(context).RunAsync(dataset, token).ConfigureAwait(false);
                ReportWriter.Write(context.ResultsDirectory, "ingest", dataset.Name, report.Label, report, ReportWriter.Ingest(report));
                return report.Ingest.Complete ? 0 : 3;
            }
        }

        private static async Task<int> LoadAsync(BenchmarkArguments args, CancellationToken token)
        {
            BenchmarkDataset dataset = DatasetStore.Load(args.Get("dataset", string.Empty));
            using (BenchmarkContext context = await BenchmarkContext.CreateAsync(args, token).ConfigureAwait(false))
            {
                LoadReport report = await new LoadRunner(context).RunAsync(dataset, token).ConfigureAwait(false);
                ReportWriter.Write(context.ResultsDirectory, "load", dataset.Name + (args.GetFlag("stub") ? "-stub" : string.Empty), report.Label, report, ReportWriter.Load(report));
                return 0;
            }
        }

        private static async Task<int> GlobalAsync(BenchmarkArguments args, CancellationToken token)
        {
            BenchmarkDataset dataset = DatasetStore.Load(args.Get("dataset", string.Empty));
            using (BenchmarkContext context = await BenchmarkContext.CreateAsync(args, token).ConfigureAwait(false))
            {
                GlobalReport report = await new GlobalRunner(context).RunAsync(dataset, token).ConfigureAwait(false);
                ReportWriter.Write(context.ResultsDirectory, "global", dataset.Name, report.Label, report, ReportWriter.Global(report));
                return 0;
            }
        }

        private static async Task<int> AgentAsync(BenchmarkArguments args, CancellationToken token)
        {
            string tasks = args.Get("tasks", string.Empty);
            if (!File.Exists(tasks)) throw new FileNotFoundException("agent needs --tasks <file>.", tasks);
            using (BenchmarkContext context = await BenchmarkContext.CreateAsync(args, token).ConfigureAwait(false))
            {
                Test.Benchmark.Agent.AgentReport report = await new Test.Benchmark.Agent.AgentRunner(context).RunAsync(tasks, token).ConfigureAwait(false);
                ReportWriter.Write(context.ResultsDirectory, "agent", report.Suite, args.GetOptional("label"), report, ReportWriter.Agent(report));
                return 0;
            }
        }

        private static async Task<int> StubAsync(BenchmarkArguments args, CancellationToken token)
        {
            using (StubModelServer stub = new StubModelServer(args.GetInt("port", 28434), args.GetInt("dimensionality", 768), args.GetInt("latency-ms", 0)))
            {
                Console.WriteLine("[stub] model server listening on " + stub.BaseUrl + " (Ctrl+C to stop)");
                try
                {
                    await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }

                Console.WriteLine("[stub] served " + stub.EmbeddingRequests + " embedding and " + stub.ChatRequests + " chat requests");
            }

            return 0;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Pneuma benchmark harness (see benchmarks/README.md)");
            Console.WriteLine();
            Console.WriteLine("  retrieval --dataset <file> [--modes text,vector,hybrid] [--no-reference] [--profile full|lean] [--label x]");
            Console.WriteLine("            [--embedding-model nomic-embed-text] [--inference-model gemma3:4b] [--chunk-max-tokens N] [--chunk-overlap N]");
            Console.WriteLine("            [--chunk-strategy S] [--index-summaries true|false] [--rerank none|llm|cross-encoder] [--rewrite true|false]");
            Console.WriteLine("            [--override-<setting> <value>] [--scope-suffix s] [--reingest] [--no-filter] [--concurrency N]");
            Console.WriteLine("  answer    --dataset <file> [--endpoint query|chat] [--judge-model m] [--faithfulness] [--repeat N] [--limit N]");
            Console.WriteLine("  global    --dataset <file> [--judge-model m]");
            Console.WriteLine("  ingest    --dataset <file>                      ingest fidelity: chunk statistics, extraction coverage, graph quality");
            Console.WriteLine("  agent     --tasks <file> [--model haiku] [--arms pneuma,none]");
            Console.WriteLine("  load      [--stub] [--corpus-size N] [--concurrency 1,4,16] [--duration 30] [--dataset <file>]");
            Console.WriteLine("  compare   --baseline <report.json> --candidate <report.json> [--tolerance 0.01] [--latency-tolerance 0.25]");
            Console.WriteLine("  prepare   --format beir|multihoprag --input <dir> --output <file> [--name n] [--limit N] [--seed 7]");
            Console.WriteLine("  stub      [--port 28434] [--dimensionality 768] [--latency-ms 0]");
            Console.WriteLine();
            Console.WriteLine("Common: --url http://127.0.0.1:28080 --email admin@pneuma --password password --results benchmarks/results");
        }

        #endregion
    }
}
