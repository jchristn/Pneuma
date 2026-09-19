namespace Pneuma.Core.Ingestion.Stages
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// Merges the candidate subgraph into the tenant's knowledge graph: creates and links the Source node, does
    /// best-effort entity resolution on the candidate nodes, and materializes a Cell node per non-empty extracted
    /// cell (so retrieval hits can resolve back to a graph node). The resulting per-cell node ids are aligned
    /// one-to-one with the cells for the downstream summarization/chunking steps.
    /// </summary>
    public class GraphMergeStage : IStage
    {
        #region Private-Members

        private readonly StageDependencies _Deps;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the stage.</summary>
        /// <param name="deps">Shared stage dependencies.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="deps"/> is null.</exception>
        public GraphMergeStage(StageDependencies deps)
        {
            _Deps = deps ?? throw new ArgumentNullException(nameof(deps));
        }

        #endregion

        #region Public-Members

        /// <inheritdoc />
        public IngestionStageEnum Stage => IngestionStageEnum.GraphMerge;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task ExecuteAsync(StageContext context, CancellationToken token)
        {
            IngestionJob job = context.Job;
            CandidateSubgraph subgraph = context.Subgraph;
            List<ExtractedCell> cells = context.Cells;

            IGraphRepository graph = await _Deps.GraphFactory.ForTenantAsync(job.TenantId, token).ConfigureAwait(false);
            await graph.EnsureGraphAsync(token).ConfigureAwait(false);

            GraphNode source = new GraphNode
            {
                NodeType = Ontology.NodeSource,
                Name = job.SourceUrl,
                CanonicalName = job.SourceUrl,
                Labels = new List<string> { Ontology.NodeSource }
            };
            source.Tags[Ontology.TagTenantId] = job.TenantId;
            source.Tags[Ontology.TagSubjectId] = job.SubjectId;
            source.Tags[Ontology.TagNodeType] = Ontology.NodeSource;
            source.Tags[Ontology.TagAssertedByJob] = job.Id;
            // Operator-supplied labels/tags also ride on the source graph node for provenance (labels as graph
            // labels, tags as graph tags), guarding the ontology's own reserved tag keys.
            IngestionMetadata.ApplyUserGraphMetadata(source, job);
            GraphNode createdSource = await graph.CreateNodeAsync(source, token).ConfigureAwait(false);

            NodeMergeResult nodeMerge = await new SubgraphMerger(graph).MergeNodesAsync(subgraph, job.TenantId, job.SubjectId, createdSource.Id, job.Id, token).ConfigureAwait(false);
            MergeResult merge = new MergeResult
            {
                NodeIds = nodeMerge.NodeIds,
                RefToId = nodeMerge.RefToId,
                SourceNodeId = createdSource.Id
            };
            if (!merge.NodeIds.Contains(createdSource.Id)) merge.NodeIds.Insert(0, createdSource.Id);

            // Materialize each cell as a Cell node linked to the source. The list is aligned one-to-one with the
            // input cells (empty string for a cell with no text) so summarization/chunking can look a chunk's
            // originating cell node up by index.
            List<string> cellNodeIds = new List<string>(cells != null ? cells.Count : 0);
            if (cells != null)
            {
                foreach (ExtractedCell cell in cells)
                {
                    token.ThrowIfCancellationRequested();
                    if (String.IsNullOrWhiteSpace(cell.Text))
                    {
                        cellNodeIds.Add(String.Empty);
                        continue;
                    }
                    string cellNodeId = await CreateCellNodeAsync(job, graph, createdSource.Id, cell.Text, token).ConfigureAwait(false);
                    cellNodeIds.Add(cellNodeId);
                    if (!String.IsNullOrEmpty(cellNodeId)) merge.NodeIds.Add(cellNodeId);
                }
            }
            merge.CellNodeIds = cellNodeIds;

            context.Merge = merge;
            job.GraphNodeIds = merge.NodeIds;
            context.Message = "Knowledge-graph insertion complete — inserted/linked " + merge.NodeIds.Count + " node(s), including a cell node per extracted cell.";
        }

        #endregion

        #region Private-Methods

        private async Task<string> CreateCellNodeAsync(IngestionJob job, IGraphRepository graph, string sourceNodeId, string text, CancellationToken token)
        {
            try
            {
                // A Cell node is the graph's unit of source content: it holds the cell's extracted text and links
                // to its source. Its finer-grained chunks are not graph nodes — they live only in RecallDB and
                // point back here via litegraphNodeId.
                GraphNode cellNode = new GraphNode
                {
                    NodeType = Ontology.NodeCell,
                    Name = text.Length > 80 ? text.Substring(0, 80) : text,
                    Content = text,
                    Labels = new List<string> { Ontology.NodeCell }
                };
                cellNode.Tags[Ontology.TagTenantId] = job.TenantId;
                cellNode.Tags[Ontology.TagSubjectId] = job.SubjectId;
                cellNode.Tags[Ontology.TagNodeType] = Ontology.NodeCell;
                cellNode.Tags[Ontology.TagAssertedByJob] = job.Id;
                if (!String.IsNullOrEmpty(sourceNodeId)) cellNode.Tags[Ontology.TagSourceId] = sourceNodeId;

                GraphNode created = await graph.CreateNodeAsync(cellNode, token).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(created.Id) && !String.IsNullOrEmpty(sourceNodeId))
                {
                    GraphEdge edge = new GraphEdge
                    {
                        FromNodeId = sourceNodeId,
                        ToNodeId = created.Id,
                        EdgeType = Ontology.EdgeHasCell
                    };
                    edge.Tags[Ontology.TagAssertedByJob] = job.Id;
                    await graph.CreateEdgeAsync(edge, token).ConfigureAwait(false);
                }
                return created.Id;
            }
            catch (Exception exception)
            {
                _Deps.Logging.Warn("[GraphMergeStage] cell node creation failed: " + exception.Message);
                return String.Empty;
            }
        }

        #endregion
    }
}
