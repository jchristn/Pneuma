namespace Test.Benchmark.Runners
{
    using System;
    using System.Collections.Generic;
    using Test.Benchmark.Client;
    using Test.Benchmark.Datasets;

    /// <summary>
    /// A corpus provisioned as a Pneuma subject, with the maps that turn Pneuma's ids back into document ids.
    /// </summary>
    public class ProvisionedSubject
    {
        #region Public-Members

        /// <summary>
        /// The corpus.
        /// </summary>
        public BenchmarkCorpus Corpus { get; set; } = new BenchmarkCorpus();

        /// <summary>
        /// Subject id.
        /// </summary>
        public string SubjectId { get; set; } = string.Empty;

        /// <summary>
        /// Deterministic subject name.
        /// </summary>
        public string SubjectName { get; set; } = string.Empty;

        /// <summary>
        /// Collection id.
        /// </summary>
        public string? CollectionId { get; set; } = null;

        /// <summary>
        /// Link id to document id.
        /// </summary>
        public Dictionary<string, string> DocIdByLinkId { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Document id to link id.
        /// </summary>
        public Dictionary<string, string> LinkIdByDocId { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Ingestion job id to document id (graph nodes carry the asserting job in their assertedByJob tag).
        /// </summary>
        public Dictionary<string, string> DocIdByJobId { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// True when the subject was reused from an earlier run.
        /// </summary>
        public bool Reused { get; set; } = false;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve a document id from a link id or a link URL.
        /// </summary>
        /// <param name="linkId">Link id.</param>
        /// <param name="url">Link URL.</param>
        /// <returns>The document id, or null.</returns>
        public string? ResolveDocument(string? linkId, string? url)
        {
            if (!string.IsNullOrEmpty(linkId) && DocIdByLinkId.TryGetValue(linkId!, out string? byLink)) return byLink;
            return Servers.CorpusServer.DocumentIdFromUrl(url);
        }

        /// <summary>
        /// Resolve the document a grounded-answer source came from (link id when returned, else the asserting job).
        /// </summary>
        /// <param name="node">Source node.</param>
        /// <returns>The document id, or null.</returns>
        public string? ResolveDocument(GraphNodeInfo node)
        {
            if (node == null) return null;
            if (!string.IsNullOrEmpty(node.LinkId) && DocIdByLinkId.TryGetValue(node.LinkId!, out string? byLink)) return byLink;
            if (node.Tags != null)
            {
                if (node.Tags.TryGetValue("linkId", out string? tagLink) && DocIdByLinkId.TryGetValue(tagLink, out string? byTagLink)) return byTagLink;
                if (node.Tags.TryGetValue("assertedByJob", out string? job) && DocIdByJobId.TryGetValue(job, out string? byJob)) return byJob;
            }

            if (!string.IsNullOrEmpty(node.CanonicalName) && DocIdByLinkId.TryGetValue(node.CanonicalName!, out string? byCanonical)) return byCanonical;
            return null;
        }

        #endregion
    }
}
