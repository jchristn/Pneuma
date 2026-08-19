namespace Pneuma.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;

    /// <summary>SQLite ingestion job methods.</summary>
    internal class IngestionJobMethods : SqliteMethodsBase, IIngestionJobMethods
    {
        internal IngestionJobMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<IngestionJob> CreateAsync(IngestionJob job, CancellationToken token = default)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            job.CreatedUtc = DateTime.UtcNow;
            job.LastUpdateUtc = job.CreatedUtc;

            await Query(InsertSql(job), token).ConfigureAwait(false);
            return job;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteWithEventsAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable existing = await Query(ExistsSql(tenantId, id), token).ConfigureAwait(false);

            List<string> statements = new List<string>
            {
                IngestionJobEventMethods.DeleteByJobSql(tenantId, id),
                DeleteByIdSql(tenantId, id)
            };
            await QueryTransaction(statements, token).ConfigureAwait(false);
            return existing.Rows.Count > 0;
        }

        internal static string InsertSql(IngestionJob job)
        {
            return
                "INSERT INTO ingestionjobs (id, tenantid, subjectid, linkid, sourceurl, status, stage, attemptcount, error, documenttype, blobkey, embeddingendpointid, completionendpointid, graphnodeids, collectionid, startedutc, completedutc, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(job.Id) + ", " + Sanitizer.Str(job.TenantId) + ", " +
                Sanitizer.Str(job.SubjectId) + ", " + Sanitizer.Str(job.LinkId) + ", " +
                Sanitizer.Str(job.SourceUrl) + ", " + Sanitizer.Str(job.Status.ToString()) + ", " +
                Sanitizer.Str(job.Stage.ToString()) + ", " + job.AttemptCount.ToString(CultureInfo.InvariantCulture) + ", " +
                Sanitizer.Str(job.Error) + ", " + Sanitizer.Str(job.DocumentType) + ", " +
                Sanitizer.Str(job.BlobKey) + ", " + Sanitizer.Str(job.EmbeddingEndpointId) + ", " +
                Sanitizer.Str(job.CompletionEndpointId) + ", " + Sanitizer.Str(JsonColumn.FromStrings(job.GraphNodeIds)) + ", " +
                Sanitizer.Str(job.CollectionId) + ", " + Sanitizer.Ts(job.StartedUtc) + ", " +
                Sanitizer.Ts(job.CompletedUtc) + ", " + Sanitizer.Ts(job.CreatedUtc) + ", " +
                Sanitizer.Ts(job.LastUpdateUtc) + ");";
        }

        internal static string ExistsSql(string tenantId, string id)
        {
            return "SELECT id FROM ingestionjobs WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";";
        }

        internal static string DeleteByIdSql(string tenantId, string id)
        {
            return "DELETE FROM ingestionjobs WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";";
        }

        /// <inheritdoc />
        public async Task<IngestionJob?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM ingestionjobs WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";",
                token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<IngestionJob>> EnumerateAsync(string tenantId, IngestionStatusEnum? status, CancellationToken token = default)
        {
            string sql = "SELECT * FROM ingestionjobs WHERE tenantid = " + Sanitizer.Str(tenantId);
            if (status != null) sql += " AND status = " + Sanitizer.Str(status.Value.ToString());
            sql += " ORDER BY createdutc ASC;";

            DataTable table = await Query(sql, token).ConfigureAwait(false);
            List<IngestionJob> result = new List<IngestionJob>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<List<IngestionJob>> EnumerateByLinkAsync(string tenantId, string linkId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM ingestionjobs WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND linkid = " + Sanitizer.Str(linkId) + " ORDER BY createdutc ASC;",
                token).ConfigureAwait(false);
            List<IngestionJob> result = new List<IngestionJob>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<IngestionJob?> ClaimNextQueuedAsync(CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM ingestionjobs WHERE status = 'Queued' ORDER BY createdutc ASC LIMIT 1;",
                token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;

            IngestionJob job = Map(table.Rows[0]);
            job.Status = IngestionStatusEnum.Processing;
            job.Stage = IngestionStageEnum.TypeDetection;
            job.StartedUtc = DateTime.UtcNow;
            job.AttemptCount = job.AttemptCount + 1;
            job.LastUpdateUtc = DateTime.UtcNow;

            string sql =
                "UPDATE ingestionjobs SET status = " + Sanitizer.Str(job.Status.ToString()) +
                ", stage = " + Sanitizer.Str(job.Stage.ToString()) +
                ", startedutc = " + Sanitizer.Ts(job.StartedUtc) +
                ", attemptcount = " + job.AttemptCount.ToString(CultureInfo.InvariantCulture) +
                ", lastupdateutc = " + Sanitizer.Ts(job.LastUpdateUtc) +
                " WHERE id = " + Sanitizer.Str(job.Id) + " AND status = 'Queued';";
            await Query(sql, token).ConfigureAwait(false);
            return job;
        }

        /// <inheritdoc />
        public async Task<IngestionJob> UpdateAsync(IngestionJob job, CancellationToken token = default)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            job.LastUpdateUtc = DateTime.UtcNow;

            string sql =
                "UPDATE ingestionjobs SET subjectid = " + Sanitizer.Str(job.SubjectId) +
                ", linkid = " + Sanitizer.Str(job.LinkId) +
                ", sourceurl = " + Sanitizer.Str(job.SourceUrl) +
                ", status = " + Sanitizer.Str(job.Status.ToString()) +
                ", stage = " + Sanitizer.Str(job.Stage.ToString()) +
                ", attemptcount = " + job.AttemptCount.ToString(CultureInfo.InvariantCulture) +
                ", error = " + Sanitizer.Str(job.Error) +
                ", documenttype = " + Sanitizer.Str(job.DocumentType) +
                ", blobkey = " + Sanitizer.Str(job.BlobKey) +
                ", embeddingendpointid = " + Sanitizer.Str(job.EmbeddingEndpointId) +
                ", completionendpointid = " + Sanitizer.Str(job.CompletionEndpointId) +
                ", graphnodeids = " + Sanitizer.Str(JsonColumn.FromStrings(job.GraphNodeIds)) +
                ", collectionid = " + Sanitizer.Str(job.CollectionId) +
                ", startedutc = " + Sanitizer.Ts(job.StartedUtc) +
                ", completedutc = " + Sanitizer.Ts(job.CompletedUtc) +
                ", lastupdateutc = " + Sanitizer.Ts(job.LastUpdateUtc) +
                " WHERE tenantid = " + Sanitizer.Str(job.TenantId) + " AND id = " + Sanitizer.Str(job.Id) + ";";
            await Query(sql, token).ConfigureAwait(false);
            return job;
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            return DeleteIfExistsAsync(ExistsSql(tenantId, id), DeleteByIdSql(tenantId, id), token);
        }

        internal static IngestionJob Map(DataRow row)
        {
            return new IngestionJob
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                SubjectId = RowReader.GetString(row, "subjectid"),
                LinkId = RowReader.GetString(row, "linkid"),
                SourceUrl = RowReader.GetString(row, "sourceurl"),
                Status = RowReader.GetEnum<IngestionStatusEnum>(row, "status", IngestionStatusEnum.Queued),
                Stage = RowReader.GetEnum<IngestionStageEnum>(row, "stage", IngestionStageEnum.Pending),
                AttemptCount = RowReader.GetInt(row, "attemptcount"),
                Error = RowReader.GetNullableString(row, "error"),
                DocumentType = RowReader.GetNullableString(row, "documenttype"),
                BlobKey = RowReader.GetNullableString(row, "blobkey"),
                EmbeddingEndpointId = RowReader.GetNullableString(row, "embeddingendpointid"),
                CompletionEndpointId = RowReader.GetNullableString(row, "completionendpointid"),
                GraphNodeIds = RowReader.GetStringList(row, "graphnodeids"),
                CollectionId = RowReader.GetNullableString(row, "collectionid"),
                StartedUtc = RowReader.GetNullableDateTime(row, "startedutc"),
                CompletedUtc = RowReader.GetNullableDateTime(row, "completedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
