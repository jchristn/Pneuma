namespace Pneuma.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Enums;
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
                "INSERT INTO subjectlinks (id, tenantid, subjectid, url, title, labelsjson, tagsjson, submittedbyuserid, status, lastingestedutc, lasterror, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(link.Id) + ", " + Sanitizer.Str(link.TenantId) + ", " +
                Sanitizer.Str(link.SubjectId) + ", " + Sanitizer.Str(link.Url) + ", " +
                Sanitizer.Str(link.Title) + ", " + Sanitizer.Str(JsonColumn.FromStrings(link.Labels)) + ", " +
                Sanitizer.Str(JsonColumn.FromDictionary(link.Tags)) + ", " + Sanitizer.Str(link.SubmittedByUserId) + ", " +
                Sanitizer.Str(link.Status.ToString()) + ", " + Sanitizer.Ts(link.LastIngestedUtc) + ", " +
                Sanitizer.Str(link.LastError) + ", " + Sanitizer.Bit(link.Active) + ", " +
                Sanitizer.Bit(link.IsProtected) + ", " + Sanitizer.Ts(link.CreatedUtc) + ", " +
                Sanitizer.Ts(link.LastUpdateUtc) + ");";
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
                ", active = " + Sanitizer.Bit(link.Active) +
                ", isprotected = " + Sanitizer.Bit(link.IsProtected) +
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
                Active = RowReader.GetBool(row, "active"),
                IsProtected = RowReader.GetBool(row, "isprotected"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
