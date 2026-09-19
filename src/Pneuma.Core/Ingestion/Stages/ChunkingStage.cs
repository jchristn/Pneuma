namespace Pneuma.Core.Ingestion.Stages
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;

    /// <summary>
    /// Chunks each cell's text (and each summary) into retrieval-sized chunks using the subject's chunking
    /// configuration (falling back to the platform defaults). No embeddings are produced here; each chunk is
    /// stamped with its originating Cell node id so retrieval hits resolve back to a graph node.
    /// </summary>
    public class ChunkingStage : IStage
    {
        #region Private-Members

        private readonly StageDependencies _Deps;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the stage.</summary>
        /// <param name="deps">Shared stage dependencies.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="deps"/> is null.</exception>
        public ChunkingStage(StageDependencies deps)
        {
            _Deps = deps ?? throw new ArgumentNullException(nameof(deps));
        }

        #endregion

        #region Public-Members

        /// <inheritdoc />
        public IngestionStageEnum Stage => IngestionStageEnum.Chunking;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task ExecuteAsync(StageContext context, CancellationToken token)
        {
            IngestionJob job = context.Job;
            List<ExtractedCell> cells = context.Cells;
            List<string> cellNodeIds = context.Merge.CellNodeIds;
            List<CellSummary> summaries = context.Summaries;

            List<SemanticChunk> all = new List<SemanticChunk>();

            // Resolve the subject's chunking configuration so short-form and long-form subjects can chunk
            // differently; falls back to the platform defaults when the subject is missing or unset.
            ChunkingOptions options = await ResolveChunkingOptionsAsync(job, token).ConfigureAwait(false);

            for (int i = 0; i < cells.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                ExtractedCell cell = cells[i];
                if (String.IsNullOrWhiteSpace(cell.Text)) continue;
                string cellNodeId = (cellNodeIds != null && i < cellNodeIds.Count) ? cellNodeIds[i] : String.Empty;
                List<SemanticChunk> chunks = await _Deps.Processor.ChunkAsync(cell.Text, options, token).ConfigureAwait(false);
                if (chunks.Count == 0) chunks.Add(new SemanticChunk { Text = cell.Text });
                foreach (SemanticChunk chunk in chunks)
                {
                    if (String.IsNullOrWhiteSpace(chunk.Text)) continue;
                    chunk.CellNodeId = cellNodeId;
                    all.Add(chunk);
                }
            }

            if (summaries != null)
            {
                foreach (CellSummary summary in summaries)
                {
                    token.ThrowIfCancellationRequested();
                    if (String.IsNullOrWhiteSpace(summary.Text)) continue;
                    List<SemanticChunk> chunks = await _Deps.Processor.ChunkAsync(summary.Text, options, token).ConfigureAwait(false);
                    if (chunks.Count == 0) chunks.Add(new SemanticChunk { Text = summary.Text });
                    foreach (SemanticChunk chunk in chunks)
                    {
                        if (String.IsNullOrWhiteSpace(chunk.Text)) continue;
                        chunk.CellNodeId = summary.CellNodeId;
                        all.Add(chunk);
                    }
                }
            }

            context.Chunks = all;
            context.Message = "Chunking complete — produced " + all.Count + " chunk(s) from " + cells.Count + " cell(s) and " + (summaries != null ? summaries.Count : 0) + " summary(ies).";
        }

        #endregion

        #region Private-Methods

        private async Task<ChunkingOptions> ResolveChunkingOptionsAsync(IngestionJob job, CancellationToken token)
        {
            ChunkingOptions options = new ChunkingOptions();
            Subject? subject = await _Deps.Db.Subjects.ReadAsync(job.TenantId, job.SubjectId, token).ConfigureAwait(false);
            if (subject == null) return options;
            if (!String.IsNullOrWhiteSpace(subject.ChunkStrategy)) options.Strategy = subject.ChunkStrategy!;
            options.MaxTokens = subject.ChunkMaxTokens;
            options.OverlapCount = subject.ChunkOverlapTokens;
            return options;
        }

        #endregion
    }
}
