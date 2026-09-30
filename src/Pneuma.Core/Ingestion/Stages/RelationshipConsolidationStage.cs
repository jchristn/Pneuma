namespace Pneuma.Core.Ingestion.Stages
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Ontologies;

    /// <summary>
    /// Consolidates the candidate subgraph's relationships into the live graph: a re-asserted edge accumulates
    /// weight (noisy-OR) and corroboration count in place, otherwise a new edge is created. Uses the ref→node-id
    /// map and Source node id carried from the graph-merge step.
    /// </summary>
    public class RelationshipConsolidationStage : IStage
    {
        #region Private-Members

        private readonly StageDependencies _Deps;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the stage.</summary>
        /// <param name="deps">Shared stage dependencies.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="deps"/> is null.</exception>
        public RelationshipConsolidationStage(StageDependencies deps)
        {
            _Deps = deps ?? throw new ArgumentNullException(nameof(deps));
        }

        #endregion

        #region Public-Members

        /// <inheritdoc />
        public IngestionStageEnum Stage => IngestionStageEnum.RelationshipConsolidation;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task ExecuteAsync(StageContext context, CancellationToken token)
        {
            IngestionJob job = context.Job;
            MergeResult merge = context.Merge;

            IGraphRepository graph = await _Deps.GraphFactory.ForTenantAsync(job.TenantId, token).ConfigureAwait(false);
            EdgeConsolidationResult result = await new SubgraphMerger(graph).ConsolidateEdgesAsync(context.Subgraph, merge.RefToId, merge.SourceNodeId, job.Id, token).ConfigureAwait(false);

            int taxonomyLinks = await LinkTaxonomyAsync(context, graph, token).ConfigureAwait(false);
            context.Message = "Relationship consolidation complete — created " + result.CreatedCount + " new relationship(s), consolidated " + result.ConsolidatedCount + " re-asserted one(s)" +
                (taxonomyLinks > 0 ? ", linked " + taxonomyLinks + " cell(s) to taxonomy concepts." : ".");
        }

        #endregion

        #region Private-Methods

        /// <summary>Link each cell to the taxonomy concepts matched in it during classification.</summary>
        private static async Task<int> LinkTaxonomyAsync(StageContext context, IGraphRepository graph, CancellationToken token)
        {
            OntologyVersion? version = context.Classification?.Version;
            if (version == null || context.TaxonomyMatches.Count == 0) return 0;
            List<string> cellNodeIds = context.Merge.CellNodeIds ?? new List<string>();
            TaxonomyLinker linker = new TaxonomyLinker(graph, version, context.Job.TenantId, context.Job.SubjectId);
            int added = 0;
            foreach (KeyValuePair<int, List<string>> pair in context.TaxonomyMatches)
            {
                if (pair.Key < 0 || pair.Key >= cellNodeIds.Count || String.IsNullOrEmpty(cellNodeIds[pair.Key])) continue;
                TaxonomyLinkResult linked = await linker.LinkCellAsync(cellNodeIds[pair.Key], pair.Value, context.Job.Id, token).ConfigureAwait(false);
                added += linked.Added;
            }
            PneumaMetrics.RecordTaxonomyLinks("added", added);
            return added;
        }

        #endregion
    }
}
