namespace Pneuma.Core.Ingestion.Stages
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Ingestion.Prompts;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// Summarizes each substantive cell with bounded per-job concurrency. Short fragments (below the effective
    /// minimum cell length) are skipped so a large document does not fire a model call per trivial cell; a single
    /// cell's failure is logged and skipped (non-fatal) while cancellation still propagates.
    /// </summary>
    public class SummarizationStage : IStage
    {
        #region Private-Members

        private readonly StageDependencies _Deps;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the stage.</summary>
        /// <param name="deps">Shared stage dependencies.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="deps"/> is null.</exception>
        public SummarizationStage(StageDependencies deps)
        {
            _Deps = deps ?? throw new ArgumentNullException(nameof(deps));
        }

        #endregion

        #region Public-Members

        /// <inheritdoc />
        public IngestionStageEnum Stage => IngestionStageEnum.Summarization;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task ExecuteAsync(StageContext context, CancellationToken token)
        {
            IngestionJob job = context.Job;
            List<ExtractedCell> cells = context.Cells;
            List<string> cellNodeIds = context.Merge.CellNodeIds;

            ResolvedPrompt summarizeResolved = await new PromptResolver(_Deps.Db).ResolveAsync(job.TenantId, job.SubjectId, "cell.summarize", null, token).ConfigureAwait(false);
            string? summarizationPrompt = String.IsNullOrWhiteSpace(summarizeResolved.EffectiveContent) ? null : summarizeResolved.EffectiveContent;

            // Effective per-subject tuning (subject override falling back to the system default).
            int minCellLength = _Deps.Concurrency.EffectiveSummarizationMinCellLength(job.SubjectId);
            int summarizationConcurrency = _Deps.Concurrency.EffectiveSummarizationConcurrency(job.SubjectId);

            // Only summarize cells with enough substance to be worth a model call — short fragments (headings,
            // captions, single list items) are skipped so a large document does not fire a model call per trivial cell.
            List<int> targets = new List<int>();
            for (int i = 0; i < cells.Count; i++)
            {
                string? text = cells[i]?.Text;
                if (!String.IsNullOrWhiteSpace(text) && text!.Trim().Length >= minCellLength) targets.Add(i);
            }

            List<CellSummary> summaries = new List<CellSummary>();
            if (targets.Count > 0)
            {
                // Summarize with bounded per-job concurrency (Task.WhenAll gated by a semaphore) so one document does
                // not issue hundreds of serial model calls and monopolize its stage slot. A single cell's failure is
                // non-fatal — logged and skipped — so one bad cell cannot thrash the whole job; cancellation still propagates.
                CellSummary?[] produced = new CellSummary?[targets.Count];
                using (SemaphoreSlim gate = new SemaphoreSlim(summarizationConcurrency, summarizationConcurrency))
                {
                    List<Task> tasks = new List<Task>(targets.Count);
                    for (int slot = 0; slot < targets.Count; slot++)
                    {
                        int resultIndex = slot;
                        int cellIndex = targets[slot];
                        tasks.Add(Task.Run(async () =>
                        {
                            await gate.WaitAsync(token).ConfigureAwait(false);
                            try
                            {
                                string summary = await _Deps.Processor.SummarizeAsync(cells[cellIndex].Text, summarizationPrompt, job.CompletionEndpointId, token).ConfigureAwait(false);
                                if (!String.IsNullOrWhiteSpace(summary))
                                {
                                    produced[resultIndex] = new CellSummary
                                    {
                                        CellNodeId = (cellNodeIds != null && cellIndex < cellNodeIds.Count) ? cellNodeIds[cellIndex] : String.Empty,
                                        Text = summary
                                    };
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception e)
                            {
                                _Deps.Logging.Warn("[SummarizationStage] summarization of a cell failed (skipped): " + e.Message);
                            }
                            finally
                            {
                                gate.Release();
                            }
                        }, token));
                    }
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }

                foreach (CellSummary? summary in produced)
                {
                    if (summary != null) summaries.Add(summary);
                }
            }

            context.Summaries = summaries;
            context.Message = "Summarization complete — produced " + summaries.Count + " summary(ies) from " + cells.Count + " cell(s).";
        }

        #endregion
    }
}
