namespace Test.Benchmark.Datasets
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// Converts MultiHop-RAG (Tang and Yang, 2024: corpus.json plus MultiHopRAG.json) into the neutral format. The
    /// whole article corpus is kept as distractors; queries are optionally sampled, stratified by question type.
    /// Evidence articles are matched by URL (falling back to title); null queries become unanswerable questions.
    /// </summary>
    public static class MultiHopRagConverter
    {
        #region Public-Methods

        /// <summary>
        /// Convert a MultiHop-RAG download.
        /// </summary>
        /// <param name="directory">Directory holding corpus.json and MultiHopRAG.json.</param>
        /// <param name="name">Dataset name.</param>
        /// <param name="limit">Maximum queries to keep (0 = all), stratified by question type.</param>
        /// <param name="seed">Sampling seed.</param>
        /// <returns>The dataset.</returns>
        /// <exception cref="FileNotFoundException">Thrown when a file is missing.</exception>
        /// <exception cref="InvalidDataException">Thrown when a file cannot be parsed.</exception>
        public static BenchmarkDataset Convert(string directory, string name, int limit, int seed)
        {
            string corpusPath = Path.Combine(directory, "corpus.json");
            string queriesPath = Path.Combine(directory, "MultiHopRAG.json");
            foreach (string path in new string[] { corpusPath, queriesPath })
            {
                if (!File.Exists(path)) throw new FileNotFoundException("MultiHop-RAG file not found: " + path, path);
            }

            List<MultiHopRagArticle>? articles = JsonSerializer.Deserialize<List<MultiHopRagArticle>>(File.ReadAllText(corpusPath));
            List<MultiHopRagQuery>? source = JsonSerializer.Deserialize<List<MultiHopRagQuery>>(File.ReadAllText(queriesPath));
            if (articles == null || source == null) throw new InvalidDataException("Could not parse the MultiHop-RAG files.");

            BenchmarkCorpus corpus = new BenchmarkCorpus { Id = name };
            Dictionary<string, string> idByUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> idByTitle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> categories = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (MultiHopRagArticle article in articles)
            {
                index++;
                string id = "mh-" + index.ToString("D4") + "-" + Slug(article.Title, 48);
                string category = string.IsNullOrWhiteSpace(article.Category) ? "news" : Slug(article.Category!, 32);
                categories.Add(category);
                if (!string.IsNullOrWhiteSpace(article.Url)) idByUrl[article.Url!.Trim()] = id;
                if (!string.IsNullOrWhiteSpace(article.Title)) idByTitle[article.Title.Trim()] = id;
                corpus.Documents.Add(new BenchmarkDocument
                {
                    Id = id,
                    Category = category,
                    Title = article.Title,
                    Summary = (article.Source ?? string.Empty) + (string.IsNullOrWhiteSpace(article.PublishedAt) ? string.Empty : ", " + article.PublishedAt),
                    Body = article.Body ?? string.Empty,
                    Date = ToDate(article.PublishedAt),
                    Format = "txt",
                    Tags = string.IsNullOrWhiteSpace(article.Source) ? null : new Dictionary<string, string> { { "publisher", article.Source! } }
                });
            }

            foreach (string category in categories.OrderBy(c => c, StringComparer.Ordinal))
            {
                corpus.Categories.Add(new BenchmarkCategory { Name = category, Description = "MultiHop-RAG news category." });
            }

            List<BenchmarkQuery> queries = new List<BenchmarkQuery>();
            int queryIndex = 0;
            int unmatched = 0;
            foreach (MultiHopRagQuery item in source)
            {
                queryIndex++;
                string type = item.QuestionType.EndsWith("_query", StringComparison.Ordinal) ? item.QuestionType.Substring(0, item.QuestionType.Length - 6) : item.QuestionType;
                bool isNull = string.Equals(type, "null", StringComparison.Ordinal);
                List<string> relevant = new List<string>();
                List<string> evidence = new List<string>();
                foreach (MultiHopRagArticle fact in item.EvidenceList)
                {
                    string? id = null;
                    if (!string.IsNullOrWhiteSpace(fact.Url) && idByUrl.TryGetValue(fact.Url!.Trim(), out string? byUrl)) id = byUrl;
                    else if (!string.IsNullOrWhiteSpace(fact.Title) && idByTitle.TryGetValue(fact.Title.Trim(), out string? byTitle)) id = byTitle;
                    if (id == null)
                    {
                        unmatched++;
                        continue;
                    }

                    if (!relevant.Contains(id)) relevant.Add(id);
                    if (!string.IsNullOrWhiteSpace(fact.Fact)) evidence.Add(fact.Fact!.Trim());
                }

                if (isNull) relevant.Clear();
                if (!isNull && relevant.Count == 0) continue;
                queries.Add(new BenchmarkQuery
                {
                    Id = "mh-q" + queryIndex.ToString("D4"),
                    Text = item.Query,
                    Type = isNull ? "negative" : type,
                    Relevant = relevant,
                    Answer = isNull ? DatasetStore.NotInCorpus : item.Answer,
                    Evidence = isNull || evidence.Count == 0 ? null : evidence,
                    Hops = isNull ? 0 : relevant.Count
                });
            }

            if (limit > 0 && queries.Count > limit) queries = Stratify(queries, limit, seed);
            corpus.Queries = queries;

            return new BenchmarkDataset
            {
                Name = name,
                Description = "MultiHop-RAG (Tang and Yang, 2024): " + corpus.Documents.Count + " news articles, " + queries.Count + " queries"
                    + (limit > 0 ? " (stratified sample, seed " + seed + ")" : string.Empty) + ". Null queries are unanswerable ('negative'). "
                    + unmatched + " evidence entries could not be matched to an article. Converted from the public release; not redistributed.",
                Corpora = new List<BenchmarkCorpus> { corpus }
            };
        }

        #endregion

        #region Private-Methods

        private static List<BenchmarkQuery> Stratify(List<BenchmarkQuery> queries, int limit, int seed)
        {
            // Round-robin across types over a per-type shuffle, so a limited sample keeps every type represented.
            Random random = new Random(seed);
            List<Queue<BenchmarkQuery>> buckets = queries.GroupBy(q => q.Type).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new Queue<BenchmarkQuery>(g.OrderBy(q => random.Next()))).ToList();
            List<BenchmarkQuery> sample = new List<BenchmarkQuery>();
            while (sample.Count < limit && buckets.Any(b => b.Count > 0))
            {
                foreach (Queue<BenchmarkQuery> bucket in buckets)
                {
                    if (sample.Count >= limit) break;
                    if (bucket.Count > 0) sample.Add(bucket.Dequeue());
                }
            }

            return sample.OrderBy(q => q.Id, StringComparer.Ordinal).ToList();
        }

        private static string? ToDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime parsed)
                ? parsed.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                : null;
        }

        private static string Slug(string text, int max)
        {
            StringBuilder sb = new StringBuilder();
            bool dash = false;
            foreach (char c in (text ?? string.Empty).ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) && c < 128)
                {
                    sb.Append(c);
                    dash = false;
                }
                else if (!dash && sb.Length > 0)
                {
                    sb.Append('-');
                    dash = true;
                }

                if (sb.Length >= max) break;
            }

            return sb.ToString().Trim('-');
        }

        #endregion
    }
}
