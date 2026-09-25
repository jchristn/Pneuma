namespace Test.Benchmark.Datasets
{
    using System.Collections.Generic;

    /// <summary>
    /// A benchmark dataset in the neutral format shared with the Isis harness: one or more corpora, each holding
    /// documents and labelled queries. Isis datasets (for example atlas.json) load unchanged.
    /// </summary>
    public class BenchmarkDataset
    {
        #region Public-Members

        /// <summary>
        /// Dataset name (used in subject names and report file names).
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// How the dataset was built and what it tests.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// The corpora. Each corpus becomes its own Pneuma subject with its own collection.
        /// </summary>
        public List<BenchmarkCorpus> Corpora { get; set; } = new List<BenchmarkCorpus>();

        #endregion
    }
}
