namespace Test.Benchmark.Datasets
{
    using System.Collections.Generic;

    /// <summary>
    /// A document to ingest. It is served by the harness's corpus server and submitted to Pneuma as a link.
    /// </summary>
    public class BenchmarkDocument
    {
        #region Public-Members

        /// <summary>
        /// Document id; matched against query relevance labels and used as the served file name.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Category name (applied as a link label).
        /// </summary>
        public string Category { get; set; } = "general";

        /// <summary>
        /// Optional title (submitted as the link title and prepended to md/txt documents).
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Optional one-line summary (prepended to md/txt documents under the title).
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// The document body, in <see cref="Format"/>.
        /// </summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>
        /// Optional ISO date. Dated corpora are submitted in date order.
        /// </summary>
        public string? Date { get; set; } = null;

        /// <summary>
        /// Serving format: md, html, or txt. Absent means md (the Isis datasets are markdown-style memories).
        /// </summary>
        public string? Format { get; set; } = null;

        /// <summary>
        /// Optional labels applied to the link (the category is always added).
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Optional key/value tags applied to the link.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
