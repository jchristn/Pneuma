namespace Test.Benchmark.Reporting
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using Test.Benchmark.Metrics;
    using Test.Benchmark.Runners;

    /// <summary>
    /// Writes each report as <c>&lt;utc-stamp&gt;-&lt;kind&gt;-&lt;name&gt;[-label].json</c> for machines and a matching
    /// <c>.md</c> for people.
    /// </summary>
    public static class ReportWriter
    {
        #region Public-Methods

        /// <summary>
        /// Write a report's JSON and Markdown.
        /// </summary>
        /// <param name="directory">Results directory.</param>
        /// <param name="kind">Report kind.</param>
        /// <param name="name">Dataset or suite name.</param>
        /// <param name="label">Optional run label.</param>
        /// <param name="report">The report object.</param>
        /// <param name="markdown">Its Markdown rendering.</param>
        /// <returns>The JSON path.</returns>
        public static string Write(string directory, string kind, string name, string? label, object report, string markdown)
        {
            Directory.CreateDirectory(directory);
            string stem = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + kind + "-" + Safe(name) + (string.IsNullOrWhiteSpace(label) ? string.Empty : "-" + Safe(label!));
            string jsonPath = Path.Combine(directory, stem + ".json");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, report.GetType(), HarnessJson.Indented));
            File.WriteAllText(Path.Combine(directory, stem + ".md"), markdown);
            Console.WriteLine("[report] " + jsonPath);
            return jsonPath;
        }

        /// <summary>
        /// Render a retrieval report.
        /// </summary>
        /// <param name="report">The report.</param>
        /// <returns>Markdown.</returns>
        public static string Retrieval(RetrievalReport report)
        {
            StringBuilder md = new StringBuilder();
            md.AppendLine("# Retrieval: " + report.Dataset + (report.Label != null ? " (" + report.Label + ")" : string.Empty));
            md.AppendLine();
            AppendEnvironment(md, report.Environment, report.Config);
            AppendIngest(md, report.Ingest);

            md.AppendLine("## Summary");
            md.AppendLine();
            string[] headline = new string[] { "hit@1", "recall@5", "recall@10", "all@10", "mrr@10", "ndcg@10", "evidence@10" };
            md.AppendLine("| Mode | Queries | " + string.Join(" | ", headline) + " | p50 ms | p95 ms | Errors |");
            md.AppendLine("|---|---|" + string.Concat(headline.Select(h => "---|")) + "---|---|---|");
            foreach (ModeSummary mode in report.Modes)
            {
                md.Append("| ").Append(mode.Mode).Append(" | ").Append(mode.Queries).Append(" | ");
                foreach (string metric in headline) md.Append(Cell(mode, metric)).Append(" | ");
                md.Append(mode.Latency.P50.ToString("F1", CultureInfo.InvariantCulture)).Append(" | ").Append(mode.Latency.P95.ToString("F1", CultureInfo.InvariantCulture)).Append(" | ").Append(mode.Errors).AppendLine(" |");
            }

            md.AppendLine();
            md.AppendLine("Values are means over answerable queries; brackets are 95% bootstrap intervals for nDCG@10.");
            md.AppendLine();

            md.AppendLine("## nDCG@10 by query type");
            md.AppendLine();
            List<string> types = report.Modes.SelectMany(m => m.ByType.Keys).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList();
            md.AppendLine("| Type | n | " + string.Join(" | ", report.Modes.Select(m => m.Mode)) + " |");
            md.AppendLine("|---|---|" + string.Concat(report.Modes.Select(m => "---|")));
            foreach (string type in types)
            {
                ModeSummary? any = report.Modes.FirstOrDefault(m => m.ByType.ContainsKey(type));
                double count = any != null ? any.ByType[type]["count"] : 0;
                md.Append("| ").Append(type).Append(" | ").Append(count.ToString("F0", CultureInfo.InvariantCulture)).Append(" | ");
                foreach (ModeSummary mode in report.Modes)
                {
                    md.Append(mode.ByType.TryGetValue(type, out Dictionary<string, double>? metrics) && metrics.TryGetValue("ndcg@10", out double v) ? v.ToString("F3", CultureInfo.InvariantCulture) : "-").Append(" | ");
                }

                md.AppendLine();
            }

            md.AppendLine();
            md.AppendLine("## Hit@1 by query type");
            md.AppendLine();
            md.AppendLine("| Type | " + string.Join(" | ", report.Modes.Select(m => m.Mode)) + " |");
            md.AppendLine("|---|" + string.Concat(report.Modes.Select(m => "---|")));
            foreach (string type in types)
            {
                md.Append("| ").Append(type).Append(" | ");
                foreach (ModeSummary mode in report.Modes)
                {
                    md.Append(mode.ByType.TryGetValue(type, out Dictionary<string, double>? metrics) && metrics.TryGetValue("hit@1", out double v) ? v.ToString("F3", CultureInfo.InvariantCulture) : "-").Append(" | ");
                }

                md.AppendLine();
            }

            md.AppendLine();
            md.AppendLine("## Can a score say \"nothing relevant\"?");
            md.AppendLine();
            md.AppendLine("AUROC of the top hit's score as a classifier of answerable vs unanswerable questions (0.5 = no signal, 1.0 = perfect threshold).");
            md.AppendLine();
            md.AppendLine("| Mode | Negatives | Mean top score (answerable / negative) | Reported score | Vector score | Fused score | Summary-chunk share |");
            md.AppendLine("|---|---|---|---|---|---|---|");
            foreach (ModeSummary mode in report.Modes.Where(m => !m.Mode.StartsWith("ref-", StringComparison.Ordinal)))
            {
                md.Append("| ").Append(mode.Mode).Append(" | ").Append(mode.NegativeQueries).Append(" | ")
                    .Append(Num(mode.MeanTopScoreAnswerable)).Append(" / ").Append(Num(mode.MeanTopScoreNegative)).Append(" | ")
                    .Append(Num(mode.ScoreAuroc)).Append(" | ").Append(Num(mode.VectorScoreAuroc)).Append(" | ").Append(Num(mode.FusedScoreAuroc)).Append(" | ")
                    .Append(Num(mode.SummaryShare)).AppendLine(" |");
            }

            md.AppendLine();
            AppendStages(md, report.Modes);
            return md.ToString();
        }

        /// <summary>
        /// Render an answering report.
        /// </summary>
        /// <param name="report">The report.</param>
        /// <returns>Markdown.</returns>
        public static string Answer(AnswerReport report)
        {
            StringBuilder md = new StringBuilder();
            md.AppendLine("# Answering (" + report.Endpoint + "): " + report.Dataset + (report.Label != null ? " (" + report.Label + ")" : string.Empty));
            md.AppendLine();
            AppendEnvironment(md, report.Environment, report.Config);
            AppendIngest(md, report.Ingest);

            md.AppendLine("## Summary");
            md.AppendLine();
            md.AppendLine("| Metric | Value |");
            md.AppendLine("|---|---|");
            string[] order = new string[]
            {
                "accuracy", "evidenceInContext", "accuracyWithEvidence", "accuracyWithoutEvidence", "evidenceCoverage", "abstention",
                "insufficientSupportOnNegatives", "citationRate", "citationPrecision", "citationRecall", "faithfulness", "meanToolCalls",
                "accuracyRunStdDev", "answerableWithEvidence", "answerableWithoutEvidence", "negatives", "items", "errors", "unparsedVerdicts"
            };
            foreach (string name in order)
            {
                if (!report.Summary.TryGetValue(name, out double value)) continue;
                string text = value == Math.Floor(value) && value > 1 ? value.ToString("F0", CultureInfo.InvariantCulture) : value.ToString("F3", CultureInfo.InvariantCulture);
                if (name == "accuracy" && report.AccuracyInterval != null) text += " [" + report.AccuracyInterval.Low.ToString("F3", CultureInfo.InvariantCulture) + ", " + report.AccuracyInterval.High.ToString("F3", CultureInfo.InvariantCulture) + "]";
                md.AppendLine("| " + name + " | " + text + " |");
            }

            md.AppendLine("| latency p50 / p95 ms | " + report.Latency.P50.ToString("F0", CultureInfo.InvariantCulture) + " / " + report.Latency.P95.ToString("F0", CultureInfo.InvariantCulture) + " |");
            if (report.AccuracyByRun.Count > 1) md.AppendLine("| accuracy by run | " + string.Join(", ", report.AccuracyByRun.Select(a => a.ToString("F3", CultureInfo.InvariantCulture))) + " |");
            md.AppendLine();
            md.AppendLine("Accuracy is over answerable questions; abstention is the share of unanswerable questions correctly declined. Accuracy is split by whether a relevant document or gold evidence span reached the model, which separates retrieval misses from generation misses.");
            md.AppendLine();

            md.AppendLine("## By query type");
            md.AppendLine();
            md.AppendLine("| Type | n | Correct | Evidence in context |");
            md.AppendLine("|---|---|---|---|");
            foreach (KeyValuePair<string, Dictionary<string, double>> type in report.ByType)
            {
                md.AppendLine("| " + type.Key + " | " + type.Value["count"].ToString("F0", CultureInfo.InvariantCulture) + " | " + type.Value["correct"].ToString("F3", CultureInfo.InvariantCulture) + " | "
                    + (type.Value.TryGetValue("evidenceInContext", out double e) ? e.ToString("F3", CultureInfo.InvariantCulture) : "-") + " |");
            }

            md.AppendLine();
            if (report.Stages.Count > 0)
            {
                md.AppendLine("## Server-side stages");
                md.AppendLine();
                md.AppendLine("| Stage | Count | Mean ms |");
                md.AppendLine("|---|---|---|");
                foreach (KeyValuePair<string, StageBreakdown> stage in report.Stages.OrderByDescending(s => s.Value.TotalMs).Take(25))
                {
                    md.AppendLine("| " + stage.Key.Replace("|", "/") + " | " + stage.Value.Count + " | " + stage.Value.MeanMs.ToString("F1", CultureInfo.InvariantCulture) + " |");
                }

                md.AppendLine();
            }

            md.AppendLine("## Failures (first 25 incorrect answerable items)");
            md.AppendLine();
            foreach (AnswerItem item in report.Items.Where(i => i.Answerable && i.Correct == false).Take(25))
            {
                md.AppendLine("- **" + item.QueryId + "** (" + item.Type + ", evidence " + (item.EvidenceInContext ? "in" : "NOT in") + " context): " + item.Question);
                md.AppendLine("  - gold: " + OneLine(item.Gold, 200));
                md.AppendLine("  - answer: " + OneLine(item.Answer, 300));
            }

            md.AppendLine();
            return md.ToString();
        }

        /// <summary>
        /// Render an ingest fidelity report.
        /// </summary>
        /// <param name="report">The report.</param>
        /// <returns>Markdown.</returns>
        public static string Ingest(IngestReport report)
        {
            StringBuilder md = new StringBuilder();
            md.AppendLine("# Ingest fidelity: " + report.Dataset + (report.Label != null ? " (" + report.Label + ")" : string.Empty));
            md.AppendLine();
            AppendEnvironment(md, report.Environment, report.Config);
            AppendIngest(md, report.Ingest);
            md.AppendLine("## Summary");
            md.AppendLine();
            md.AppendLine("| Metric | All | " + string.Join(" | ", report.ByFormat.Keys) + " |");
            md.AppendLine("|---|---|" + string.Concat(report.ByFormat.Keys.Select(k => "---|")));
            foreach (string metric in report.Summary.Keys)
            {
                md.Append("| ").Append(metric).Append(" | ").Append(Plain(report.Summary[metric])).Append(" | ");
                foreach (Dictionary<string, double> format in report.ByFormat.Values) md.Append(format.TryGetValue(metric, out double v) ? Plain(v) : "-").Append(" | ");
                md.AppendLine();
            }

            md.AppendLine();
            md.AppendLine("Extraction coverage is the share of a source document's distinct words (3+ letters) that survive into its extracted cells. A chunk counts as a summary chunk when its text is not found in the cells.");
            md.AppendLine();
            md.AppendLine("## Lowest extraction coverage");
            md.AppendLine();
            md.AppendLine("| Document | Format | Coverage | Cells | Chunks |");
            md.AppendLine("|---|---|---|---|---|");
            foreach (IngestDocument document in report.Documents.Where(d => d.Measured).OrderBy(d => d.ExtractionCoverage).Take(15))
            {
                md.AppendLine("| " + document.DocumentId + " | " + document.Format + " | " + document.ExtractionCoverage.ToString("F3", CultureInfo.InvariantCulture) + " | " + document.Cells + " | " + document.Chunks + " |");
            }

            md.AppendLine();
            return md.ToString();
        }

        /// <summary>
        /// Render a load report.
        /// </summary>
        /// <param name="report">The report.</param>
        /// <returns>Markdown.</returns>
        public static string Load(LoadReport report)
        {
            StringBuilder md = new StringBuilder();
            md.AppendLine("# Load: " + report.Dataset + (report.Label != null ? " (" + report.Label + ")" : string.Empty));
            md.AppendLine();
            AppendEnvironment(md, report.Environment, report.Config);
            md.AppendLine("| Concurrency | Ops | Throughput ops/s | p50 ms | p95 ms | p99 ms | Error rate |");
            md.AppendLine("|---|---|---|---|---|---|---|");
            foreach (LoadLevel level in report.Levels)
            {
                md.AppendLine("| " + level.Concurrency + " | " + level.Operations + " | " + level.Throughput.ToString("F1", CultureInfo.InvariantCulture) + " | " + level.Latency.P50.ToString("F1", CultureInfo.InvariantCulture)
                    + " | " + level.Latency.P95.ToString("F1", CultureInfo.InvariantCulture) + " | " + level.Latency.P99.ToString("F1", CultureInfo.InvariantCulture) + " | " + level.ErrorRate.ToString("P2", CultureInfo.InvariantCulture) + " |");
            }

            md.AppendLine();
            foreach (LoadLevel level in report.Levels.Where(l => l.Stages.Count > 0))
            {
                md.AppendLine("Stages at concurrency " + level.Concurrency + ": " + string.Join("; ", level.Stages.Where(s => s.Key.StartsWith("retrieval", StringComparison.Ordinal) || s.Key.StartsWith("integration", StringComparison.Ordinal))
                    .OrderByDescending(s => s.Value.TotalMs).Take(8).Select(s => s.Key + " " + s.Value.MeanMs.ToString("F1", CultureInfo.InvariantCulture) + " ms")));
                md.AppendLine();
            }

            return md.ToString();
        }

        /// <summary>
        /// Render a global-vs-local report.
        /// </summary>
        /// <param name="report">The report.</param>
        /// <returns>Markdown.</returns>
        public static string Global(GlobalReport report)
        {
            StringBuilder md = new StringBuilder();
            md.AppendLine("# Global vs local thematic answers: " + report.Dataset);
            md.AppendLine();
            AppendEnvironment(md, report.Environment, report.Config);
            md.AppendLine("Pairwise judge, both presentation orders; a split verdict counts as a tie. Weaker evidence than the ranked metrics.");
            md.AppendLine();
            md.AppendLine("| Criterion | Global wins | Local wins | Tie |");
            md.AppendLine("|---|---|---|---|");
            foreach (KeyValuePair<string, Dictionary<string, double>> criterion in report.WinRates)
            {
                md.AppendLine("| " + criterion.Key + " | " + criterion.Value["global"].ToString("P0", CultureInfo.InvariantCulture) + " | " + criterion.Value["local"].ToString("P0", CultureInfo.InvariantCulture) + " | " + criterion.Value["tie"].ToString("P0", CultureInfo.InvariantCulture) + " |");
            }

            md.AppendLine();
            return md.ToString();
        }

        /// <summary>
        /// Render an agent report.
        /// </summary>
        /// <param name="report">The report.</param>
        /// <returns>Markdown.</returns>
        public static string Agent(Test.Benchmark.Agent.AgentReport report)
        {
            StringBuilder md = new StringBuilder();
            md.AppendLine("# Agent: " + report.Suite);
            md.AppendLine();
            AppendEnvironment(md, report.Environment, report.Config);
            md.AppendLine("| Arm | Tasks | Success | Mean turns | Mean cost | Total cost | Errors |");
            md.AppendLine("|---|---|---|---|---|---|---|");
            foreach (KeyValuePair<string, Dictionary<string, double>> arm in report.Arms)
            {
                md.AppendLine("| " + arm.Key + " | " + arm.Value["tasks"] + " | " + arm.Value["successRate"].ToString("P0", CultureInfo.InvariantCulture) + " | " + arm.Value["meanTurns"].ToString("F1", CultureInfo.InvariantCulture)
                    + " | $" + arm.Value["meanCostUsd"].ToString("F4", CultureInfo.InvariantCulture) + " | $" + arm.Value["totalCostUsd"].ToString("F2", CultureInfo.InvariantCulture) + " | " + arm.Value["errors"] + " |");
            }

            md.AppendLine();
            foreach (Test.Benchmark.Agent.AgentItem item in report.Items.Where(i => !i.Success && i.Arm != "none"))
            {
                md.AppendLine("- " + item.TaskId + " (" + item.Arm + "): " + OneLine(item.Error ?? item.Answer, 240));
            }

            md.AppendLine();
            return md.ToString();
        }

        /// <summary>
        /// Render the shared environment block.
        /// </summary>
        /// <param name="md">Builder.</param>
        /// <param name="environment">Environment.</param>
        /// <param name="config">Configuration.</param>
        public static void AppendEnvironment(StringBuilder md, BenchmarkEnvironment environment, Dictionary<string, string> config)
        {
            md.AppendLine("- Started: " + environment.StartedUtc.ToString("u", CultureInfo.InvariantCulture));
            md.AppendLine("- Server: " + environment.ServerUrl + " at commit `" + environment.GitCommit + "`");
            md.AppendLine("- Machine: " + environment.Machine);
            md.AppendLine("- Embedding: " + environment.Embedding);
            md.AppendLine("- Inference: " + environment.Inference);
            if (!string.IsNullOrEmpty(environment.Judge)) md.AppendLine("- Judge: " + environment.Judge);
            md.AppendLine("- Ingest profile: " + environment.IngestProfile);
            foreach (KeyValuePair<string, string> entry in config.OrderBy(c => c.Key, StringComparer.Ordinal)) md.AppendLine("- " + entry.Key + ": " + entry.Value);
            md.AppendLine();
        }

        /// <summary>
        /// Render the ingest block.
        /// </summary>
        /// <param name="md">Builder.</param>
        /// <param name="ingest">Ingest summary.</param>
        public static void AppendIngest(StringBuilder md, IngestSummary ingest)
        {
            md.AppendLine("## Ingest");
            md.AppendLine();
            md.AppendLine("- Documents: " + ingest.Succeeded + "/" + ingest.Documents + " ingested; " + ingest.Submitted + " submitted this run, " + ingest.ReusedSubjects + " subject(s) reused");
            if (ingest.Submitted > 0) md.AppendLine("- Wall time: " + ingest.WallSeconds + " s (" + ingest.DocumentsPerSecond + " documents/s)");
            if (!ingest.Complete)
            {
                md.AppendLine("- **Incomplete ingest: " + ingest.Failures + " failure(s). These results are not comparable.**");
                foreach (KeyValuePair<string, int> stage in ingest.FailuresByStage) md.AppendLine("  - " + stage.Key + ": " + stage.Value);
                foreach (string sample in ingest.FailureSamples) md.AppendLine("  - " + sample);
            }

            if (ingest.Stages.Count > 0)
            {
                md.AppendLine();
                md.AppendLine("| Ingestion stage | Count | Mean ms | Total s |");
                md.AppendLine("|---|---|---|---|");
                foreach (KeyValuePair<string, StageBreakdown> stage in ingest.Stages.OrderByDescending(s => s.Value.TotalMs))
                {
                    md.AppendLine("| " + stage.Key + " | " + stage.Value.Count + " | " + stage.Value.MeanMs.ToString("F1", CultureInfo.InvariantCulture) + " | " + (stage.Value.TotalMs / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + " |");
                }
            }

            md.AppendLine();
        }

        /// <summary>
        /// Format a nullable number.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Formatted text or a dash.</returns>
        public static string Num(double? value)
        {
            return value.HasValue ? value.Value.ToString("F3", CultureInfo.InvariantCulture) : "-";
        }

        #endregion

        #region Private-Methods

        private static string Cell(ModeSummary mode, string metric)
        {
            if (!mode.Metrics.TryGetValue(metric, out double value)) return "-";
            string text = value.ToString("F3", CultureInfo.InvariantCulture);
            if (metric == "ndcg@10" && mode.Intervals.TryGetValue(metric, out ConfidenceInterval? interval))
                text += " [" + interval.Low.ToString("F3", CultureInfo.InvariantCulture) + ", " + interval.High.ToString("F3", CultureInfo.InvariantCulture) + "]";
            return text;
        }

        private static void AppendStages(StringBuilder md, List<ModeSummary> modes)
        {
            List<ModeSummary> withStages = modes.Where(m => m.Stages.Count > 0).ToList();
            if (withStages.Count == 0) return;
            md.AppendLine("## Server-side stages");
            md.AppendLine();
            md.AppendLine("Mean time per observation during each mode's queries, from Prometheus histogram deltas.");
            md.AppendLine();
            md.AppendLine("| Mode | Stage | Count | Mean ms |");
            md.AppendLine("|---|---|---|---|");
            foreach (ModeSummary mode in withStages)
            {
                foreach (KeyValuePair<string, StageBreakdown> stage in mode.Stages.OrderByDescending(s => s.Value.TotalMs))
                {
                    md.AppendLine("| " + mode.Mode + " | " + stage.Key.Replace("|", "/") + " | " + stage.Value.Count + " | " + stage.Value.MeanMs.ToString("F1", CultureInfo.InvariantCulture) + " |");
                }
            }

            md.AppendLine();
        }

        private static string Plain(double value)
        {
            return value == Math.Floor(value) && Math.Abs(value) >= 1 ? value.ToString("F0", CultureInfo.InvariantCulture) : value.ToString("F3", CultureInfo.InvariantCulture);
        }

        private static string OneLine(string text, int max)
        {
            string flat = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
            return flat.Length > max ? flat.Substring(0, max) + "..." : flat;
        }

        private static string Safe(string text)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in text.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) || c == '-' ? c : '-');
            return sb.ToString().Trim('-');
        }

        #endregion
    }
}
