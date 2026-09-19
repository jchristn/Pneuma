namespace Pneuma.Core.Ingestion.Pipeline
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ingestion.Models;

    /// <summary>
    /// Runtime-adjustable concurrency limiter for the ingestion pipeline. Each gated stage and the job pool are
    /// backed by an <see cref="AsyncSemaphoreGate"/> (a SemaphoreSlim whose cancelled wait provably consumes no
    /// permit, plus an idempotent releaser). A tuning change is applied by swapping in a fresh gate at the new
    /// cap: new acquisitions immediately observe the new limit while any in-flight holders drain on the old
    /// instance (a cap decrease can therefore be briefly exceeded by work already running — acceptable for a
    /// tuning knob), and releasing a swapped-out gate is a harmless no-op. The system defaults (an <see cref="IngestionTuning"/>
    /// singleton) provide a global cap shared by non-overridden subjects; a subject with a per-stage override
    /// gets its own dedicated limiter for that stage. Non-gate values (stage timeout, summarization concurrency /
    /// min length) are read per job from the effective settings (subject override falling back to system default).
    /// </summary>
    public class ConcurrencyManager
    {
        #region Private-Members


        private static readonly IngestionStageEnum[] _GatedStages = new IngestionStageEnum[]
        {
            IngestionStageEnum.ContentRetrieval,
            IngestionStageEnum.TypeDetection,
            IngestionStageEnum.CellExtraction,
            IngestionStageEnum.Classification,
            IngestionStageEnum.GraphMerge,
            IngestionStageEnum.RelationshipConsolidation,
            IngestionStageEnum.Summarization,
            IngestionStageEnum.Chunking,
            IngestionStageEnum.Embedding,
            IngestionStageEnum.Indexing
        };

        private volatile IngestionTuning _Defaults;
        private readonly ConcurrentDictionary<IngestionStageEnum, AsyncSemaphoreGate> _DefaultGates = new ConcurrentDictionary<IngestionStageEnum, AsyncSemaphoreGate>();
        private volatile AsyncSemaphoreGate _JobPool;
        private readonly ConcurrentDictionary<string, SubjectConcurrencyOverrides> _Overrides = new ConcurrentDictionary<string, SubjectConcurrencyOverrides>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<IngestionStageEnum, AsyncSemaphoreGate>> _SubjectGates = new ConcurrentDictionary<string, ConcurrentDictionary<IngestionStageEnum, AsyncSemaphoreGate>>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the manager with the given system defaults.</summary>
        /// <param name="defaults">The system-wide tuning defaults.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="defaults"/> is null.</exception>
        public ConcurrencyManager(IngestionTuning defaults)
        {
            _Defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
            foreach (IngestionStageEnum stage in _GatedStages)
            {
                _DefaultGates[stage] = new AsyncSemaphoreGate(DefaultCapFor(stage, _Defaults));
            }
            _JobPool = new AsyncSemaphoreGate(_Defaults.MaxConcurrentTasks);
        }

        #endregion

        #region Public-Methods

        /// <summary>Load persisted system defaults (seeding them from the constructor defaults if absent) and all per-subject overrides.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="db"/> is null.</exception>
        public async Task InitializeAsync(DatabaseDriverBase db, CancellationToken token = default)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));

            IngestionTuning? stored = await db.IngestionTuning.ReadAsync(token).ConfigureAwait(false);
            if (stored == null) stored = await db.IngestionTuning.UpsertAsync(_Defaults, token).ConfigureAwait(false);
            ApplySystemDefaults(stored);

            List<Subject> subjects = await db.Subjects.EnumerateWithConcurrencyOverridesAsync(token).ConfigureAwait(false);
            foreach (Subject subject in subjects)
            {
                SubjectConcurrencyOverrides? overrides = subject.GetConcurrencyOverrides();
                if (overrides != null && !overrides.IsEmpty()) ApplySubjectOverride(subject.Id, overrides);
            }
        }

        /// <summary>Acquire a slot for the given stage and subject. Ungated stages return an immediately-completed no-op.</summary>
        /// <param name="stage">The pipeline stage.</param>
        /// <param name="subjectId">The owning subject id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A handle to dispose when the stage completes.</returns>
        public Task<IDisposable> AcquireStageAsync(IngestionStageEnum stage, string subjectId, CancellationToken token)
        {
            AsyncSemaphoreGate? gate = ResolveStageGate(stage, subjectId);
            if (gate == null) return Task.FromResult<IDisposable>(NoopScope.Instance);
            return gate.AcquireAsync(token);
        }

        /// <summary>Acquire a job-pool slot (bounds how many jobs process at once).</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A handle to dispose when the job completes.</returns>
        public Task<IDisposable> AcquireJobSlotAsync(CancellationToken token)
        {
            return _JobPool.AcquireAsync(token);
        }

        /// <summary>The effective per-stage timeout (seconds) for a subject.</summary>
        /// <param name="subjectId">The subject id.</param>
        /// <returns>The effective stage timeout.</returns>
        public int EffectiveStageTimeoutSeconds(string subjectId)
        {
            SubjectConcurrencyOverrides? o = GetOverride(subjectId);
            return (o != null && o.StageTimeoutSeconds.HasValue) ? o.StageTimeoutSeconds.Value : _Defaults.StageTimeoutSeconds;
        }

        /// <summary>The effective per-job summarization concurrency for a subject.</summary>
        /// <param name="subjectId">The subject id.</param>
        /// <returns>The effective summarization concurrency.</returns>
        public int EffectiveSummarizationConcurrency(string subjectId)
        {
            SubjectConcurrencyOverrides? o = GetOverride(subjectId);
            return (o != null && o.SummarizationConcurrency.HasValue) ? o.SummarizationConcurrency.Value : _Defaults.SummarizationConcurrency;
        }

        /// <summary>The effective minimum cell length to summarize for a subject.</summary>
        /// <param name="subjectId">The subject id.</param>
        /// <returns>The effective minimum cell length.</returns>
        public int EffectiveSummarizationMinCellLength(string subjectId)
        {
            SubjectConcurrencyOverrides? o = GetOverride(subjectId);
            return (o != null && o.SummarizationMinCellLength.HasValue) ? o.SummarizationMinCellLength.Value : _Defaults.SummarizationMinCellLength;
        }

        /// <summary>The effective number of cells classified per model call for a subject.</summary>
        /// <param name="subjectId">The subject id.</param>
        /// <returns>The effective classification batch size.</returns>
        public int EffectiveClassificationBatchSize(string subjectId)
        {
            SubjectConcurrencyOverrides? o = GetOverride(subjectId);
            return (o != null && o.ClassificationBatchSize.HasValue) ? o.ClassificationBatchSize.Value : _Defaults.ClassificationBatchSize;
        }

        /// <summary>The effective classification batch context overlap (cells per side) for a subject.</summary>
        /// <param name="subjectId">The subject id.</param>
        /// <returns>The effective classification batch overlap.</returns>
        public int EffectiveClassificationBatchOverlap(string subjectId)
        {
            SubjectConcurrencyOverrides? o = GetOverride(subjectId);
            return (o != null && o.ClassificationBatchOverlap.HasValue) ? o.ClassificationBatchOverlap.Value : _Defaults.ClassificationBatchOverlap;
        }

        /// <summary>The effective number of classification batches processed concurrently within a job for a subject.</summary>
        /// <param name="subjectId">The subject id.</param>
        /// <returns>The effective classification batch concurrency.</returns>
        public int EffectiveClassificationBatchConcurrency(string subjectId)
        {
            SubjectConcurrencyOverrides? o = GetOverride(subjectId);
            return (o != null && o.ClassificationBatchConcurrency.HasValue) ? o.ClassificationBatchConcurrency.Value : _Defaults.ClassificationBatchConcurrency;
        }

        /// <summary>The current system-default tuning.</summary>
        /// <returns>The system defaults.</returns>
        public IngestionTuning CurrentDefaults()
        {
            return _Defaults;
        }

        /// <summary>Apply new system defaults, swapping in a fresh limiter at the new cap for every default stage and the job pool.</summary>
        /// <param name="tuning">The new system defaults.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tuning"/> is null.</exception>
        public void ApplySystemDefaults(IngestionTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            _Defaults = tuning;
            foreach (IngestionStageEnum stage in _GatedStages)
            {
                _DefaultGates[stage] = new AsyncSemaphoreGate(DefaultCapFor(stage, tuning));
            }
            _JobPool = new AsyncSemaphoreGate(tuning.MaxConcurrentTasks);
        }

        /// <summary>Apply (create/update) a subject's overrides, swapping in a fresh dedicated limiter at each overridden cap. An empty set clears the overrides.</summary>
        /// <param name="subjectId">The subject id.</param>
        /// <param name="overrides">The subject's overrides.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="subjectId"/> is null or empty.</exception>
        public void ApplySubjectOverride(string subjectId, SubjectConcurrencyOverrides overrides)
        {
            if (String.IsNullOrEmpty(subjectId)) throw new ArgumentNullException(nameof(subjectId));
            if (overrides == null || overrides.IsEmpty()) { ClearSubjectOverride(subjectId); return; }

            _Overrides[subjectId] = overrides;
            ConcurrentDictionary<IngestionStageEnum, AsyncSemaphoreGate> map = _SubjectGates.GetOrAdd(subjectId, key => new ConcurrentDictionary<IngestionStageEnum, AsyncSemaphoreGate>());
            foreach (IngestionStageEnum stage in _GatedStages)
            {
                int? cap = OverrideCapFor(stage, overrides);
                if (cap.HasValue) map[stage] = new AsyncSemaphoreGate(cap.Value);
                else map.TryRemove(stage, out AsyncSemaphoreGate? _);
            }
        }

        /// <summary>Remove a subject's overrides so it reverts to the system defaults.</summary>
        /// <param name="subjectId">The subject id.</param>
        public void ClearSubjectOverride(string subjectId)
        {
            if (String.IsNullOrEmpty(subjectId)) return;
            _Overrides.TryRemove(subjectId, out SubjectConcurrencyOverrides? _);
            _SubjectGates.TryRemove(subjectId, out ConcurrentDictionary<IngestionStageEnum, AsyncSemaphoreGate>? _);
        }

        #endregion

        #region Private-Methods

        private SubjectConcurrencyOverrides? GetOverride(string subjectId)
        {
            if (String.IsNullOrEmpty(subjectId)) return null;
            return _Overrides.TryGetValue(subjectId, out SubjectConcurrencyOverrides? o) ? o : null;
        }

        private AsyncSemaphoreGate? ResolveStageGate(IngestionStageEnum stage, string subjectId)
        {
            if (Array.IndexOf(_GatedStages, stage) < 0) return null;
            if (!String.IsNullOrEmpty(subjectId)
                && _SubjectGates.TryGetValue(subjectId, out ConcurrentDictionary<IngestionStageEnum, AsyncSemaphoreGate>? map)
                && map != null
                && map.TryGetValue(stage, out AsyncSemaphoreGate? subjectGate)
                && subjectGate != null)
            {
                return subjectGate;
            }
            return _DefaultGates.TryGetValue(stage, out AsyncSemaphoreGate? defaultGate) ? defaultGate : null;
        }

        private static int DefaultCapFor(IngestionStageEnum stage, IngestionTuning t)
        {
            switch (stage)
            {
                case IngestionStageEnum.ContentRetrieval: return t.ContentRetrieval;
                case IngestionStageEnum.TypeDetection: return t.TypeDetection;
                case IngestionStageEnum.CellExtraction: return t.CellExtraction;
                case IngestionStageEnum.Classification: return t.Classification;
                case IngestionStageEnum.GraphMerge: return t.GraphMerge;
                case IngestionStageEnum.RelationshipConsolidation: return t.GraphMerge;
                case IngestionStageEnum.Summarization: return t.Summarization;
                case IngestionStageEnum.Chunking: return t.Chunking;
                case IngestionStageEnum.Embedding: return t.Embedding;
                case IngestionStageEnum.Indexing: return t.Indexing;
                default: return 1;
            }
        }

        private static int? OverrideCapFor(IngestionStageEnum stage, SubjectConcurrencyOverrides o)
        {
            switch (stage)
            {
                case IngestionStageEnum.ContentRetrieval: return o.ContentRetrieval;
                case IngestionStageEnum.TypeDetection: return o.TypeDetection;
                case IngestionStageEnum.CellExtraction: return o.CellExtraction;
                case IngestionStageEnum.Classification: return o.Classification;
                case IngestionStageEnum.GraphMerge: return o.GraphMerge;
                case IngestionStageEnum.RelationshipConsolidation: return o.GraphMerge;
                case IngestionStageEnum.Summarization: return o.Summarization;
                case IngestionStageEnum.Chunking: return o.Chunking;
                case IngestionStageEnum.Embedding: return o.Embedding;
                case IngestionStageEnum.Indexing: return o.Indexing;
                default: return null;
            }
        }

        #endregion

        #region Nested-Types

        private sealed class NoopScope : IDisposable
        {
            public static readonly NoopScope Instance = new NoopScope();

            public void Dispose()
            {
            }
        }

        #endregion
    }
}
