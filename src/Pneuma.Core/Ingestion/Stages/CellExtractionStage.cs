namespace Pneuma.Core.Ingestion.Stages
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Serialization;

    /// <summary>
    /// Parses the source into semantic cells via DocumentAtom, writes the raw source to the blob store, and
    /// persists the extracted cells as the atoms artifact. A document that yields no cells is a deterministic
    /// hard failure and throws <see cref="IngestionHardFailException"/>.
    /// </summary>
    public class CellExtractionStage : IStage
    {
        #region Private-Members

        private readonly StageDependencies _Deps;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the stage.</summary>
        /// <param name="deps">Shared stage dependencies.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="deps"/> is null.</exception>
        public CellExtractionStage(StageDependencies deps)
        {
            _Deps = deps ?? throw new ArgumentNullException(nameof(deps));
        }

        #endregion

        #region Public-Members

        /// <inheritdoc />
        public IngestionStageEnum Stage => IngestionStageEnum.CellExtraction;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        /// <exception cref="IngestionHardFailException">Thrown when no semantic cells are extracted.</exception>
        public async Task ExecuteAsync(StageContext context, CancellationToken token)
        {
            IngestionJob job = context.Job;

            List<ExtractedCell> cells = await _Deps.DocumentAtom.ExtractCellsAsync(job.DocumentType, context.SourceBytes, token).ConfigureAwait(false);
            job.BlobKey = await _Deps.Blobs.WriteAsync(job.Id, context.SourceBytes, token).ConfigureAwait(false);
            await _Deps.Journal.TryStoreAsync("atoms", () => _Deps.Artifacts.PutAtomsAsync(job.LinkId, Json.Serialize(cells), token), token).ConfigureAwait(false);

            if (cells.Count == 0) throw new IngestionHardFailException(IngestionStageEnum.CellExtraction, "No semantic cells extracted.");

            context.Cells = cells;
            context.Message = "Semantic cell extraction complete — extracted " + cells.Count + " cell(s).";
        }

        #endregion
    }
}
