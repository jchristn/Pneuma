namespace Pneuma.Core.Ingestion.Stages
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;

    /// <summary>
    /// Chunks each cell's text (and each summary) into retrieval-sized chunks using the subject's chunking
    /// configuration (falling back to the platform defaults). Chunks are counted in the embedding model's own tokens and
    /// sized to fit the model's input limit with a small safety margin. When the subject asks for chunk headers, each
    /// chunk is embedded with the document title and its section's heading path in front (the stored text is unchanged).
    /// No embeddings are produced here; each chunk is stamped with its originating Cell node id so retrieval hits
    /// resolve back to a graph node.
    /// </summary>
    public class ChunkingStage : IStage
    {
        #region Public-Members

        /// <inheritdoc />
        public IngestionStageEnum Stage => IngestionStageEnum.Chunking;

        /// <summary>
        /// Share of the chunk size a header may use; longer headers are cut at a word boundary. Default 0.25; clamped
        /// to [0.05, 0.5].
        /// </summary>
        public double HeaderBudgetFraction
        {
            get { return _HeaderBudgetFraction; }
            set { _HeaderBudgetFraction = Math.Clamp(value, 0.05, 0.5); }
        }

        #endregion

        #region Private-Members

        private readonly StageDependencies _Deps;
        private double _HeaderBudgetFraction = 0.25;

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

        #region Public-Methods

        /// <inheritdoc />
        public async Task ExecuteAsync(StageContext context, CancellationToken token)
        {
            IngestionJob job = context.Job;
            List<ExtractedCell> cells = context.Cells;
            List<string> cellNodeIds = context.Merge.CellNodeIds;
            List<CellSummary> summaries = context.Summaries;

            List<SemanticChunk> all = new List<SemanticChunk>();

            Subject? subject = await _Deps.Db.Subjects.ReadAsync(job.TenantId, job.SubjectId, token).ConfigureAwait(false);
            ChunkingOptions options = await ResolveChunkingOptionsAsync(subject, token).ConfigureAwait(false);
            context.ChunkingOptions = options;
            ChunkHeaderModeEnum headerMode = subject?.ChunkHeaders ?? ChunkHeaderModeEnum.None;
            string? title = headerMode == ChunkHeaderModeEnum.None ? null : await ResolveTitleAsync(job, cells, token).ConfigureAwait(false);

            for (int i = 0; i < cells.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                ExtractedCell cell = cells[i];
                if (String.IsNullOrWhiteSpace(cell.Text)) continue;
                string cellNodeId = (cellNodeIds != null && i < cellNodeIds.Count) ? cellNodeIds[i] : String.Empty;

                ChunkingOptions cellOptions = options.Clone();
                cellOptions.ContextHeader = BuildHeader(headerMode, title, cell.HeadingPath, options.MaxTokens, HeaderBudgetFraction);
                List<SemanticChunk> chunks = await _Deps.Processor.ChunkAsync(cell.Text, cellOptions, token).ConfigureAwait(false);
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
                // A summary already restates its cell, so it only gets the document title, not the heading path.
                ChunkingOptions summaryOptions = options.Clone();
                summaryOptions.ContextHeader = BuildHeader(headerMode == ChunkHeaderModeEnum.None ? ChunkHeaderModeEnum.None : ChunkHeaderModeEnum.Title, title, null, options.MaxTokens, HeaderBudgetFraction);
                foreach (CellSummary summary in summaries)
                {
                    token.ThrowIfCancellationRequested();
                    if (String.IsNullOrWhiteSpace(summary.Text)) continue;
                    List<SemanticChunk> chunks = await _Deps.Processor.ChunkAsync(summary.Text, summaryOptions, token).ConfigureAwait(false);
                    if (chunks.Count == 0) chunks.Add(new SemanticChunk { Text = summary.Text });
                    foreach (SemanticChunk chunk in chunks)
                    {
                        if (String.IsNullOrWhiteSpace(chunk.Text)) continue;
                        chunk.CellNodeId = summary.CellNodeId;
                        chunk.Kind = "summary";
                        all.Add(chunk);
                    }
                }
            }

            context.Chunks = all;
            job.Completeness.ChunksProduced = all.Count;
            context.Message = "Chunking complete: produced " + all.Count + " chunk(s) from " + cells.Count + " cell(s) and " + (summaries != null ? summaries.Count : 0) + " summary(ies). " +
                "Strategy " + options.Strategy + ", up to " + options.MaxTokens.ToString(CultureInfo.InvariantCulture) + " tokens" +
                (String.IsNullOrEmpty(options.ModelId) ? " (cl100k_base)" : " counted in " + options.ModelId + " tokens") +
                (options.EffectiveInputBudget != null ? ", model input limit " + options.EffectiveInputBudget.Value.ToString(CultureInfo.InvariantCulture) : String.Empty) +
                ", overlap " + options.OverlapCount.ToString(CultureInfo.InvariantCulture) + ", headers " + headerMode + ".";
        }

        /// <summary>
        /// Build a chunk's context header from the document title and heading path, cut to a share of the chunk size.
        /// </summary>
        /// <param name="mode">The subject's header mode.</param>
        /// <param name="title">The document title, or null.</param>
        /// <param name="headingPath">The cell's heading path, or null.</param>
        /// <param name="maxTokens">The chunk size in tokens.</param>
        /// <param name="headerBudgetFraction">Share of the chunk size the header may use (0.05 to 0.5).</param>
        /// <returns>The header, or null when there is nothing to add.</returns>
        public static string? BuildHeader(ChunkHeaderModeEnum mode, string? title, string? headingPath, int maxTokens, double headerBudgetFraction)
        {
            double fraction = Math.Clamp(headerBudgetFraction, 0.05, 0.5);
            if (mode == ChunkHeaderModeEnum.None) return null;

            string header = title?.Trim() ?? String.Empty;
            if (mode == ChunkHeaderModeEnum.TitleAndHeadings && !String.IsNullOrWhiteSpace(headingPath))
            {
                string path = headingPath!.Trim();
                // The top heading is often the title itself; do not repeat it.
                if (header.Length > 0 && path.StartsWith(header, StringComparison.OrdinalIgnoreCase)) header = path;
                else header = header.Length > 0 ? header + " > " + path : path;
            }

            if (header.Length == 0) return null;

            // About four characters per token keeps the header inside its share of the budget; the chunker then charges
            // the header's real token count against the chunk.
            int maxChars = Math.Max(16, (int)(maxTokens * fraction * 4));
            if (header.Length > maxChars)
            {
                int cut = header.LastIndexOf(' ', Math.Min(maxChars, header.Length - 1));
                header = header.Substring(0, cut > maxChars / 2 ? cut : maxChars).TrimEnd();
            }

            return header;
        }

        #endregion

        #region Private-Methods

        private async Task<ChunkingOptions> ResolveChunkingOptionsAsync(Subject? subject, CancellationToken token)
        {
            ChunkingOptions options = new ChunkingOptions();
            if (subject == null) return options;
            if (!String.IsNullOrWhiteSpace(subject.ChunkStrategy)) options.Strategy = subject.ChunkStrategy!;
            options.MaxTokens = subject.ChunkMaxTokens;
            options.OverlapCount = subject.ChunkOverlapTokens;

            // Size chunks in the embedding model's own tokens (e.g. WordPiece for nomic-embed-text), so a chunk the
            // chunker counts as within budget is also within budget for the model that embeds it. A runner's explicit
            // input limit overrides the known limit for the model family.
            if (!String.IsNullOrWhiteSpace(subject.EmbeddingModel))
            {
                ModelRunner? runner = await _Deps.Db.ModelRunners.ReadAsync(subject.EmbeddingModel!, token).ConfigureAwait(false);
                string? model = runner == null ? null : (String.IsNullOrWhiteSpace(runner.DefaultEmbeddingModel) ? runner.DefaultModel : runner.DefaultEmbeddingModel);
                if (!String.IsNullOrWhiteSpace(model)) options.ModelId = model;
                if (runner != null && runner.MaxInputTokens > 0) options.EffectiveInputBudget = runner.MaxInputTokens;
            }

            return options;
        }

        private async Task<string?> ResolveTitleAsync(IngestionJob job, List<ExtractedCell> cells, CancellationToken token)
        {
            SubjectLink? link = await _Deps.Db.SubjectLinks.ReadAsync(job.TenantId, job.LinkId, token).ConfigureAwait(false);
            if (link != null && !String.IsNullOrWhiteSpace(link.Title)) return link.Title!.Trim();

            foreach (ExtractedCell cell in cells)
            {
                if (cell.HeaderLevel == 1 && !String.IsNullOrWhiteSpace(cell.HeadingPath)) return cell.HeadingPath!.Trim();
            }

            return null;
        }

        #endregion
    }
}
