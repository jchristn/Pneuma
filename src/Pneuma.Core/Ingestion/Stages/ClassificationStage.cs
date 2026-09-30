namespace Pneuma.Core.Ingestion.Stages
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Ontologies;
    using Pneuma.Core.Serialization;

    /// <summary>
    /// Maps the extracted cells to a candidate subgraph using the job's completion endpoint. The subject's pinned ontology
    /// version (when it has one) supplies the definition and a taxonomy matched deterministically in the cells before any
    /// model call; identical requests are answered from the classification cache. A large document is classified in
    /// bounded, symmetrically-overlapping batches (so no single model call carries a whole document's prompt) and the
    /// partial subgraphs are merged. Persists the candidate subgraph artifact and records provenance. A missing
    /// completion endpoint is a deterministic hard failure.
    /// </summary>
    public class ClassificationStage : IStage
    {
        #region Private-Members

        private readonly StageDependencies _Deps;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the stage.</summary>
        /// <param name="deps">Shared stage dependencies.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="deps"/> is null.</exception>
        public ClassificationStage(StageDependencies deps)
        {
            _Deps = deps ?? throw new ArgumentNullException(nameof(deps));
        }

        #endregion

        #region Public-Members

        /// <inheritdoc />
        public IngestionStageEnum Stage => IngestionStageEnum.Classification;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        /// <exception cref="IngestionHardFailException">Thrown when no completion endpoint is available.</exception>
        public async Task ExecuteAsync(StageContext context, CancellationToken token)
        {
            IngestionJob job = context.Job;

            Subject? subject = await _Deps.Db.Subjects.ReadByIdAsync(job.SubjectId, token).ConfigureAwait(false);
            context.SubjectName = subject?.DisplayName ?? "Unknown subject";

            // The runner, prompts (system default, tenant and subject overrides), and pinned ontology version are resolved
            // once per job. No completion endpoint is a deterministic hard failure.
            ClassificationSetup? setup = await ClassificationSetupBuilder.BuildAsync(_Deps.Db, _Deps.Cipher, job.TenantId, subject, job.CompletionEndpointId, token).ConfigureAwait(false);
            if (setup == null) throw new IngestionHardFailException(IngestionStageEnum.Classification, IngestionFailureCategoryEnum.Configuration, "No completion model endpoint is available for classification.");
            context.Classification = setup;

            List<ExtractedCell> cells = context.Cells;
            MatchTaxonomy(context, setup, cells);

            // Effective per-subject batching tuning (subject override falling back to the system default). A large
            // document is classified as bounded batches rather than one enormous prompt so a slow completion model
            // can finish each call well within the stage timeout; a document that fits one batch keeps the single call.
            int batchSize = _Deps.Concurrency.EffectiveClassificationBatchSize(job.SubjectId);
            CandidateSubgraph subgraph;
            if (cells.Count <= batchSize)
            {
                job.Completeness.ClassificationBatches = 1;
                try
                {
                    subgraph = await ClassifyAsync(context, setup, cells, 0, cells.Count, token).ConfigureAwait(false);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    // The document still ingests (its cells become searchable Cell nodes) but its entities and
                    // relationships are missing from the graph, which the warning makes visible.
                    _Deps.Logging.Warn("[ClassificationStage] classification failed (skipped): " + e.Message);
                    job.Completeness.IncrementClassificationBatchesFailed();
                    context.AddWarning("Classification failed and the document's entities were left out of the graph: " + e.Message);
                    PneumaMetrics.RecordIngestionPartial("Classification", "classification_batch");
                    subgraph = new CandidateSubgraph();
                }
            }
            else
            {
                int overlap = _Deps.Concurrency.EffectiveClassificationBatchOverlap(job.SubjectId);
                int batchConcurrency = _Deps.Concurrency.EffectiveClassificationBatchConcurrency(job.SubjectId);
                subgraph = await ClassifyInBatchesAsync(context, setup, batchSize, overlap, batchConcurrency, token).ConfigureAwait(false);
            }

            await _Deps.Journal.TryStoreAsync("subgraph", () => _Deps.Artifacts.PutSubgraphAsync(job.LinkId, Json.Serialize(subgraph), token), token).ConfigureAwait(false);
            context.Subgraph = subgraph;
            context.Provenance = setup.Provenance + ", cache " + job.Completeness.ClassificationCacheHits.ToString(CultureInfo.InvariantCulture) + "/" +
                job.Completeness.ClassificationBatches.ToString(CultureInfo.InvariantCulture) + " hits";
            context.Message = "Ontology / knowledge-graph mapping complete — proposed " + subgraph.Nodes.Count + " node(s) and " + subgraph.Edges.Count + " relationship(s)" +
                (job.Completeness.ClassificationCacheHits > 0 ? " (" + job.Completeness.ClassificationCacheHits + " batch(es) from the classification cache)" : String.Empty) +
                (job.Completeness.TaxonomyMatches > 0 ? "; " + job.Completeness.TaxonomyMatches + " taxonomy match(es)." : ".");
        }

        #endregion

        #region Private-Methods

        /// <summary>Match the pinned version's taxonomy in every cell (deterministic, no model call).</summary>
        private static void MatchTaxonomy(StageContext context, ClassificationSetup setup, List<ExtractedCell> cells)
        {
            context.TaxonomyMatches = new Dictionary<int, List<string>>();
            if (setup.Matcher == null) return;
            int total = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                List<TaxonomyMatch> matches = setup.Matcher.Match(cells[i].Text);
                if (matches.Count == 0) continue;
                List<string> keys = matches.Select(m => m.ConceptKey).Distinct(StringComparer.Ordinal).ToList();
                context.TaxonomyMatches[i] = keys;
                total += keys.Count;
            }
            context.Job.Completeness.TaxonomyMatches = total;
        }

        /// <summary>Classify the cells in [start, end), from the cache when an identical request was answered before.</summary>
        private async Task<CandidateSubgraph> ClassifyAsync(StageContext context, ClassificationSetup setup, List<ExtractedCell> window, int start, int end, CancellationToken token)
        {
            List<string> conceptKeys = new List<string>();
            for (int i = start; i < end; i++)
            {
                List<string>? keys;
                if (context.TaxonomyMatches.TryGetValue(i, out keys)) conceptKeys.AddRange(keys);
            }
            string userPrompt = PolyPromptClassifier.BuildUserPrompt(window, context.SubjectName, setup.Hint(conceptKeys));
            string key = ClassificationCache.Key(setup.Runner.Id, setup.Runner.DefaultModel, setup.Temperature, setup.SystemPrompt, userPrompt);

            if (setup.CacheEnabled)
            {
                CandidateSubgraph? cached = null;
                try { cached = await _Deps.ClassificationCache.TryGetAsync(context.Job.TenantId, key, token).ConfigureAwait(false); }
                catch (Exception e) when (!(e is OperationCanceledException)) { _Deps.Logging.Warn("[ClassificationStage] cache read failed: " + e.Message); }
                if (cached != null)
                {
                    context.Job.Completeness.IncrementClassificationCacheHits();
                    PneumaMetrics.RecordClassificationCache("hit");
                    return cached;
                }
                PneumaMetrics.RecordClassificationCache("miss");
            }

            CandidateSubgraph result = await _Deps.Classifier.ClassifyAsync(setup.SystemPrompt, userPrompt, setup.Runner, setup.ApiKey, setup.Temperature, token).ConfigureAwait(false);
            if (setup.CacheEnabled)
            {
                try { await _Deps.ClassificationCache.StoreAsync(context.Job.TenantId, context.Job.SubjectId, key, result, token).ConfigureAwait(false); }
                catch (Exception e) when (!(e is OperationCanceledException)) { _Deps.Logging.Warn("[ClassificationStage] cache write failed: " + e.Message); }
            }
            return result;
        }

        /// <summary>
        /// Classify a large document as independent, bounded batches and merge the partial subgraphs. Each batch
        /// classifies its own <paramref name="batchSize"/> cells but is given <paramref name="overlap"/> cells of
        /// context on each side (read symmetrically before and after) so a relationship straddling a batch boundary
        /// is still seen from at least one side; the overlap yields duplicate nodes/edges that the downstream graph
        /// merge dedups by canonical type+name. Candidate Refs are only unique within a single model call, so each
        /// batch's Refs are namespaced before concatenation to keep every edge pointing at its own nodes.
        /// </summary>
        private async Task<CandidateSubgraph> ClassifyInBatchesAsync(StageContext context, ClassificationSetup setup, int batchSize, int overlap, int batchConcurrency, CancellationToken token)
        {
            List<ExtractedCell> cells = context.Cells;
            List<int> starts = new List<int>();
            for (int start = 0; start < cells.Count; start += batchSize) starts.Add(start);
            context.Job.Completeness.ClassificationBatches = starts.Count;

            CandidateSubgraph?[] parts = new CandidateSubgraph?[starts.Count];
            using (SemaphoreSlim gate = new SemaphoreSlim(batchConcurrency, batchConcurrency))
            {
                List<Task> tasks = new List<Task>(starts.Count);
                for (int b = 0; b < starts.Count; b++)
                {
                    int batchIndex = b;
                    int ownedStart = starts[b];
                    int ownedEnd = Math.Min(ownedStart + batchSize, cells.Count);
                    int windowStart = Math.Max(0, ownedStart - overlap);
                    int windowEnd = Math.Min(cells.Count, ownedEnd + overlap);
                    tasks.Add(Task.Run(async () =>
                    {
                        await gate.WaitAsync(token).ConfigureAwait(false);
                        try
                        {
                            List<ExtractedCell> window = cells.GetRange(windowStart, windowEnd - windowStart);
                            parts[batchIndex] = await ClassifyAsync(context, setup, window, windowStart, windowEnd, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception e)
                        {
                            // One batch failing (e.g. a transient model error) loses that slice of the graph but must
                            // not fail the whole document — the successful batches still merge. Cancellation (operator
                            // stop / stage timeout) is rethrown so the stage fails as a unit.
                            _Deps.Logging.Warn("[ClassificationStage] classification of a cell batch failed (skipped): " + e.Message);
                            context.Job.Completeness.IncrementClassificationBatchesFailed();
                            context.AddWarning("Classification of cells " + (ownedStart + 1).ToString(CultureInfo.InvariantCulture) + " to " +
                                ownedEnd.ToString(CultureInfo.InvariantCulture) + " failed and those cells were left out of the graph: " + e.Message);
                            PneumaMetrics.RecordIngestionPartial("Classification", "classification_batch");
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }, token));
                }
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            CandidateSubgraph merged = new CandidateSubgraph();
            for (int b = 0; b < parts.Length; b++)
            {
                CandidateSubgraph? part = parts[b];
                if (part == null) continue;
                string prefix = "b" + b.ToString(CultureInfo.InvariantCulture) + "_";
                if (part.Nodes != null)
                {
                    foreach (CandidateNode node in part.Nodes)
                    {
                        node.Ref = prefix + node.Ref;
                        merged.Nodes.Add(node);
                    }
                }
                if (part.Edges != null)
                {
                    foreach (CandidateEdge edge in part.Edges)
                    {
                        edge.FromRef = prefix + edge.FromRef;
                        edge.ToRef = prefix + edge.ToRef;
                        merged.Edges.Add(edge);
                    }
                }
            }
            return merged;
        }

        #endregion
    }
}
