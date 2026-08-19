namespace Pneuma.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Models;

    /// <summary>SQLite subject methods.</summary>
    internal class SubjectMethods : SqliteMethodsBase, ISubjectMethods
    {
        internal SubjectMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<Subject> CreateAsync(Subject subject, CancellationToken token = default)
        {
            if (subject == null) throw new ArgumentNullException(nameof(subject));
            subject.CreatedUtc = DateTime.UtcNow;
            subject.LastUpdateUtc = subject.CreatedUtc;

            string sql =
                "INSERT INTO subjects (id, tenantid, displayname, type, description, graphrootnodeid, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(subject.Id) + ", " + Sanitizer.Str(subject.TenantId) + ", " +
                Sanitizer.Str(subject.DisplayName) + ", " + Sanitizer.Str(subject.Type) + ", " +
                Sanitizer.Str(subject.Description) + ", " + Sanitizer.Str(subject.GraphRootNodeId) + ", " +
                Sanitizer.Bit(subject.Active) + ", " + Sanitizer.Bit(subject.IsProtected) + ", " +
                Sanitizer.Ts(subject.CreatedUtc) + ", " + Sanitizer.Ts(subject.LastUpdateUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return subject;
        }

        /// <inheritdoc />
        public async Task<Subject?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM subjects WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";",
                token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<Subject?> ReadByIdAsync(string id, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM subjects WHERE id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<Subject>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM subjects WHERE tenantid = " + Sanitizer.Str(tenantId) + " ORDER BY createdutc ASC;",
                token).ConfigureAwait(false);
            List<Subject> result = new List<Subject>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<Subject> UpdateAsync(Subject subject, CancellationToken token = default)
        {
            if (subject == null) throw new ArgumentNullException(nameof(subject));
            subject.LastUpdateUtc = DateTime.UtcNow;

            string sql =
                "UPDATE subjects SET displayname = " + Sanitizer.Str(subject.DisplayName) +
                ", type = " + Sanitizer.Str(subject.Type) +
                ", description = " + Sanitizer.Str(subject.Description) +
                ", graphrootnodeid = " + Sanitizer.Str(subject.GraphRootNodeId) +
                ", active = " + Sanitizer.Bit(subject.Active) +
                ", isprotected = " + Sanitizer.Bit(subject.IsProtected) +
                ", lastupdateutc = " + Sanitizer.Ts(subject.LastUpdateUtc) +
                " WHERE tenantid = " + Sanitizer.Str(subject.TenantId) + " AND id = " + Sanitizer.Str(subject.Id) + ";";
            await Query(sql, token).ConfigureAwait(false);
            return subject;
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            return DeleteIfExistsAsync(ExistsSql(tenantId, id), DeleteByIdSql(tenantId, id), token);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteWithSubordinatesAsync(string tenantId, string subjectId, IEnumerable<string> linkIds, IEnumerable<string> jobIds, CancellationToken token = default)
        {
            DataTable existing = await Query(ExistsSql(tenantId, subjectId), token).ConfigureAwait(false);

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
            if (linkIds != null)
            {
                foreach (string linkId in linkIds)
                {
                    if (String.IsNullOrEmpty(linkId)) continue;
                    statements.Add(SubjectLinkMethods.DeleteByIdSql(tenantId, linkId));
                }
            }
            statements.Add(DeleteByIdSql(tenantId, subjectId));

            await QueryTransaction(statements, token).ConfigureAwait(false);
            return existing.Rows.Count > 0;
        }

        internal static string ExistsSql(string tenantId, string id)
        {
            return "SELECT id FROM subjects WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";";
        }

        internal static string DeleteByIdSql(string tenantId, string id)
        {
            return "DELETE FROM subjects WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";";
        }

        internal static Subject Map(DataRow row)
        {
            return new Subject
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                DisplayName = RowReader.GetString(row, "displayname"),
                Type = RowReader.GetString(row, "type"),
                Description = RowReader.GetNullableString(row, "description"),
                GraphRootNodeId = RowReader.GetNullableString(row, "graphrootnodeid"),
                Active = RowReader.GetBool(row, "active"),
                IsProtected = RowReader.GetBool(row, "isprotected"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
