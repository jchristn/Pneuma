namespace Pneuma.Core.Ingestion.Stages
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations.Abstractions;

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

            context.Message = "Relationship consolidation complete — created " + result.CreatedCount + " new relationship(s), consolidated " + result.ConsolidatedCount + " re-asserted one(s).";
        }

        #endregion
    }
}
