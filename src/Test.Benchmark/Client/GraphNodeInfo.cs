namespace Test.Benchmark.Client
{
    using System.Collections.Generic;

    /// <summary>
    /// A graph node as returned in grounded-answer sources.
    /// </summary>
    public class GraphNodeInfo
    {
        #region Public-Members

        /// <summary>
        /// Node id.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Node type (Cell, Source, Person, ...).
        /// </summary>
        public string? NodeType { get; set; } = null;

        /// <summary>
        /// Name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Canonical name.
        /// </summary>
        public string? CanonicalName { get; set; } = null;

        /// <summary>
        /// Content (the passage text for chunk-derived sources).
        /// </summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// Tags (provenance: assertedByJob, sourceId, subjectId, ...).
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>
        /// Originating link id, on builds that return it.
        /// </summary>
        public string? LinkId { get; set; } = null;

        #endregion
    }
}
