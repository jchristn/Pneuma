namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Integrations.Abstractions;

    /// <summary>
    /// Links cells to taxonomy concepts in the graph. Each concept gets one node per subject (found by its preferred
    /// label and node type, so a node the classifier already created for the same name is reused), linked to its broader
    /// concept with a BROADER edge. A cell that mentions a concept gets an ABOUT edge to it. Every element it creates is
    /// tagged <c>assertedBy=taxonomy</c> with the concept key, so retagging can find and replace its own links without
    /// touching the classifier's. Concept nodes carry no job tag: they belong to the subject and outlive any one link.
    /// </summary>
    public class TaxonomyLinker
    {
        #region Private-Members

        private readonly IGraphRepository _Graph;
        private readonly Dictionary<string, OntologyConcept> _Concepts = new Dictionary<string, OntologyConcept>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _NodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly string _TenantId;
        private readonly string _SubjectId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate a linker for one subject.</summary>
        /// <param name="graph">The tenant's graph.</param>
        /// <param name="version">The pinned ontology version (with its concepts).</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="graph"/> or <paramref name="version"/> is null.</exception>
        public TaxonomyLinker(IGraphRepository graph, OntologyVersion version, string tenantId, string subjectId)
        {
            _Graph = graph ?? throw new ArgumentNullException(nameof(graph));
            if (version == null) throw new ArgumentNullException(nameof(version));
            foreach (OntologyConcept concept in version.Concepts) _Concepts[concept.Key] = concept;
            _TenantId = tenantId;
            _SubjectId = subjectId;
        }

        #endregion

        #region Public-Methods

        /// <summary>Link a new cell to the concepts it mentions.</summary>
        /// <param name="cellNodeId">The cell's graph node.</param>
        /// <param name="conceptKeys">Keys of the concepts matched in the cell.</param>
        /// <param name="jobId">The job that asserted the cell (tagged on the links so they go with the cell's version).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What was added.</returns>
        public async Task<TaxonomyLinkResult> LinkCellAsync(string cellNodeId, IEnumerable<string> conceptKeys, string? jobId, CancellationToken token = default)
        {
            TaxonomyLinkResult result = new TaxonomyLinkResult();
            foreach (string key in (conceptKeys ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal))
            {
                string? conceptNodeId = await ConceptNodeAsync(key, result, token).ConfigureAwait(false);
                if (conceptNodeId == null) continue;
                await _Graph.CreateEdgeAsync(AboutEdge(cellNodeId, conceptNodeId, key, jobId), token).ConfigureAwait(false);
                result.Added++;
            }
            return result;
        }

        /// <summary>
        /// Make a stored cell's taxonomy links match a set of concepts: remove links to concepts it no longer mentions (or
        /// that the version no longer has) and add the missing ones. Links the classifier made are left alone.
        /// </summary>
        /// <param name="cell">The cell node.</param>
        /// <param name="conceptKeys">Keys of the concepts the cell mentions now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What was added and removed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="cell"/> is null.</exception>
        public async Task<TaxonomyLinkResult> RetagCellAsync(GraphNode cell, IEnumerable<string> conceptKeys, CancellationToken token = default)
        {
            if (cell == null) throw new ArgumentNullException(nameof(cell));
            TaxonomyLinkResult result = new TaxonomyLinkResult();
            HashSet<string> wanted = new HashSet<string>(conceptKeys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

            List<GraphEdge> edges = await _Graph.GetEdgesAsync(cell.Id, token).ConfigureAwait(false);
            HashSet<string> present = new HashSet<string>(StringComparer.Ordinal);
            foreach (GraphEdge edge in edges)
            {
                if (!IsTaxonomyAbout(edge, cell.Id)) continue;
                string key = Tag(edge.Tags, Ontology.TagTaxonomyConcept) ?? String.Empty;
                if (wanted.Contains(key) && present.Add(key)) continue;
                await DeleteEdgeAsync(edge, token).ConfigureAwait(false);
                result.Removed++;
            }

            string? jobId = Tag(cell.Tags, Ontology.TagAssertedByJob);
            foreach (string key in wanted)
            {
                if (present.Contains(key)) continue;
                string? conceptNodeId = await ConceptNodeAsync(key, result, token).ConfigureAwait(false);
                if (conceptNodeId == null) continue;
                await _Graph.CreateEdgeAsync(AboutEdge(cell.Id, conceptNodeId, key, jobId), token).ConfigureAwait(false);
                result.Added++;
            }
            return result;
        }

        #endregion

        #region Private-Methods

        private async Task<string?> ConceptNodeAsync(string key, TaxonomyLinkResult result, CancellationToken token)
        {
            string? cached;
            if (_NodeIds.TryGetValue(key, out cached)) return cached;
            OntologyConcept? concept;
            if (!_Concepts.TryGetValue(key, out concept)) return null;

            GraphNode? existing = await _Graph.FindNodeByCanonicalAsync(concept.NodeType, concept.PrefLabel, _SubjectId, token).ConfigureAwait(false);
            string nodeId;
            bool created = false;
            if (existing != null && !String.IsNullOrEmpty(existing.Id))
            {
                nodeId = existing.Id;
            }
            else
            {
                GraphNode node = new GraphNode
                {
                    NodeType = concept.NodeType,
                    Name = concept.PrefLabel,
                    CanonicalName = concept.PrefLabel,
                    Content = concept.Definition,
                    Labels = new List<string> { concept.NodeType }
                };
                node.Tags[Ontology.TagTenantId] = _TenantId;
                node.Tags[Ontology.TagSubjectId] = _SubjectId;
                node.Tags[Ontology.TagNodeType] = concept.NodeType;
                node.Tags[Ontology.TagCanonicalName] = concept.PrefLabel;
                node.Tags[Ontology.TagConfidence] = "1.000";
                node.Tags[Ontology.TagAssertedBy] = Ontology.AssertedByTaxonomy;
                node.Tags[Ontology.TagTaxonomyConcept] = concept.Key;
                GraphNode stored = await _Graph.CreateNodeAsync(node, token).ConfigureAwait(false);
                nodeId = stored.Id;
                created = true;
                result.ConceptNodesCreated++;
            }
            _NodeIds[key] = nodeId;

            // Link a newly created concept to its broader concept (created on demand). The cache entry above stops a cycle.
            if (created && !String.IsNullOrWhiteSpace(concept.BroaderKey))
            {
                string? parentId = await ConceptNodeAsync(concept.BroaderKey!, result, token).ConfigureAwait(false);
                if (parentId != null && parentId != nodeId)
                {
                    GraphEdge broader = new GraphEdge { EdgeType = Ontology.EdgeBroader, FromNodeId = nodeId, ToNodeId = parentId };
                    broader.Tags[Ontology.TagAssertedBy] = Ontology.AssertedByTaxonomy;
                    broader.Tags[Ontology.TagTaxonomyConcept] = concept.Key;
                    await _Graph.CreateEdgeAsync(broader, token).ConfigureAwait(false);
                }
            }
            return nodeId;
        }

        private static GraphEdge AboutEdge(string cellNodeId, string conceptNodeId, string key, string? jobId)
        {
            GraphEdge edge = new GraphEdge { EdgeType = Ontology.EdgeAbout, FromNodeId = cellNodeId, ToNodeId = conceptNodeId };
            edge.Tags[Ontology.TagConfidence] = "1.000";
            edge.Tags[Ontology.TagWeight] = "1.0000";
            edge.Tags[Ontology.TagCorroborationCount] = "1";
            edge.Tags[Ontology.TagAssertedBy] = Ontology.AssertedByTaxonomy;
            edge.Tags[Ontology.TagTaxonomyConcept] = key;
            if (!String.IsNullOrWhiteSpace(jobId)) edge.Tags[Ontology.TagAssertedByJob] = jobId!;
            return edge;
        }

        private static bool IsTaxonomyAbout(GraphEdge edge, string cellNodeId)
        {
            return String.Equals(edge.EdgeType, Ontology.EdgeAbout, StringComparison.Ordinal)
                && String.Equals(edge.FromNodeId, cellNodeId, StringComparison.Ordinal)
                && String.Equals(Tag(edge.Tags, Ontology.TagAssertedBy), Ontology.AssertedByTaxonomy, StringComparison.Ordinal);
        }

        private async Task DeleteEdgeAsync(GraphEdge edge, CancellationToken token)
        {
            await _Graph.DeleteEdgeAsync(edge.Id, token).ConfigureAwait(false);
        }

        private static string? Tag(Dictionary<string, string>? tags, string key)
        {
            string? value;
            return tags != null && tags.TryGetValue(key, out value) ? value : null;
        }

        #endregion
    }
}
