namespace Pneuma.Core.Database.Postgresql.Implementations
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

    /// <summary>PostgreSQL ingestion job event methods.</summary>
    internal class IngestionJobEventMethods : PostgresqlMethodsBase, IIngestionJobEventMethods
    {
        internal IngestionJobEventMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<IngestionJobEvent> CreateAsync(IngestionJobEvent jobEvent, CancellationToken token = default)
        {
            if (jobEvent == null) throw new ArgumentNullException(nameof(jobEvent));
            jobEvent.CreatedUtc = DateTime.UtcNow;

            string sql =
                "INSERT INTO ingestionjobevents (id, tenantid, jobid, stage, status, message, durationms, queuedurationms, createdutc) VALUES (" +
                Sanitizer.Str(jobEvent.Id) + ", " + Sanitizer.Str(jobEvent.TenantId) + ", " +
                Sanitizer.Str(jobEvent.JobId) + ", " + Sanitizer.Str(jobEvent.Stage.ToString()) + ", " +
                Sanitizer.Str(jobEvent.Status.ToString()) + ", " + Sanitizer.Str(jobEvent.Message) + ", " +
                jobEvent.DurationMs.ToString(CultureInfo.InvariantCulture) + ", " + jobEvent.QueueDurationMs.ToString(CultureInfo.InvariantCulture) + ", " + Sanitizer.Ts(jobEvent.CreatedUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return jobEvent;
        }

        /// <inheritdoc />
        public async Task<IngestionJobEvent> UpdateAsync(IngestionJobEvent jobEvent, CancellationToken token = default)
        {
            if (jobEvent == null) throw new ArgumentNullException(nameof(jobEvent));

            string sql =
                "UPDATE ingestionjobevents SET " +
                "stage = " + Sanitizer.Str(jobEvent.Stage.ToString()) + ", " +
                "status = " + Sanitizer.Str(jobEvent.Status.ToString()) + ", " +
                "message = " + Sanitizer.Str(jobEvent.Message) + ", " +
                "durationms = " + jobEvent.DurationMs.ToString(CultureInfo.InvariantCulture) + ", " +
                "queuedurationms = " + jobEvent.QueueDurationMs.ToString(CultureInfo.InvariantCulture) + " " +
                "WHERE id = " + Sanitizer.Str(jobEvent.Id) + " AND tenantid = " + Sanitizer.Str(jobEvent.TenantId) + ";";
            await Query(sql, token).ConfigureAwait(false);
            return jobEvent;
        }

        /// <inheritdoc />
        public async Task<List<IngestionJobEvent>> EnumerateByJobAsync(string tenantId, string jobId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM ingestionjobevents WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND jobid = " + Sanitizer.Str(jobId) + " ORDER BY createdutc ASC;",
                token).ConfigureAwait(false);
            List<IngestionJobEvent> result = new List<IngestionJobEvent>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task DeleteByJobAsync(string tenantId, string jobId, CancellationToken token = default)
        {
            await Query(DeleteByJobSql(tenantId, jobId), token).ConfigureAwait(false);
        }

        internal static string DeleteByJobSql(string tenantId, string jobId)
        {
            return "DELETE FROM ingestionjobevents WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND jobid = " + Sanitizer.Str(jobId) + ";";
        }

        internal static IngestionJobEvent Map(DataRow row)
        {
            return new IngestionJobEvent
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                JobId = RowReader.GetString(row, "jobid"),
                Stage = RowReader.GetEnum<IngestionStageEnum>(row, "stage", IngestionStageEnum.Pending),
                Status = RowReader.GetEnum<IngestionStatusEnum>(row, "status", IngestionStatusEnum.Processing),
                Message = RowReader.GetNullableString(row, "message"),
                DurationMs = RowReader.GetDouble(row, "durationms"),
                QueueDurationMs = RowReader.GetDouble(row, "queuedurationms"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
