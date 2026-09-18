namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Models;
    using Pneuma.Server.Settings;
    using SyslogLogging;

    /// <summary>
    /// Builds GraphRAG-style community summaries for a subject: runs community detection over the tenant's
    /// knowledge graph (LiteGraph's Louvain), groups the subject's entity nodes by community, asks the
    /// subject's model to summarize the theme of each community, and persists each summary as a
    /// <c>CommunitySummary</c> graph node (co-located with the graph, cascade-cleaned with the subject).
    /// Rebuilding replaces the prior summaries. These summaries power the global / thematic query mode
    /// (<see cref="GroundedQueryService.AnswerGlobalAsync"/>).
    /// </summary>
    public class CommunityService
    {
        #region Private-Members

        // Node types whose clustering is meaningful for a thematic summary — the ontology's semantic entities.
        // Structural/content nodes (Source, Cell, Chunk) and the summaries themselves are excluded.
        private static readonly HashSet<string> _EntityTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            Ontology.NodeSubject, Ontology.NodePerson, Ontology.NodeOrganization, Ontology.NodeWork,
            Ontology.NodeCollection, Ontology.NodeEvent, Ontology.NodePlace, Ontology.NodeTopic, Ontology.NodeMedia
        };

        private readonly DatabaseDriverBase _Db;
        private readonly IGraphRepositoryFactory _GraphFactory;
        private readonly GroundedQueryService _Query;
        private readonly RetrievalSettings _Retrieval;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the community service.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="graphFactory">Per-tenant graph repository factory.</param>
        /// <param name="query">Grounded query service (used to run the summarization completion).</param>
        /// <param name="retrieval">Retrieval settings (community sizing / iteration knobs).</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public CommunityService(DatabaseDriverBase db, IGraphRepositoryFactory graphFactory, GroundedQueryService query, RetrievalSettings retrieval, LoggingModule logging)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _GraphFactory = graphFactory ?? throw new ArgumentNullException(nameof(graphFactory));
            _Query = query ?? throw new ArgumentNullException(nameof(query));
            _Retrieval = retrieval ?? throw new ArgumentNullException(nameof(retrieval));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// (Re)build the community summaries for a subject. Detects communities over the tenant graph, groups
        /// the subject's entity nodes, summarizes each sufficiently-large community, replaces any prior
        /// summaries, and returns the number of summaries created.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of community summaries created.</returns>
        public async Task<int> BuildAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            Subject? subject = await _Db.Subjects.ReadAsync(tenantId, subjectId, token).ConfigureAwait(false);
            if (subject == null) return 0;

            IGraphRepository graph = await _GraphFactory.ForTenantAsync(tenantId, token).ConfigureAwait(false);

            // Subject entity nodes, keyed by id (community detection reports names but we key on the subject's set).
            Dictionary<string, GraphNode> entityById = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
            List<GraphNode> subjectNodes = await graph.SearchNodesByTagsAsync(
                new Dictionary<string, string> { { Ontology.TagSubjectId, subjectId } }, 100000, token).ConfigureAwait(false);
            foreach (GraphNode node in subjectNodes)
            {
                if (!String.IsNullOrEmpty(node.Id) && _EntityTypes.Contains(node.NodeType)) entityById[node.Id] = node;
            }

            // Detect communities over the whole tenant graph, then restrict to this subject's entity nodes.
            CommunityDetectionResult detection = await graph.DetectCommunitiesAsync(false, _Retrieval.CommunityDetectionMaxIterations, token).ConfigureAwait(false);
            Dictionary<long, List<GraphNode>> byCommunity = new Dictionary<long, List<GraphNode>>();
            foreach (NodeCommunity assignment in detection.Nodes)
            {
                if (!entityById.TryGetValue(assignment.NodeId, out GraphNode? node)) continue;
                if (!byCommunity.TryGetValue(assignment.Community, out List<GraphNode>? members))
                {
                    members = new List<GraphNode>();
                    byCommunity[assignment.Community] = members;
                }
                members.Add(node);
            }

            // Replace any prior summaries for this subject (community ids are not stable across runs).
            await DeleteExistingSummariesAsync(graph, subjectId, token).ConfigureAwait(false);

            int created = 0;
            foreach (KeyValuePair<long, List<GraphNode>> entry in byCommunity)
            {
                token.ThrowIfCancellationRequested();
                if (entry.Value.Count < _Retrieval.CommunityMinSize) continue;

                string summary = await SummarizeAsync(tenantId, subject, entry.Value, token).ConfigureAwait(false);
                await CreateSummaryNodeAsync(graph, tenantId, subjectId, entry.Key, entry.Value, summary, token).ConfigureAwait(false);
                created++;
            }

            _Logging.Info("[CommunityService] built " + created + " community summary(ies) for subject " + subjectId + " (" + detection.CommunityCount + " communities detected over the tenant graph).");
            return created;
        }

        /// <summary>List the community summaries currently stored for a subject.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The community-summary nodes.</returns>
        public async Task<List<GraphNode>> ListSummariesAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            IGraphRepository graph = await _GraphFactory.ForTenantAsync(tenantId, token).ConfigureAwait(false);
            return await graph.SearchNodesByTagsAsync(
                new Dictionary<string, string> { { Ontology.TagSubjectId, subjectId }, { Ontology.TagNodeType, Ontology.NodeCommunitySummary } },
                1000, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task DeleteExistingSummariesAsync(IGraphRepository graph, string subjectId, CancellationToken token)
        {
            List<GraphNode> existing = await graph.SearchNodesByTagsAsync(
                new Dictionary<string, string> { { Ontology.TagSubjectId, subjectId }, { Ontology.TagNodeType, Ontology.NodeCommunitySummary } },
                100000, token).ConfigureAwait(false);
            foreach (GraphNode node in existing)
            {
                if (!String.IsNullOrEmpty(node.Id)) await graph.DeleteNodeAsync(node.Id, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Summarize a community's theme via the subject's model. Falls back to a deterministic member listing
        /// when no answering model is configured (or the call fails), so a summary node is always produced.
        /// </summary>
        private async Task<string> SummarizeAsync(string tenantId, Subject subject, List<GraphNode> members, CancellationToken token)
        {
            StringBuilder memberList = new StringBuilder();
            int limit = Math.Min(members.Count, _Retrieval.CommunitySummaryMaxMembers);
            for (int i = 0; i < limit; i++)
            {
                GraphNode node = members[i];
                memberList.Append("- ").Append(node.Name);
                if (!String.IsNullOrEmpty(node.NodeType)) memberList.Append(" (").Append(node.NodeType).Append(')');
                memberList.Append('\n');
            }

            ResolvedPrompt resolvedCommunity = await new PromptResolver(_Db).ResolveAsync(tenantId, subject.Id, "community.summarize", null, token).ConfigureAwait(false);
            string systemPrompt = String.IsNullOrWhiteSpace(resolvedCommunity.EffectiveContent)
                ? "You are summarizing a community of related entities from a knowledge graph. In 2-4 sentences, "
                    + "describe the theme that connects them and what they collectively concern. Do not list the entities verbatim; "
                    + "synthesize the shared topic. Do not mention that this is a graph or a community."
                : resolvedCommunity.EffectiveContent;
            string userText = "Related entities:\n" + memberList.ToString();

            string? summary = null;
            try
            {
                summary = await _Query.CompleteWithSubjectAsync(tenantId, subject, systemPrompt, userText, 256, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                _Logging.Warn("[CommunityService] community summarization failed: " + e.Message);
            }

            if (!String.IsNullOrWhiteSpace(summary)) return summary!.Trim();
            return "Related entities: " + BuildLabel(members) + ".";
        }

        private async Task CreateSummaryNodeAsync(IGraphRepository graph, string tenantId, string subjectId, long community, List<GraphNode> members, string summary, CancellationToken token)
        {
            string label = BuildLabel(members);
            GraphNode node = new GraphNode
            {
                NodeType = Ontology.NodeCommunitySummary,
                Name = label.Length > 80 ? label.Substring(0, 80) : label,
                Content = summary,
                Labels = new List<string> { Ontology.NodeCommunitySummary }
            };
            node.Tags[Ontology.TagTenantId] = tenantId;
            node.Tags[Ontology.TagSubjectId] = subjectId;
            node.Tags[Ontology.TagNodeType] = Ontology.NodeCommunitySummary;
            node.Tags[Ontology.TagCommunityId] = community.ToString(CultureInfo.InvariantCulture);
            node.Tags[Ontology.TagMemberCount] = members.Count.ToString(CultureInfo.InvariantCulture);
            await graph.CreateNodeAsync(node, token).ConfigureAwait(false);
        }

        /// <summary>Build a short comma-separated label from the first few member names.</summary>
        private static string BuildLabel(List<GraphNode> members)
        {
            StringBuilder builder = new StringBuilder();
            int limit = Math.Min(members.Count, 5);
            for (int i = 0; i < limit; i++)
            {
                if (builder.Length > 0) builder.Append(", ");
                builder.Append(members[i].Name);
            }
            if (members.Count > limit) builder.Append(", …");
            return builder.ToString();
        }

        #endregion
    }
}
