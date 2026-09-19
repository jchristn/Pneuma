namespace Pneuma.Core.Ingestion.Stages
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Graph;

    /// <summary>
    /// Canonicalizes the candidate subgraph's node and edge types in place, coercing each to a built-in ontology
    /// type when recognized so casing/spelling variance does not fragment the ontology. Pure (no graph I/O).
    /// </summary>
    public class OntologyCanonicalizationStage : IStage
    {
        #region Private-Members

        private readonly StageDependencies _Deps;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the stage.</summary>
        /// <param name="deps">Shared stage dependencies.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="deps"/> is null.</exception>
        public OntologyCanonicalizationStage(StageDependencies deps)
        {
            _Deps = deps ?? throw new ArgumentNullException(nameof(deps));
        }

        #endregion

        #region Public-Members

        /// <inheritdoc />
        public IngestionStageEnum Stage => IngestionStageEnum.OntologyCanonicalization;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task ExecuteAsync(StageContext context, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            int normalized = SubgraphMerger.Canonicalize(context.Subgraph);
            context.Message = "Ontology canonicalization complete — normalized " + normalized + " node/edge type(s) to the canonical ontology.";
            return Task.CompletedTask;
        }

        #endregion
    }
}
