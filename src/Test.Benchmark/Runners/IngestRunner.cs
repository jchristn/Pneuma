namespace Test.Benchmark.Runners
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Datasets;
    using Test.Benchmark.Reference;

    /// <summary>
    /// Ingest fidelity: provisions (or reuses) a dataset's subjects and reads each link's pipeline artifacts to
    /// measure what the pipeline kept and produced: extraction coverage (how much of the source text survives into
    /// the extracted cells), chunk counts and sizes, the share of chunks cut from LLM summaries rather than content,
    /// redundant tail chunks, and U+FFFD corruption. Together with the ingest summary (failures by stage, stage
    /// timings) it shows where the pipeline loses information before retrieval ever runs.
    /// </summary>
    public class IngestRunner
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
        public IngestRunner(BenchmarkContext context)
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
        public async Task<IngestReport> RunAsync(BenchmarkDataset dataset, CancellationToken token)
        {
            IngestReport report = new IngestReport { Dataset = dataset.Name, Label = _Context.Arguments.GetOptional("label"), Environment = _Context.Environment };
            report.Config["ingestProfile"] = _Context.IngestProfile;
            List<ProvisionedSubject> subjects = await new SubjectProvisioner(_Context).ProvisionAsync(dataset, report.Ingest, token).ConfigureAwait(false);

            foreach (ProvisionedSubject subject in subjects)
            {
                foreach (BenchmarkDocument document in subject.Corpus.Documents)
                {
                    if (!subject.LinkIdByDocId.TryGetValue(document.Id, out string? linkId)) continue;
                    report.Documents.Add(await MeasureAsync(document, linkId, token).ConfigureAwait(false));
                }
            }

            List<IngestDocument> measured = report.Documents.Where(d => d.Measured).ToList();
            report.Summary = Aggregate(measured);
            report.Summary["documents"] = report.Documents.Count;
            report.Summary["measured"] = measured.Count;
            foreach (IGrouping<string, IngestDocument> group in measured.GroupBy(d => d.Format).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                Dictionary<string, double> byFormat = Aggregate(group.ToList());
                byFormat["documents"] = group.Count();
                report.ByFormat[group.Key] = byFormat;
            }

            Console.WriteLine("[ingest] " + measured.Count + "/" + report.Documents.Count + " documents measured; extraction coverage "
                + Get(report.Summary, "extractionCoverage") + ", chunks/doc " + Get(report.Summary, "chunksPerDocument") + ", summary share "
                + Get(report.Summary, "summaryChunkShare") + ", redundant share " + Get(report.Summary, "redundantChunkShare"));
            return report;
        }

        #endregion

        #region Private-Methods

        private async Task<IngestDocument> MeasureAsync(BenchmarkDocument document, string linkId, CancellationToken token)
        {
            IngestDocument result = new IngestDocument { DocumentId = document.Id, Format = (document.Format ?? "md").ToLowerInvariant() };
            string? atomsJson = await _Context.Client.GetLinkArtifactAsync(linkId, "atoms", token).ConfigureAwait(false);
            string? chunksJson = await _Context.Client.GetLinkArtifactAsync(linkId, "chunks", token).ConfigureAwait(false);
            if (atomsJson == null || chunksJson == null) return result;

            List<ExtractedCellInfo>? cells;
            List<string>? chunks;
            try
            {
                cells = JsonSerializer.Deserialize<List<ExtractedCellInfo>>(atomsJson, HarnessJson.Options);
                chunks = JsonSerializer.Deserialize<List<string>>(chunksJson, HarnessJson.Options);
            }
            catch (JsonException)
            {
                return result;
            }

            if (cells == null || chunks == null) return result;
            result.Measured = true;
            result.Cells = cells.Count;
            result.Chunks = chunks.Count;

            string cellText = EvidenceMatcher.Normalize(string.Join("\n", cells.Select(c => c.Text ?? string.Empty)));
            HashSet<string> sourceWords = Words(ReferenceArm.PlainText(document));
            HashSet<string> cellWords = Words(cellText);
            result.ExtractionCoverage = sourceWords.Count == 0 ? 1.0 : Math.Round((double)sourceWords.Count(w => cellWords.Contains(w)) / sourceWords.Count, 4);

            string? previous = null;
            List<int> contentWords = new List<int>();
            foreach (string chunk in chunks)
            {
                string normalized = EvidenceMatcher.Normalize(chunk ?? string.Empty);
                result.ReplacementCharacters += (chunk ?? string.Empty).Count(c => c == '�');
                if (previous != null && normalized.Length > 0 && previous.Contains(normalized, StringComparison.Ordinal)) result.RedundantChunks++;
                if (normalized.Length > 0 && cellText.Contains(normalized, StringComparison.Ordinal))
                {
                    result.ContentChunks++;
                    contentWords.Add(normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
                }
                else
                {
                    result.SummaryChunks++;
                }

                previous = normalized;
            }

            result.MeanChunkWords = contentWords.Count > 0 ? Math.Round(contentWords.Average(), 1) : 0.0;
            return result;
        }

        private static Dictionary<string, double> Aggregate(List<IngestDocument> documents)
        {
            Dictionary<string, double> metrics = new Dictionary<string, double>();
            if (documents.Count == 0) return metrics;
            int chunks = documents.Sum(d => d.Chunks);
            metrics["extractionCoverage"] = Math.Round(documents.Average(d => d.ExtractionCoverage), 4);
            metrics["extractionCoverageMin"] = Math.Round(documents.Min(d => d.ExtractionCoverage), 4);
            metrics["documentsBelow90PercentCoverage"] = documents.Count(d => d.ExtractionCoverage < 0.9);
            metrics["cellsPerDocument"] = Math.Round(documents.Average(d => d.Cells), 2);
            metrics["chunksPerDocument"] = Math.Round(documents.Average(d => d.Chunks), 2);
            metrics["summaryChunkShare"] = chunks > 0 ? Math.Round((double)documents.Sum(d => d.SummaryChunks) / chunks, 4) : 0.0;
            metrics["redundantChunkShare"] = chunks > 0 ? Math.Round((double)documents.Sum(d => d.RedundantChunks) / chunks, 4) : 0.0;
            metrics["replacementCharacters"] = documents.Sum(d => d.ReplacementCharacters);
            List<IngestDocument> sized = documents.Where(d => d.MeanChunkWords > 0).ToList();
            if (sized.Count > 0) metrics["meanChunkWords"] = Math.Round(sized.Average(d => d.MeanChunkWords), 1);
            return metrics;
        }

        private static HashSet<string> Words(string text)
        {
            HashSet<string> words = new HashSet<string>(StringComparer.Ordinal);
            StringBuilder word = new StringBuilder();
            foreach (char raw in (text ?? string.Empty) + " ")
            {
                char c = char.ToLowerInvariant(raw);
                if (char.IsLetterOrDigit(c))
                {
                    word.Append(c);
                }
                else if (word.Length > 0)
                {
                    if (word.Length > 2) words.Add(word.ToString());
                    word.Clear();
                }
            }

            return words;
        }

        private static string Get(Dictionary<string, double> values, string name)
        {
            return values.TryGetValue(name, out double value) ? value.ToString("F3") : "n/a";
        }

        #endregion
    }
}
