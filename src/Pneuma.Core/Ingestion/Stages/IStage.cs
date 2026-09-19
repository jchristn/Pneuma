namespace Pneuma.Core.Ingestion.Stages
{
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Ingestion.Enums;

    /// <summary>
    /// A single, uniform unit of ingestion work. Each pipeline step (content retrieval, type detection, cell
    /// extraction, classification, canonicalization, graph merge, relationship consolidation, summarization,
    /// chunking, embedding, indexing) is one <see cref="IStage"/> so the orchestrator can drive an ordered list
    /// of them through the same runner — the same concurrency gate, per-stage timeout, telemetry span, metric,
    /// and event recording — rather than each step being wired in an ad-hoc way. A stage reads its inputs from,
    /// and writes its outputs to, the shared <see cref="StageContext"/>, and sets <see cref="StageContext.Message"/>
    /// to the human-readable line shown in the job's log. Deterministic, non-retryable failures are signaled by
    /// throwing <see cref="IngestionHardFailException"/>; any other exception is treated as transient and retried.
    /// </summary>
    public interface IStage
    {
        /// <summary>The pipeline stage this unit implements (used for gating, timing, telemetry, and logging).</summary>
        IngestionStageEnum Stage { get; }

        /// <summary>
        /// Execute the stage against the shared context. Reads the inputs it needs from the context and writes
        /// its outputs back onto it; sets <see cref="StageContext.Message"/> to the completion message.
        /// </summary>
        /// <param name="context">The per-job pipeline context.</param>
        /// <param name="token">Cancellation token (already bounded by the per-stage timeout).</param>
        /// <returns>A task that completes when the stage's work is done.</returns>
        /// <exception cref="IngestionHardFailException">Thrown for a deterministic failure that must not be retried.</exception>
        Task ExecuteAsync(StageContext context, CancellationToken token);
    }
}
