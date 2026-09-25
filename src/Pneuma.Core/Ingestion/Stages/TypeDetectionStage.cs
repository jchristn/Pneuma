namespace Pneuma.Core.Ingestion.Stages
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// Detects the source document's type via DocumentAtom. An unknown or unsupported type is a deterministic
    /// hard failure (re-running would fail identically), so it throws <see cref="IngestionHardFailException"/>
    /// rather than being retried.
    /// </summary>
    public class TypeDetectionStage : IStage
    {
        #region Private-Members

        private readonly StageDependencies _Deps;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the stage.</summary>
        /// <param name="deps">Shared stage dependencies.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="deps"/> is null.</exception>
        public TypeDetectionStage(StageDependencies deps)
        {
            _Deps = deps ?? throw new ArgumentNullException(nameof(deps));
        }

        #endregion

        #region Public-Members

        /// <inheritdoc />
        public IngestionStageEnum Stage => IngestionStageEnum.TypeDetection;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        /// <exception cref="IngestionHardFailException">Thrown when the document type is unknown or unsupported.</exception>
        public async Task ExecuteAsync(StageContext context, CancellationToken token)
        {
            IngestionJob job = context.Job;

            TypeDetectResult detected = await _Deps.DocumentAtom.DetectTypeAsync(context.SourceBytes, token).ConfigureAwait(false);

            // The detector reports some genuine text (e.g. markdown full of box-drawing characters) as an unknown
            // binary type. Valid UTF-8 without control bytes is ingested as plain text instead of failing the job.
            if (detected.IsUnknown && TextSniffer.IsLikelyText(context.SourceBytes))
            {
                detected = new TypeDetectResult { Type = "Text", MimeType = "text/plain", Extension = "txt" };
                _Deps.Logging.Info("[TypeDetectionStage] job " + job.Id + ": detector returned Unknown for valid UTF-8 text; ingesting as Text.");
            }

            if (detected.IsUnknown) throw new IngestionHardFailException(IngestionStageEnum.TypeDetection, "Unknown or unsupported document type.");

            job.DocumentType = detected.Type;
            context.Message = "Type detection complete — detected document type: " + detected.Type + " (" + detected.MimeType + ").";
        }

        #endregion
    }
}
