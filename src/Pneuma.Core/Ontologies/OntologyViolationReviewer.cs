namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Integrations.Abstractions;

    /// <summary>
    /// Reviews quarantined elements. Releasing a node merges it into the subject's graph (reusing a node with the same
    /// type and canonical name); releasing an edge links its two endpoint nodes, which must already be in the graph.
    /// Released elements are tagged with the job that proposed them, so re-ingesting or deleting its link removes them
    /// like any other element of that version. Dismissing just closes the review.
    /// </summary>
    public class OntologyViolationReviewer
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly IGraphRepositoryFactory _GraphFactory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the reviewer.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="graphFactory">Per-tenant graph factory.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public OntologyViolationReviewer(DatabaseDriverBase db, IGraphRepositoryFactory graphFactory)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _GraphFactory = graphFactory ?? throw new ArgumentNullException(nameof(graphFactory));
        }

        #endregion

        #region Public-Methods

        /// <summary>Release a quarantined element into the graph.</summary>
        /// <param name="violation">The violation (must be quarantined).</param>
        /// <param name="userId">Reviewing user, if known.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated violation, or 409 when it is not quarantined or an edge's endpoint is missing.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="violation"/> is null.</exception>
        public async Task<OntologyResult<OntologyViolation>> ReleaseAsync(OntologyViolation violation, string? userId, CancellationToken token = default)
        {
            if (violation == null) throw new ArgumentNullException(nameof(violation));
            if (violation.Status != OntologyViolationStatusEnum.Quarantined) return OntologyResult<OntologyViolation>.Fail(409, "Only a quarantined element can be released; this one is " + violation.Status + ".");

            IGraphRepository graph = await _GraphFactory.ForTenantAsync(violation.TenantId, token).ConfigureAwait(false);
            string jobId = violation.JobId ?? ("released-" + violation.Id);
            if (violation.ElementKind == OntologyElementKindEnum.Node)
            {
                if (String.IsNullOrWhiteSpace(violation.NodeType) || String.IsNullOrWhiteSpace(violation.NodeName))
                    return OntologyResult<OntologyViolation>.Fail(409, "The quarantined node has no type or name to release.");
                CandidateSubgraph subgraph = new CandidateSubgraph();
                subgraph.Nodes.Add(new CandidateNode { Ref = "n1", NodeType = violation.NodeType!, Name = violation.NodeName!, Content = violation.Content, Confidence = violation.Confidence });
                await new SubgraphMerger(graph).MergeNodesAsync(subgraph, violation.TenantId, violation.SubjectId, await SourceNodeIdAsync(graph, violation.JobId, token).ConfigureAwait(false), jobId, token).ConfigureAwait(false);
            }
            else
            {
                GraphNode? from = await FindAsync(graph, violation.FromNodeType, violation.FromNodeName, violation.SubjectId, token).ConfigureAwait(false);
                GraphNode? to = await FindAsync(graph, violation.ToNodeType, violation.ToNodeName, violation.SubjectId, token).ConfigureAwait(false);
                if (from == null || to == null)
                    return OntologyResult<OntologyViolation>.Fail(409, "An endpoint of this relationship is not in the graph. Release its quarantined node first.");
                GraphEdge edge = new GraphEdge
                {
                    EdgeType = violation.EdgeType ?? Ontology.EdgeMentions,
                    FromNodeId = from.Id,
                    ToNodeId = to.Id,
                    Tags = new Dictionary<string, string>
                    {
                        { Ontology.TagConfidence, violation.Confidence.ToString("F3", CultureInfo.InvariantCulture) },
                        { Ontology.TagWeight, violation.Confidence.ToString("F4", CultureInfo.InvariantCulture) },
                        { Ontology.TagCorroborationCount, "1" },
                        { Ontology.TagAssertedByJob, jobId }
                    }
                };
                await graph.CreateEdgeAsync(edge, token).ConfigureAwait(false);
            }

            violation.Status = OntologyViolationStatusEnum.Released;
            violation.ResolvedByUserId = userId;
            violation.ResolvedUtc = DateTime.UtcNow;
            await _Db.OntologyViolations.UpdateStatusAsync(violation, token).ConfigureAwait(false);
            return OntologyResult<OntologyViolation>.Ok(violation);
        }

        /// <summary>Dismiss a quarantined element (it stays out of the graph).</summary>
        /// <param name="violation">The violation (must be quarantined).</param>
        /// <param name="userId">Reviewing user, if known.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated violation, or 409 when it is not quarantined.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="violation"/> is null.</exception>
        public async Task<OntologyResult<OntologyViolation>> DismissAsync(OntologyViolation violation, string? userId, CancellationToken token = default)
        {
            if (violation == null) throw new ArgumentNullException(nameof(violation));
            if (violation.Status != OntologyViolationStatusEnum.Quarantined) return OntologyResult<OntologyViolation>.Fail(409, "Only a quarantined element can be dismissed; this one is " + violation.Status + ".");
            violation.Status = OntologyViolationStatusEnum.Dismissed;
            violation.ResolvedByUserId = userId;
            violation.ResolvedUtc = DateTime.UtcNow;
            await _Db.OntologyViolations.UpdateStatusAsync(violation, token).ConfigureAwait(false);
            return OntologyResult<OntologyViolation>.Ok(violation);
        }

        #endregion

        #region Private-Methods

        private static async Task<GraphNode?> FindAsync(IGraphRepository graph, string? type, string? name, string subjectId, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(type) || String.IsNullOrWhiteSpace(name)) return null;
            GraphNode? node = await graph.FindNodeByCanonicalAsync(type!, name!, subjectId, token).ConfigureAwait(false);
            return node != null && !String.IsNullOrEmpty(node.Id) ? node : null;
        }

        private static async Task<string?> SourceNodeIdAsync(IGraphRepository graph, string? jobId, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(jobId)) return null;
            Dictionary<string, string> tags = new Dictionary<string, string>
            {
                { Ontology.TagAssertedByJob, jobId! },
                { Ontology.TagNodeType, Ontology.NodeSource }
            };
            List<GraphNode> sources = await graph.SearchNodesByTagsAsync(tags, 1, token).ConfigureAwait(false);
            return sources.Count > 0 ? sources[0].Id : null;
        }

        #endregion
    }
}
