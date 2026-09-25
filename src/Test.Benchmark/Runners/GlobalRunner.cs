namespace Test.Benchmark.Runners
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Client;
    using Test.Benchmark.Datasets;

    /// <summary>
    /// Thematic ("global") questions have no gold ranking, so they are scored the way the GraphRAG paper does:
    /// each question is answered by community summaries (<c>/v1.0/query/global</c>) and by ordinary chunk retrieval
    /// (<c>/v1.0/query</c>), and a judge compares the two answers on comprehensiveness, diversity, and directness,
    /// in both presentation orders to cancel position bias. Weaker evidence than the ranked metrics; reported apart.
    /// Communities are (re)built first. Requires a full-profile subject (the lean profile extracts no entities).
    /// </summary>
    public class GlobalRunner
    {
        #region Public-Members

        /// <summary>
        /// Criteria judged.
        /// </summary>
        public static readonly string[] Criteria = new string[] { "comprehensiveness", "diversity", "directness" };

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
        public GlobalRunner(BenchmarkContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the benchmark.
        /// </summary>
        /// <param name="dataset">Dataset (its "global" queries are used).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The report.</returns>
        public async Task<GlobalReport> RunAsync(BenchmarkDataset dataset, CancellationToken token)
        {
            BenchmarkArguments args = _Context.Arguments;
            ModelSettings judgeSettings = ModelSettings.FromArguments(args, "judge", _Context.Inference.Model, _Context.Inference);
            GlobalReport report = new GlobalReport { Dataset = dataset.Name, Label = args.GetOptional("label"), Environment = _Context.Environment };
            report.Environment.Judge = judgeSettings.Description;
            report.Config["queryType"] = args.Get("type", "global");

            IngestSummary ingest = new IngestSummary();
            List<ProvisionedSubject> subjects = await new SubjectProvisioner(_Context).ProvisionAsync(dataset, ingest, token).ConfigureAwait(false);
            using (JudgeClient judge = new JudgeClient(judgeSettings))
            {
                foreach (ProvisionedSubject subject in subjects)
                {
                    int status = await _Context.Client.BuildCommunitiesAsync(subject.SubjectId, token).ConfigureAwait(false);
                    Console.WriteLine("[global] " + subject.SubjectName + ": community build HTTP " + status);
                    foreach (BenchmarkQuery query in subject.Corpus.Queries.Where(q => string.Equals(q.Type, report.Config["queryType"], StringComparison.OrdinalIgnoreCase)))
                    {
                        TimedResponse<QueryResult> global = await _Context.Client.QueryAsync(new QueryBody { Question = query.Text, SubjectId = subject.SubjectId, MaxResults = 8 }, true, token).ConfigureAwait(false);
                        TimedResponse<QueryResult> local = await _Context.Client.QueryAsync(new QueryBody { Question = query.Text, SubjectId = subject.SubjectId, MaxResults = 8 }, false, token).ConfigureAwait(false);
                        GlobalItem item = new GlobalItem
                        {
                            QueryId = query.Id,
                            Question = query.Text,
                            GlobalAnswer = global.Value?.Answer ?? ("(error: " + global.Error + ")"),
                            LocalAnswer = local.Value?.Answer ?? ("(error: " + local.Error + ")")
                        };
                        foreach (string criterion in Criteria)
                        {
                            int forward = await judge.PreferAsync(query.Text, item.GlobalAnswer, item.LocalAnswer, criterion, token).ConfigureAwait(false);
                            int backward = await judge.PreferAsync(query.Text, item.LocalAnswer, item.GlobalAnswer, criterion, token).ConfigureAwait(false);
                            bool globalWins = forward == 1 && backward == 2;
                            bool localWins = forward == 2 && backward == 1;
                            item.Verdicts[criterion] = globalWins ? "global" : (localWins ? "local" : "tie");
                        }

                        report.Items.Add(item);
                        Console.WriteLine("[global] " + query.Id + ": " + string.Join(", ", item.Verdicts.Select(v => v.Key + "=" + v.Value)));
                    }
                }
            }

            foreach (string criterion in Criteria)
            {
                int n = Math.Max(1, report.Items.Count);
                report.WinRates[criterion] = new Dictionary<string, double>
                {
                    ["global"] = Math.Round(report.Items.Count(i => i.Verdicts[criterion] == "global") / (double)n, 4),
                    ["local"] = Math.Round(report.Items.Count(i => i.Verdicts[criterion] == "local") / (double)n, 4),
                    ["tie"] = Math.Round(report.Items.Count(i => i.Verdicts[criterion] == "tie") / (double)n, 4)
                };
            }

            return report;
        }

        #endregion
    }
}
