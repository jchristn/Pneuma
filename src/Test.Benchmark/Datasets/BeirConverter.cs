namespace Test.Benchmark.Datasets
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text.Json;

    /// <summary>
    /// Converts a BEIR dataset directory (corpus.jsonl, queries.jsonl, qrels/{split}.tsv) into the neutral format as
    /// a single corpus. Only queries with at least one positive judgement in the split are kept, and qrels scores
    /// become graded relevance (NFCorpus uses grades 1 and 2).
    /// </summary>
    public static class BeirConverter
    {
        #region Public-Methods

        /// <summary>
        /// Convert a BEIR directory.
        /// </summary>
        /// <param name="directory">Directory holding corpus.jsonl, queries.jsonl, and qrels/.</param>
        /// <param name="name">Dataset name.</param>
        /// <param name="split">Qrels split (test by default).</param>
        /// <param name="limit">Maximum queries to keep (0 = all), sampled reproducibly.</param>
        /// <param name="seed">Sampling seed.</param>
        /// <returns>The dataset.</returns>
        /// <exception cref="FileNotFoundException">Thrown when a required file is missing.</exception>
        public static BenchmarkDataset Convert(string directory, string name, string split, int limit, int seed)
        {
            string corpusPath = Path.Combine(directory, "corpus.jsonl");
            string queriesPath = Path.Combine(directory, "queries.jsonl");
            string qrelsPath = Path.Combine(directory, "qrels", split + ".tsv");
            foreach (string path in new string[] { corpusPath, queriesPath, qrelsPath })
            {
                if (!File.Exists(path)) throw new FileNotFoundException("BEIR file not found: " + path, path);
            }

            Dictionary<string, Dictionary<string, int>> qrels = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            foreach (string line in File.ReadLines(qrelsPath).Skip(1))
            {
                string[] parts = line.Split('\t');
                if (parts.Length < 3) continue;
                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int grade) || grade <= 0) continue;
                if (!qrels.TryGetValue(parts[0], out Dictionary<string, int>? grades))
                {
                    grades = new Dictionary<string, int>(StringComparer.Ordinal);
                    qrels[parts[0]] = grades;
                }

                grades[parts[1]] = grade;
            }

            BenchmarkCorpus corpus = new BenchmarkCorpus { Id = name };
            corpus.Categories.Add(new BenchmarkCategory { Name = "general", Description = "BEIR corpus documents." });
            HashSet<string> documentIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in File.ReadLines(corpusPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                BeirRecord? record = JsonSerializer.Deserialize<BeirRecord>(line);
                if (record == null || string.IsNullOrWhiteSpace(record.Id) || !documentIds.Add(record.Id)) continue;
                corpus.Documents.Add(new BenchmarkDocument
                {
                    Id = record.Id,
                    Category = "general",
                    Title = string.IsNullOrWhiteSpace(record.Title) ? null : record.Title,
                    Body = record.Text ?? string.Empty,
                    Format = "txt"
                });
            }

            List<BenchmarkQuery> queries = new List<BenchmarkQuery>();
            foreach (string line in File.ReadLines(queriesPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                BeirRecord? record = JsonSerializer.Deserialize<BeirRecord>(line);
                if (record == null || !qrels.TryGetValue(record.Id, out Dictionary<string, int>? grades)) continue;
                List<string> relevant = grades.Where(g => documentIds.Contains(g.Key)).OrderByDescending(g => g.Value).Select(g => g.Key).ToList();
                if (relevant.Count == 0) continue;
                bool graded = grades.Values.Any(g => g > 1);
                queries.Add(new BenchmarkQuery
                {
                    Id = record.Id,
                    Text = record.Text,
                    Type = "default",
                    Relevant = relevant,
                    Grades = graded ? grades.Where(g => documentIds.Contains(g.Key)).ToDictionary(g => g.Key, g => g.Value, StringComparer.Ordinal) : null,
                    Hops = relevant.Count
                });
            }

            if (limit > 0 && queries.Count > limit)
            {
                Random random = new Random(seed);
                queries = queries.OrderBy(q => random.Next()).Take(limit).OrderBy(q => q.Id, StringComparer.Ordinal).ToList();
            }

            corpus.Queries = queries;
            return new BenchmarkDataset
            {
                Name = name,
                Description = "BEIR '" + name + "' (" + split + " split): " + corpus.Documents.Count + " documents, " + queries.Count
                    + " queries with at least one positive judgement. Converted from the public BEIR release; not redistributed.",
                Corpora = new List<BenchmarkCorpus> { corpus }
            };
        }

        #endregion
    }
}
