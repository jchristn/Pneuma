namespace Pneuma.Core.Ingestion.Stages
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Ingestion.Prompts;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Serialization;

    /// <summary>
    /// Maps the extracted cells to a candidate subgraph using the job's completion endpoint. A large document is
    /// classified in bounded, symmetrically-overlapping batches (so no single model call carries a whole
    /// document's prompt) and the partial subgraphs are merged. Persists the candidate subgraph artifact and
    /// records prompt provenance. A missing completion endpoint is a deterministic hard failure.
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

            // The classification model comes from the job's chosen completion runner in the model-endpoint store;
            // its key is decrypted here for the direct provider call. No endpoint is a deterministic hard failure.
            ModelRunner? runner = await ResolveCompletionEndpointAsync(job, token).ConfigureAwait(false);
            if (runner == null) throw new IngestionHardFailException(IngestionStageEnum.Classification, "No completion model endpoint is available for classification.");

            string? apiKey = null;
            if (!String.IsNullOrEmpty(runner.AuthMaterialEncrypted))
            {
                try { apiKey = _Deps.Cipher.Decrypt(runner.AuthMaterialEncrypted); }
                catch (Exception) { apiKey = null; }
            }

            // Resolve the classification and ontology prompts: global base + per-subject override (Append/Replace)
            // + the legacy per-subject ontology columns, so a subject can refine classification and its ontology
            // without losing the shared base. Global is the fallback.
            PromptResolver resolver = new PromptResolver(_Deps.Db);
            ResolvedPrompt classifyResolved = await resolver.ResolveAsync(job.TenantId, job.SubjectId, "ontology.classify", subject?.OntologyClassifyPrompt, token).ConfigureAwait(false);
            string systemPrompt = String.IsNullOrWhiteSpace(classifyResolved.EffectiveContent) ? "Classify the content into the subject knowledge-graph ontology." : classifyResolved.EffectiveContent;

            ResolvedPrompt ontologyResolved = await resolver.ResolveAsync(job.TenantId, job.SubjectId, "ontology.definition", subject?.OntologyDefinitionPrompt, token).ConfigureAwait(false);
            string ontologyDefinition = ontologyResolved.EffectiveContent;

            // Effective per-subject batching tuning (subject override falling back to the system default). A large
            // document is classified as bounded batches rather than one enormous prompt so a slow completion model
            // can finish each call well within the stage timeout; a document that fits one batch keeps the single call.
            List<ExtractedCell> cells = context.Cells;
            int batchSize = _Deps.Concurrency.EffectiveClassificationBatchSize(job.SubjectId);
            CandidateSubgraph subgraph;
            if (cells.Count <= batchSize)
            {
                subgraph = await _Deps.Classifier.ClassifyAsync(cells, systemPrompt, ontologyDefinition, runner, apiKey, context.SubjectName, token).ConfigureAwait(false);
            }
            else
            {
                int overlap = _Deps.Concurrency.EffectiveClassificationBatchOverlap(job.SubjectId);
                int batchConcurrency = _Deps.Concurrency.EffectiveClassificationBatchConcurrency(job.SubjectId);
                subgraph = await ClassifyInBatchesAsync(cells, systemPrompt, ontologyDefinition, runner, apiKey, context.SubjectName, batchSize, overlap, batchConcurrency, token).ConfigureAwait(false);
            }

            await _Deps.Journal.TryStoreAsync("subgraph", () => _Deps.Artifacts.PutSubgraphAsync(job.LinkId, Json.Serialize(subgraph), token), token).ConfigureAwait(false);
            context.Subgraph = subgraph;
            context.Provenance = await BuildPromptProvenanceAsync(job, token).ConfigureAwait(false);
            context.Message = "Ontology / knowledge-graph mapping complete — proposed " + subgraph.Nodes.Count + " node(s) and " + subgraph.Edges.Count + " relationship(s).";
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Classify a large document as independent, bounded batches and merge the partial subgraphs. Each batch
        /// classifies its own <paramref name="batchSize"/> cells but is given <paramref name="overlap"/> cells of
        /// context on each side (read symmetrically before and after) so a relationship straddling a batch boundary
        /// is still seen from at least one side; the overlap yields duplicate nodes/edges that the downstream graph
        /// merge dedups by canonical type+name. Candidate Refs are only unique within a single model call, so each
        /// batch's Refs are namespaced before concatenation to keep every edge pointing at its own nodes.
        /// </summary>
        private async Task<CandidateSubgraph> ClassifyInBatchesAsync(
            List<ExtractedCell> cells,
            string systemPrompt,
            string ontologyDefinition,
            ModelRunner runner,
            string? apiKey,
            string subjectName,
            int batchSize,
            int overlap,
            int batchConcurrency,
            CancellationToken token)
        {
            List<int> starts = new List<int>();
            for (int start = 0; start < cells.Count; start += batchSize) starts.Add(start);

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
                            parts[batchIndex] = await _Deps.Classifier.ClassifyAsync(window, systemPrompt, ontologyDefinition, runner, apiKey, subjectName, token).ConfigureAwait(false);
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

        private async Task<ModelRunner?> ResolveCompletionEndpointAsync(IngestionJob job, CancellationToken token)
        {
            if (!String.IsNullOrWhiteSpace(job.CompletionEndpointId))
            {
                ModelRunner? byId = await _Deps.Db.ModelRunners.ReadAsync(job.CompletionEndpointId!, token).ConfigureAwait(false);
                if (byId != null && byId.Active) return byId;
            }

            List<ModelRunner> runners = await _Deps.Db.ModelRunners.EnumerateAsync(job.TenantId, token).ConfigureAwait(false);
            foreach (ModelRunner runner in runners)
            {
                if (!runner.Active) continue;
                if (runner.Capabilities.Contains(ModelCapabilityEnum.Completion)) return runner;
            }
            return null;
        }

        /// <summary>
        /// Build the prompt-provenance summary so a run is reproducible: which prompt version and exact content
        /// (by hash) shaped this candidate plan. If a prompt is later edited its hash changes, and a past job's
        /// provenance still shows what it actually used.
        /// </summary>
        private async Task<string> BuildPromptProvenanceAsync(IngestionJob job, CancellationToken token)
        {
            string[] keys = { "ontology.classify", "ontology.definition", "cell.summarize" };
            List<string> parts = new List<string>();
            foreach (string key in keys)
            {
                Prompt? prompt = await _Deps.Db.Prompts.ReadByKeyAsync(job.TenantId, key, token).ConfigureAwait(false);
                string hash = String.IsNullOrEmpty(prompt?.Content) ? "(none)" : ShortHash(prompt!.Content!);
                int version = prompt?.Version ?? 0;
                parts.Add(key + " v" + version + "@" + hash);
            }
            return String.Join(", ", parts);
        }

        private static string ShortHash(string content)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < 4 && i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));
                return builder.ToString();
            }
        }

        #endregion
    }
}
