namespace Test.Benchmark.Reporting
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using Test.Benchmark.Metrics;
    using Test.Benchmark.Runners;

    /// <summary>
    /// Compares two retrieval reports query by query and exits non-zero on a regression, so a benchmark can gate
    /// changes. A metric regresses only when the paired bootstrap 95% interval of the per-query difference lies
    /// entirely below zero AND the mean drop exceeds the tolerance, so noise on a few hundred queries does not fail
    /// a build. p95 latency regresses past a relative tolerance.
    /// </summary>
    public static class ResultComparer
    {
        #region Public-Methods

        /// <summary>
        /// Run the compare command.
        /// </summary>
        /// <param name="arguments">Arguments (--baseline, --candidate, --tolerance, --latency-tolerance, --metrics).</param>
        /// <returns>0 when there is no regression, 1 when there is.</returns>
        /// <exception cref="FileNotFoundException">Thrown when a report is missing.</exception>
        public static int Compare(BenchmarkArguments arguments)
        {
            string baselinePath = arguments.Get("baseline", string.Empty);
            string candidatePath = arguments.Get("candidate", string.Empty);
            if (!File.Exists(baselinePath) || !File.Exists(candidatePath)) throw new FileNotFoundException("compare needs existing --baseline and --candidate report files.");

            double tolerance = arguments.GetDouble("tolerance", 0.01);
            double latencyTolerance = arguments.GetDouble("latency-tolerance", 0.0);
            List<string> metrics = arguments.GetList("metrics", "hit@1,recall@10,mrr@10,ndcg@10,evidence@10");
            RetrievalReport baseline = Load(baselinePath);
            RetrievalReport candidate = Load(candidatePath);

            Console.WriteLine("baseline : " + baselinePath + " (" + baseline.Environment.GitCommit + ")");
            Console.WriteLine("candidate: " + candidatePath + " (" + candidate.Environment.GitCommit + ")");
            Console.WriteLine();
            Console.WriteLine(string.Format("{0,-11} {1,-12} {2,9} {3,9} {4,9} {5,20} {6,6}", "mode", "metric", "baseline", "candidate", "delta", "95% CI of delta", "n"));

            bool regressed = false;
            foreach (ModeSummary candidateMode in candidate.Modes)
            {
                ModeSummary? baselineMode = baseline.Modes.Find(m => string.Equals(m.Mode, candidateMode.Mode, StringComparison.OrdinalIgnoreCase));
                if (baselineMode == null) continue;

                Dictionary<string, QueryOutcome> before = baseline.Outcomes.Where(o => o.Mode == candidateMode.Mode && o.Metrics.Count > 0)
                    .GroupBy(o => o.Corpus + "/" + o.QueryId).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                List<QueryOutcome> after = candidate.Outcomes.Where(o => o.Mode == candidateMode.Mode && o.Metrics.Count > 0 && before.ContainsKey(o.Corpus + "/" + o.QueryId)).ToList();

                foreach (string metric in metrics)
                {
                    List<double> b = new List<double>();
                    List<double> c = new List<double>();
                    foreach (QueryOutcome outcome in after)
                    {
                        QueryOutcome match = before[outcome.Corpus + "/" + outcome.QueryId];
                        if (!outcome.Metrics.TryGetValue(metric, out double cv) || !match.Metrics.TryGetValue(metric, out double bv)) continue;
                        b.Add(bv);
                        c.Add(cv);
                    }

                    if (b.Count == 0) continue;
                    double delta = c.Average() - b.Average();
                    ConfidenceInterval? interval = Bootstrap.PairedDifference(b, c);
                    bool significant = interval != null && interval.ExcludesZero();
                    bool bad = significant && interval!.High < 0 && delta < -tolerance;
                    regressed |= bad;
                    string ci = interval == null ? "-" : "[" + interval.Low.ToString("+0.000;-0.000;0.000") + ", " + interval.High.ToString("+0.000;-0.000;0.000") + "]";
                    string flag = bad ? "  REGRESSION" : (significant && delta > tolerance ? "  improved" : string.Empty);
                    Console.WriteLine(string.Format("{0,-11} {1,-12} {2,9:F3} {3,9:F3} {4,9:+0.000;-0.000;0.000} {5,20} {6,6}{7}", candidateMode.Mode, metric, b.Average(), c.Average(), delta, ci, b.Count, flag));
                }

                double p95Before = baselineMode.Latency.P95;
                double p95After = candidateMode.Latency.P95;
                bool slow = latencyTolerance > 0 && p95Before > 0 && p95After > p95Before * (1.0 + latencyTolerance);
                regressed |= slow;
                Console.WriteLine(string.Format("{0,-11} {1,-12} {2,9:F1} {3,9:F1} {4,9:+0.0;-0.0;0.0}{5}", candidateMode.Mode, "p95 ms", p95Before, p95After, p95After - p95Before, slow ? "  REGRESSION" : string.Empty));
            }

            Console.WriteLine();
            Console.WriteLine(regressed ? "RESULT: regression beyond tolerance" : "RESULT: no regression");
            return regressed ? 1 : 0;
        }

        #endregion

        #region Private-Methods

        private static RetrievalReport Load(string path)
        {
            RetrievalReport? report = JsonSerializer.Deserialize<RetrievalReport>(File.ReadAllText(path), HarnessJson.Options);
            if (report == null || !string.Equals(report.Kind, "retrieval", StringComparison.Ordinal))
                throw new InvalidDataException("'" + path + "' is not a retrieval report.");
            return report;
        }

        #endregion
    }
}
