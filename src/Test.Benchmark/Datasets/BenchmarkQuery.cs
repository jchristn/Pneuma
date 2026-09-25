namespace Test.Benchmark.Datasets
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A labelled query. An empty <see cref="Relevant"/> list marks a question the corpus cannot answer.
    /// </summary>
    public class BenchmarkQuery
    {
        #region Public-Members

        /// <summary>
        /// Query id, unique within the corpus.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The query text.
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Query type used to break results down (paraphrase, lexical, multi, multihop, negative, ...).
        /// </summary>
        public string Type { get; set; } = "default";

        /// <summary>
        /// Ids of the relevant documents, most important first.
        /// </summary>
        public List<string> Relevant { get; set; } = new List<string>();

        /// <summary>
        /// Optional graded relevance (document id to gain). Documents in <see cref="Relevant"/> but absent here have gain 1.
        /// </summary>
        public Dictionary<string, int>? Grades { get; set; } = null;

        /// <summary>
        /// Optional category (Isis format). Applied as a required label filter.
        /// </summary>
        public string? Category { get; set; } = null;

        /// <summary>
        /// Optional explicit metadata filter, sent to Pneuma as the search/query filter.
        /// </summary>
        public QueryFilter? Filter { get; set; } = null;

        /// <summary>
        /// Optional gold answer. "NOT_IN_CORPUS" (or the Isis "NOT_IN_MEMORY") marks an unanswerable question.
        /// </summary>
        public string? Answer { get; set; } = null;

        /// <summary>
        /// Optional verbatim evidence spans from the relevant documents, for passage-level scoring.
        /// </summary>
        public List<string>? Evidence { get; set; } = null;

        /// <summary>
        /// Optional number of documents the answer needs.
        /// </summary>
        public int? Hops { get; set; } = null;

        /// <summary>
        /// Optional date the question is asked on.
        /// </summary>
        public string? Date { get; set; } = null;

        /// <summary>
        /// True when the corpus contains the answer.
        /// </summary>
        [JsonIgnore]
        public bool Answerable
        {
            get
            {
                return Relevant != null && Relevant.Count > 0;
            }
        }

        #endregion
    }
}
