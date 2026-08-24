namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;

    /// <summary>
    /// Stamps a job's operator-supplied labels and tags onto its ingested chunks (RecallDB tags) and its source
    /// graph node (LiteGraph labels/tags), guarding the reserved provenance and ontology keys. Factored out of
    /// <see cref="IngestionStages"/> so that pipeline class stays focused on stage orchestration.
    /// </summary>
    public static class IngestionMetadata
    {
        #region Public-Methods

        /// <summary>
        /// Stamp a chunk's tag map with the job's operator-supplied labels and tags. Each label becomes its own
        /// filterable <c>label:{value}</c> tag (so a chunk may carry several labels at once); each tag is applied
        /// verbatim. Reserved provenance keys (<c>litegraphNodeId</c>, ids, urls, <c>documentType</c>, and any
        /// <c>label:</c> key) are never overwritten by a user tag.
        /// </summary>
        /// <param name="tags">The chunk tag map to extend in place.</param>
        /// <param name="job">The job carrying the operator labels/tags.</param>
        public static void ApplyUserMetadata(Dictionary<string, string> tags, IngestionJob job)
        {
            if (job.Labels != null)
            {
                foreach (string label in job.Labels)
                {
                    string key = RetrievalFilter.LabelTagKeyFor(label);
                    if (!String.IsNullOrEmpty(key)) tags[key] = label.Trim();
                }
            }
            if (job.Tags != null)
            {
                foreach (KeyValuePair<string, string> tag in job.Tags)
                {
                    if (String.IsNullOrWhiteSpace(tag.Key)) continue;
                    string key = tag.Key.Trim();
                    if (IsReservedChunkTagKey(key)) continue;
                    tags[key] = tag.Value ?? String.Empty;
                }
            }
        }

        /// <summary>
        /// Add the job's operator labels (as graph labels) and tags (as graph tags) to a node, guarding the
        /// ontology's own reserved tag keys so provenance/structure tags are never overwritten.
        /// </summary>
        /// <param name="node">The graph node to extend in place.</param>
        /// <param name="job">The job carrying the operator labels/tags.</param>
        public static void ApplyUserGraphMetadata(GraphNode node, IngestionJob job)
        {
            if (job.Labels != null)
            {
                foreach (string label in job.Labels)
                {
                    if (String.IsNullOrWhiteSpace(label)) continue;
                    string trimmed = label.Trim();
                    if (!node.Labels.Contains(trimmed)) node.Labels.Add(trimmed);
                }
            }
            if (job.Tags != null)
            {
                foreach (KeyValuePair<string, string> tag in job.Tags)
                {
                    if (String.IsNullOrWhiteSpace(tag.Key)) continue;
                    string key = tag.Key.Trim();
                    if (IsReservedGraphTagKey(key)) continue;
                    node.Tags[key] = tag.Value ?? String.Empty;
                }
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>Whether a chunk tag key is reserved for provenance and must not be set by an operator tag.</summary>
        /// <param name="key">The candidate tag key.</param>
        /// <returns>True when the key is reserved.</returns>
        private static bool IsReservedChunkTagKey(string key)
        {
            switch (key)
            {
                case "litegraphNodeId":
                case "linkId":
                case "tenantId":
                case "subjectId":
                case "jobId":
                case "sourceUrl":
                case "documentType":
                    return true;
                default:
                    return key.StartsWith(RetrievalFilter.LabelTagPrefix, StringComparison.Ordinal);
            }
        }

        /// <summary>Whether a graph tag key is reserved by the ontology and must not be set by an operator tag.</summary>
        /// <param name="key">The candidate tag key.</param>
        /// <returns>True when the key is reserved.</returns>
        private static bool IsReservedGraphTagKey(string key)
        {
            switch (key)
            {
                case Ontology.TagTenantId:
                case Ontology.TagSubjectId:
                case Ontology.TagNodeType:
                case Ontology.TagCanonicalName:
                case Ontology.TagSourceId:
                case Ontology.TagRights:
                case Ontology.TagAuthority:
                case Ontology.TagConfidence:
                case Ontology.TagAssertedByJob:
                    return true;
                default:
                    return false;
            }
        }

        #endregion
    }
}
