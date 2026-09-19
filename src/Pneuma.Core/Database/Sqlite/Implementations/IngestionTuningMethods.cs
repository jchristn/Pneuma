namespace Pneuma.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Models;

    /// <summary>SQLite ingestion-tuning (singleton) methods.</summary>
    internal class IngestionTuningMethods : SqliteMethodsBase, IIngestionTuningMethods
    {
        internal IngestionTuningMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<IngestionTuning?> ReadAsync(CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM ingestiontuning WHERE id = " + Sanitizer.Str(IngestionTuning.DefaultId) + " LIMIT 1;", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return IngestionTuningMapper.Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<IngestionTuning> UpsertAsync(IngestionTuning tuning, CancellationToken token = default)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            tuning.Id = IngestionTuning.DefaultId;
            tuning.LastUpdateUtc = DateTime.UtcNow;

            await Query("DELETE FROM ingestiontuning WHERE id = " + Sanitizer.Str(tuning.Id) + ";", token).ConfigureAwait(false);
            string sql =
                "INSERT INTO ingestiontuning (id, contentretrieval, typedetection, cellextraction, classification, graphmerge, summarization, chunking, embedding, indexing, maxconcurrenttasks, summarizationconcurrency, summarizationmincelllength, stagetimeoutseconds, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(tuning.Id) + ", " +
                Sanitizer.Num(tuning.ContentRetrieval) + ", " + Sanitizer.Num(tuning.TypeDetection) + ", " + Sanitizer.Num(tuning.CellExtraction) + ", " +
                Sanitizer.Num(tuning.Classification) + ", " + Sanitizer.Num(tuning.GraphMerge) + ", " + Sanitizer.Num(tuning.Summarization) + ", " +
                Sanitizer.Num(tuning.Chunking) + ", " + Sanitizer.Num(tuning.Embedding) + ", " + Sanitizer.Num(tuning.Indexing) + ", " +
                Sanitizer.Num(tuning.MaxConcurrentTasks) + ", " + Sanitizer.Num(tuning.SummarizationConcurrency) + ", " + Sanitizer.Num(tuning.SummarizationMinCellLength) + ", " +
                Sanitizer.Num(tuning.StageTimeoutSeconds) + ", " + Sanitizer.Ts(tuning.CreatedUtc) + ", " + Sanitizer.Ts(tuning.LastUpdateUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return tuning;
        }
    }
}
