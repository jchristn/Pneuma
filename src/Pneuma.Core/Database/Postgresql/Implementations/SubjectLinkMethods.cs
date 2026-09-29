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
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Models;

    /// <summary>PostgreSQL subject link methods.</summary>
    internal class SubjectLinkMethods : PostgresqlMethodsBase, ISubjectLinkMethods
    {
        internal SubjectLinkMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<SubjectLink> CreateAsync(SubjectLink link, CancellationToken token = default)
        {
            if (link == null) throw new ArgumentNullException(nameof(link));
            link.CreatedUtc = DateTime.UtcNow;
            link.LastUpdateUtc = link.CreatedUtc;

            await Query(InsertSql(link), token).ConfigureAwait(false);
            return link;
        }

        /// <inheritdoc />
        public async Task<SubjectLink> CreateWithJobAsync(SubjectLink link, IngestionJob job, CancellationToken token = default)
        {
            if (link == null) throw new ArgumentNullException(nameof(link));
            if (job == null) throw new ArgumentNullException(nameof(job));

            DateTime now = DateTime.UtcNow;
            link.CreatedUtc = now;
            link.LastUpdateUtc = now;
            job.CreatedUtc = now;
            job.LastUpdateUtc = now;

            List<string> statements = new List<string>
            {
                InsertSql(link),
                IngestionJobMethods.InsertSql(job)
            };
            await QueryTransaction(statements, token).ConfigureAwait(false);
            return link;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteWithJobsAsync(string tenantId, string linkId, IEnumerable<string> jobIds, CancellationToken token = default)
        {
            DataTable existing = await Query(ExistsSql(tenantId, linkId), token).ConfigureAwait(false);

            List<string> statements = new List<string>();
            if (jobIds != null)
            {
                foreach (string jobId in jobIds)
                {
                    if (String.IsNullOrEmpty(jobId)) continue;
                    statements.Add(IngestionJobEventMethods.DeleteByJobSql(tenantId, jobId));
                    statements.Add(IngestionJobAttemptMethods.DeleteByJobSql(tenantId, jobId));
                    statements.Add(IngestionJobMethods.DeleteByIdSql(tenantId, jobId));
                }
            }
            statements.Add(DeleteByIdSql(tenantId, linkId));

            await QueryTransaction(statements, token).ConfigureAwait(false);
            return existing.Rows.Count > 0;
        }

        internal static string InsertSql(SubjectLink link)
        {
            return
                "INSERT INTO subjectlinks (id, tenantid, subjectid, url, title, labelsjson, tagsjson, submittedbyuserid, status, lastingestedutc, lasterror, contenthash, active, isprotected, deletionstatus, sourcekind, externalkey, contenttype, sizebytes, crawlplanid, refreshintervalminutes, nextrefreshutc, lastrefreshutc, refreshfailures, sourceetag, sourcelastmodifiedutc, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(link.Id) + ", " + Sanitizer.Str(link.TenantId) + ", " +
                Sanitizer.Str(link.SubjectId) + ", " + Sanitizer.Str(link.Url) + ", " +
                Sanitizer.Str(link.Title) + ", " + Sanitizer.Str(JsonColumn.FromStrings(link.Labels)) + ", " +
                Sanitizer.Str(JsonColumn.FromDictionary(link.Tags)) + ", " + Sanitizer.Str(link.SubmittedByUserId) + ", " +
                Sanitizer.Str(link.Status.ToString()) + ", " + Sanitizer.Ts(link.LastIngestedUtc) + ", " +
                Sanitizer.Str(link.LastError) + ", " + Sanitizer.Str(link.ContentHash) + ", " + Sanitizer.Bit(link.Active) + ", " +
                Sanitizer.Bit(link.IsProtected) + ", " + Sanitizer.Str(link.DeletionStatus.ToString()) + ", " +
                Sanitizer.Str(link.SourceKind.ToString()) + ", " + Sanitizer.Str(link.ExternalKey) + ", " + Sanitizer.Str(link.ContentType) + ", " +
                link.SizeBytes.ToString(CultureInfo.InvariantCulture) + ", " + Sanitizer.Str(link.CrawlPlanId) + ", " +
                NullableInt(link.RefreshIntervalMinutes) + ", " + Sanitizer.Ts(link.NextRefreshUtc) + ", " + Sanitizer.Ts(link.LastRefreshUtc) + ", " +
                Sanitizer.Num(link.RefreshFailures) + ", " + Sanitizer.Str(link.SourceETag) + ", " + Sanitizer.Ts(link.SourceLastModifiedUtc) + ", " +
                Sanitizer.Ts(link.CreatedUtc) + ", " +
                Sanitizer.Ts(link.LastUpdateUtc) + ");";
        }

        /// <inheritdoc />
        public async Task<SubjectLink?> ReadByExternalKeyAsync(string tenantId, string subjectId, string externalKey, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM subjectlinks WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) +
                " AND externalkey = " + Sanitizer.Str(externalKey) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<SubjectLink>> EnumerateByCrawlPlanAsync(string tenantId, string crawlPlanId, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM subjectlinks WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND crawlplanid = " + Sanitizer.Str(crawlPlanId) + ";", token).ConfigureAwait(false);
            List<SubjectLink> result = new List<SubjectLink>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        internal static string ExistsSql(string tenantId, string id)
        {
            return "SELECT id FROM subjectlinks WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";";
        }

        internal static string DeleteByIdSql(string tenantId, string id)
        {
            return "DELETE FROM subjectlinks WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";";
        }

        /// <inheritdoc />
        public async Task<SubjectLink?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM subjectlinks WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";",
                token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<SubjectLink>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM subjectlinks WHERE tenantid = " + Sanitizer.Str(tenantId) + " ORDER BY createdutc ASC;",
                token).ConfigureAwait(false);
            List<SubjectLink> result = new List<SubjectLink>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<List<SubjectLink>> EnumerateBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM subjectlinks WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) + " ORDER BY createdutc ASC;",
                token).ConfigureAwait(false);
            List<SubjectLink> result = new List<SubjectLink>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<SubjectLink> UpdateAsync(SubjectLink link, CancellationToken token = default)
        {
            if (link == null) throw new ArgumentNullException(nameof(link));
            link.LastUpdateUtc = DateTime.UtcNow;

            string sql =
                "UPDATE subjectlinks SET subjectid = " + Sanitizer.Str(link.SubjectId) +
                ", url = " + Sanitizer.Str(link.Url) +
                ", title = " + Sanitizer.Str(link.Title) +
                ", labelsjson = " + Sanitizer.Str(JsonColumn.FromStrings(link.Labels)) +
                ", tagsjson = " + Sanitizer.Str(JsonColumn.FromDictionary(link.Tags)) +
                ", submittedbyuserid = " + Sanitizer.Str(link.SubmittedByUserId) +
                ", status = " + Sanitizer.Str(link.Status.ToString()) +
                ", lastingestedutc = " + Sanitizer.Ts(link.LastIngestedUtc) +
                ", lasterror = " + Sanitizer.Str(link.LastError) +
                ", failurecategory = " + Sanitizer.Str(link.FailureCategory?.ToString()) +
                ", warningcount = " + link.WarningCount.ToString(CultureInfo.InvariantCulture) +
                ", currentjobid = " + Sanitizer.Str(link.CurrentJobId) +
                ", sourcekind = " + Sanitizer.Str(link.SourceKind.ToString()) +
                ", externalkey = " + Sanitizer.Str(link.ExternalKey) +
                ", contenttype = " + Sanitizer.Str(link.ContentType) +
                ", sizebytes = " + link.SizeBytes.ToString(CultureInfo.InvariantCulture) +
                ", crawlplanid = " + Sanitizer.Str(link.CrawlPlanId) +
                ", refreshintervalminutes = " + NullableInt(link.RefreshIntervalMinutes) +
                ", nextrefreshutc = " + Sanitizer.Ts(link.NextRefreshUtc) +
                ", lastrefreshutc = " + Sanitizer.Ts(link.LastRefreshUtc) +
                ", refreshfailures = " + Sanitizer.Num(link.RefreshFailures) +
                ", sourceetag = " + Sanitizer.Str(link.SourceETag) +
                ", sourcelastmodifiedutc = " + Sanitizer.Ts(link.SourceLastModifiedUtc) +
                ", contenthash = " + Sanitizer.Str(link.ContentHash) +
                ", active = " + Sanitizer.Bit(link.Active) +
                ", isprotected = " + Sanitizer.Bit(link.IsProtected) +
                ", deletionstatus = " + Sanitizer.Str(link.DeletionStatus.ToString()) +
                ", lastupdateutc = " + Sanitizer.Ts(link.LastUpdateUtc) +
                " WHERE tenantid = " + Sanitizer.Str(link.TenantId) + " AND id = " + Sanitizer.Str(link.Id) + ";";
            await Query(sql, token).ConfigureAwait(false);
            return link;
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            return DeleteIfExistsAsync(ExistsSql(tenantId, id), DeleteByIdSql(tenantId, id), token);
        }

        internal static SubjectLink Map(DataRow row)
        {
            return new SubjectLink
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                SubjectId = RowReader.GetString(row, "subjectid"),
                Url = RowReader.GetString(row, "url"),
                Title = RowReader.GetNullableString(row, "title"),
                Labels = RowReader.GetStringList(row, "labelsjson"),
                Tags = RowReader.GetStringDictionary(row, "tagsjson"),
                SubmittedByUserId = RowReader.GetNullableString(row, "submittedbyuserid"),
                Status = RowReader.GetEnum<SubjectLinkStatusEnum>(row, "status", SubjectLinkStatusEnum.Submitted),
                LastIngestedUtc = RowReader.GetNullableDateTime(row, "lastingestedutc"),
                LastError = RowReader.GetNullableString(row, "lasterror"),
                FailureCategory = RowReader.GetNullableEnum<IngestionFailureCategoryEnum>(row, "failurecategory"),
                WarningCount = RowReader.GetInt(row, "warningcount"),
                CurrentJobId = RowReader.GetNullableString(row, "currentjobid"),
                SourceKind = RowReader.GetEnum<SourceKindEnum>(row, "sourcekind", SourceKindEnum.Url),
                ExternalKey = RowReader.GetNullableString(row, "externalkey"),
                ContentType = RowReader.GetNullableString(row, "contenttype"),
                SizeBytes = RowReader.GetLong(row, "sizebytes"),
                CrawlPlanId = RowReader.GetNullableString(row, "crawlplanid"),
                RefreshIntervalMinutes = NullableIntOf(row, "refreshintervalminutes"),
                NextRefreshUtc = RowReader.GetNullableDateTime(row, "nextrefreshutc"),
                LastRefreshUtc = RowReader.GetNullableDateTime(row, "lastrefreshutc"),
                RefreshFailures = RowReader.GetInt(row, "refreshfailures"),
                SourceETag = RowReader.GetNullableString(row, "sourceetag"),
                SourceLastModifiedUtc = RowReader.GetNullableDateTime(row, "sourcelastmodifiedutc"),
                ContentHash = RowReader.GetNullableString(row, "contenthash"),
                Active = RowReader.GetBool(row, "active"),
                IsProtected = RowReader.GetBool(row, "isprotected"),
                DeletionStatus = RowReader.GetEnum<LinkDeletionStatusEnum>(row, "deletionstatus", LinkDeletionStatusEnum.None),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }

        /// <inheritdoc />
        public async Task<List<SubjectLink>> EnumeratePendingDeletionAsync(CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM subjectlinks WHERE deletionstatus IN ('Pending', 'Deleting') ORDER BY createdutc ASC;",
                token).ConfigureAwait(false);
            List<SubjectLink> result = new List<SubjectLink>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }
            /// <inheritdoc />
        public async Task<List<SubjectLink>> EnumerateDueForRefreshAsync(DateTime nowUtc, int maxResults, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM subjectlinks WHERE nextrefreshutc IS NOT NULL AND nextrefreshutc <= " + Sanitizer.Ts(nowUtc) +
                " AND active = 1 AND deletionstatus = 'None' AND sourcekind = 'Url' AND crawlplanid IS NULL ORDER BY nextrefreshutc ASC;", token).ConfigureAwait(false);
            List<SubjectLink> result = new List<SubjectLink>();
            foreach (DataRow row in table.Rows)
            {
                if (result.Count >= Math.Max(1, maxResults)) break;
                result.Add(Map(row));
            }
            return result;
        }

        /// <inheritdoc />
        public async Task<bool> TryClaimRefreshAsync(string tenantId, string id, DateTime? expectedNextUtc, DateTime claimUntilUtc, CancellationToken token = default)
        {
            string t = Sanitizer.Str(tenantId);
            string l = Sanitizer.Str(id);
            string expected = expectedNextUtc == null ? "nextrefreshutc IS NULL" : "nextrefreshutc = " + Sanitizer.Ts(expectedNextUtc);
            await Query("UPDATE subjectlinks SET nextrefreshutc = " + Sanitizer.Ts(claimUntilUtc) + " WHERE tenantid = " + t + " AND id = " + l + " AND " + expected + ";", token).ConfigureAwait(false);
            DataTable table = await Query("SELECT nextrefreshutc FROM subjectlinks WHERE tenantid = " + t + " AND id = " + l + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return false;
            DateTime? current = RowReader.GetNullableDateTime(table.Rows[0], "nextrefreshutc");
            return current != null && Math.Abs((current.Value - claimUntilUtc.ToUniversalTime()).TotalMilliseconds) < 1;
        }

        /// <inheritdoc />
        public async Task UpdateRefreshStateAsync(SubjectLink link, CancellationToken token = default)
        {
            if (link == null) throw new ArgumentNullException(nameof(link));
            await Query(
                "UPDATE subjectlinks SET refreshintervalminutes = " + NullableInt(link.RefreshIntervalMinutes) +
                ", nextrefreshutc = " + Sanitizer.Ts(link.NextRefreshUtc) +
                ", lastrefreshutc = " + Sanitizer.Ts(link.LastRefreshUtc) +
                ", refreshfailures = " + Sanitizer.Num(link.RefreshFailures) +
                ", sourceetag = " + Sanitizer.Str(link.SourceETag) +
                ", sourcelastmodifiedutc = " + Sanitizer.Ts(link.SourceLastModifiedUtc) +
                " WHERE tenantid = " + Sanitizer.Str(link.TenantId) + " AND id = " + Sanitizer.Str(link.Id) + ";", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task SetInheritedNextRefreshAsync(string tenantId, string subjectId, DateTime? nextRefreshUtc, CancellationToken token = default)
        {
            await Query(
                "UPDATE subjectlinks SET nextrefreshutc = " + Sanitizer.Ts(nextRefreshUtc) +
                " WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) +
                " AND refreshintervalminutes IS NULL AND sourcekind = 'Url' AND crawlplanid IS NULL;", token).ConfigureAwait(false);
        }

        private static string NullableInt(int? value)
        {
            return value == null ? "NULL" : value.Value.ToString(CultureInfo.InvariantCulture);
        }

        private static int? NullableIntOf(DataRow row, string column)
        {
            object value = row[column];
            if (value == null || value == DBNull.Value) return null;
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
    }
}
