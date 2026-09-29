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
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;

    /// <summary>SQLite ingestion job attempt methods.</summary>
    internal class IngestionJobAttemptMethods : SqliteMethodsBase, IIngestionJobAttemptMethods
    {
        internal IngestionJobAttemptMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<IngestionJobAttempt> CreateAsync(IngestionJobAttempt attempt, CancellationToken token = default)
        {
            if (attempt == null) throw new ArgumentNullException(nameof(attempt));
            attempt.CreatedUtc = DateTime.UtcNow;
            string sql =
                "INSERT INTO ingestionjobattempts (id, tenantid, jobid, attemptnumber, succeeded, stage, failurecategory, message, startedutc, endedutc, createdutc) VALUES (" +
                Sanitizer.Str(attempt.Id) + ", " + Sanitizer.Str(attempt.TenantId) + ", " + Sanitizer.Str(attempt.JobId) + ", " +
                attempt.AttemptNumber.ToString(CultureInfo.InvariantCulture) + ", " + Sanitizer.Bit(attempt.Succeeded) + ", " +
                Sanitizer.Str(attempt.Stage.ToString()) + ", " + Sanitizer.Str(attempt.FailureCategory?.ToString()) + ", " +
                Sanitizer.Str(attempt.Message) + ", " + Sanitizer.Ts(attempt.StartedUtc) + ", " + Sanitizer.Ts(attempt.EndedUtc) + ", " +
                Sanitizer.Ts(attempt.CreatedUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return attempt;
        }

        /// <inheritdoc />
        public async Task<List<IngestionJobAttempt>> EnumerateByJobAsync(string tenantId, string jobId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM ingestionjobattempts WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND jobid = " + Sanitizer.Str(jobId) +
                " ORDER BY attemptnumber ASC, createdutc ASC;", token).ConfigureAwait(false);
            List<IngestionJobAttempt> result = new List<IngestionJobAttempt>();
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
            return "DELETE FROM ingestionjobattempts WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND jobid = " + Sanitizer.Str(jobId) + ";";
        }

        internal static IngestionJobAttempt Map(DataRow row)
        {
            return new IngestionJobAttempt
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                JobId = RowReader.GetString(row, "jobid"),
                AttemptNumber = RowReader.GetInt(row, "attemptnumber"),
                Succeeded = RowReader.GetBool(row, "succeeded"),
                Stage = RowReader.GetEnum<IngestionStageEnum>(row, "stage", IngestionStageEnum.Pending),
                FailureCategory = RowReader.GetNullableEnum<IngestionFailureCategoryEnum>(row, "failurecategory"),
                Message = RowReader.GetNullableString(row, "message"),
                StartedUtc = RowReader.GetDateTime(row, "startedutc"),
                EndedUtc = RowReader.GetDateTime(row, "endedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
