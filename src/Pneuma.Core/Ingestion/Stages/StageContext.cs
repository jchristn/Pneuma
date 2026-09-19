namespace Pneuma.Core.Ingestion.Stages
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// The mutable per-job state that flows through the ordered pipeline stages. Each <see cref="IStage"/> reads
    /// the inputs it needs and writes its outputs here, so the orchestrator can run a uniform list of stages
    /// without threading a growing set of typed locals between them. One instance is created per job attempt.
    /// </summary>
    public class StageContext
    {
        #region Public-Members

        /// <summary>The job being processed.</summary>
        public IngestionJob Job { get; }

        /// <summary>The owning subject's display name (for grounding classification); resolved during categorization.</summary>
        public string SubjectName { get; set; } = "Unknown subject";

        /// <summary>The fetched source bytes (content retrieval).</summary>
        public byte[] SourceBytes { get; set; } = Array.Empty<byte>();

        /// <summary>The hex SHA-256 of the fetched source bytes, used for re-ingestion delta detection.</summary>
        public string ContentHash { get; set; } = String.Empty;

        /// <summary>The extracted semantic cells (cell extraction).</summary>
        public List<ExtractedCell> Cells { get; set; } = new List<ExtractedCell>();

        /// <summary>The candidate subgraph proposed by classification (and canonicalized in place).</summary>
        public CandidateSubgraph Subgraph { get; set; } = new CandidateSubgraph();

        /// <summary>The graph-merge result (created node ids, ref→id map, source node id, per-cell node ids).</summary>
        public MergeResult Merge { get; set; } = new MergeResult();

        /// <summary>The per-cell summaries produced by summarization.</summary>
        public List<CellSummary> Summaries { get; set; } = new List<CellSummary>();

        /// <summary>The chunks produced by chunking and (later) embedded in place.</summary>
        public List<SemanticChunk> Chunks { get; set; } = new List<SemanticChunk>();

        /// <summary>
        /// Prompt-provenance summary (which prompt version/hash shaped this run), built during classification and
        /// folded into the categorization-complete log line for reproducibility.
        /// </summary>
        public string Provenance { get; set; } = String.Empty;

        /// <summary>
        /// Set true by content retrieval when the fetched bytes are unchanged since the last successful
        /// ingestion (matching content hash): the orchestrator completes the job immediately and skips the
        /// remaining stages, because re-processing identical content is deterministic.
        /// </summary>
        public bool CompleteEarly { get; set; } = false;

        /// <summary>The completion message the current stage wants surfaced in the job's log.</summary>
        public string Message { get; set; } = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate a context for the given job.</summary>
        /// <param name="job">The job being processed.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="job"/> is null.</exception>
        public StageContext(IngestionJob job)
        {
            Job = job ?? throw new ArgumentNullException(nameof(job));
        }

        #endregion
    }
}
