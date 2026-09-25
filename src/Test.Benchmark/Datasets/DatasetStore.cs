namespace Test.Benchmark.Datasets
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;

    /// <summary>
    /// Loads and saves datasets in the neutral JSON format, validating ids and relevance labels on load.
    /// </summary>
    public static class DatasetStore
    {
        #region Public-Members

        /// <summary>
        /// The gold answer that marks an unanswerable question (Isis datasets use NOT_IN_MEMORY).
        /// </summary>
        public const string NotInCorpus = "NOT_IN_CORPUS";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load and validate a dataset.
        /// </summary>
        /// <param name="path">Dataset file path.</param>
        /// <returns>The dataset.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
        /// <exception cref="InvalidDataException">Thrown when the dataset is malformed.</exception>
        public static BenchmarkDataset Load(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Dataset not found: " + path, path);
            BenchmarkDataset? dataset;
            using (FileStream stream = File.OpenRead(path))
            {
                dataset = JsonSerializer.Deserialize<BenchmarkDataset>(stream, HarnessJson.Options);
            }

            if (dataset == null || dataset.Corpora.Count == 0) throw new InvalidDataException("'" + path + "' has no corpora.");
            if (string.IsNullOrWhiteSpace(dataset.Name)) dataset.Name = Path.GetFileNameWithoutExtension(path);
            Validate(dataset, path);
            return dataset;
        }

        /// <summary>
        /// Write a dataset as indented JSON.
        /// </summary>
        /// <param name="dataset">The dataset.</param>
        /// <param name="path">Output path.</param>
        /// <exception cref="ArgumentNullException">Thrown when the dataset is null.</exception>
        public static void Save(BenchmarkDataset dataset, string path)
        {
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonSerializer.Serialize(dataset, HarnessJson.Indented));
        }

        /// <summary>
        /// True when a gold answer marks an unanswerable question.
        /// </summary>
        /// <param name="answer">Gold answer.</param>
        /// <returns>True for NOT_IN_CORPUS or NOT_IN_MEMORY.</returns>
        public static bool IsUnanswerableMarker(string? answer)
        {
            return string.Equals(answer, NotInCorpus, StringComparison.Ordinal) || string.Equals(answer, "NOT_IN_MEMORY", StringComparison.Ordinal);
        }

        #endregion

        #region Private-Methods

        private static void Validate(BenchmarkDataset dataset, string path)
        {
            HashSet<string> corpusIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (BenchmarkCorpus corpus in dataset.Corpora)
            {
                if (string.IsNullOrWhiteSpace(corpus.Id) || !corpusIds.Add(corpus.Id))
                    throw new InvalidDataException("'" + path + "': corpus ids must be unique and non-empty.");

                HashSet<string> documentIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (BenchmarkDocument document in corpus.Documents)
                {
                    if (string.IsNullOrWhiteSpace(document.Id) || !documentIds.Add(document.Id))
                        throw new InvalidDataException("'" + path + "': duplicate or empty document id '" + document.Id + "' in corpus '" + corpus.Id + "'.");
                }

                HashSet<string> queryIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (BenchmarkQuery query in corpus.Queries)
                {
                    if (string.IsNullOrWhiteSpace(query.Id) || !queryIds.Add(query.Id))
                        throw new InvalidDataException("'" + path + "': duplicate or empty query id '" + query.Id + "' in corpus '" + corpus.Id + "'.");
                    foreach (string relevant in query.Relevant)
                    {
                        if (!documentIds.Contains(relevant))
                            throw new InvalidDataException("'" + path + "': query '" + query.Id + "' names unknown document '" + relevant + "'.");
                    }
                }
            }
        }

        #endregion
    }
}
