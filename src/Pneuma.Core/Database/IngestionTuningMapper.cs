namespace Pneuma.Core.Database
{
    using System.Data;
    using Pneuma.Core.Models;

    /// <summary>
    /// Maps a database row to an <see cref="IngestionTuning"/>. Shared across providers (uses the
    /// provider-neutral <see cref="RowReader"/>); the write/read SQL differs per provider and stays local.
    /// </summary>
    public static class IngestionTuningMapper
    {
        /// <summary>Map a row to an ingestion-tuning record.</summary>
        /// <param name="row">The data row.</param>
        /// <returns>The mapped tuning record.</returns>
        public static IngestionTuning Map(DataRow row)
        {
            return new IngestionTuning
            {
                Id = RowReader.GetString(row, "id"),
                ContentRetrieval = RowReader.GetInt(row, "contentretrieval"),
                TypeDetection = RowReader.GetInt(row, "typedetection"),
                CellExtraction = RowReader.GetInt(row, "cellextraction"),
                Classification = RowReader.GetInt(row, "classification"),
                GraphMerge = RowReader.GetInt(row, "graphmerge"),
                Summarization = RowReader.GetInt(row, "summarization"),
                Chunking = RowReader.GetInt(row, "chunking"),
                Embedding = RowReader.GetInt(row, "embedding"),
                Indexing = RowReader.GetInt(row, "indexing"),
                MaxConcurrentTasks = RowReader.GetInt(row, "maxconcurrenttasks"),
                SummarizationConcurrency = RowReader.GetInt(row, "summarizationconcurrency"),
                SummarizationMinCellLength = RowReader.GetInt(row, "summarizationmincelllength"),
                ClassificationBatchSize = RowReader.GetInt(row, "classificationbatchsize"),
                ClassificationBatchOverlap = RowReader.GetInt(row, "classificationbatchoverlap"),
                ClassificationBatchConcurrency = RowReader.GetInt(row, "classificationbatchconcurrency"),
                StageTimeoutSeconds = RowReader.GetInt(row, "stagetimeoutseconds"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
