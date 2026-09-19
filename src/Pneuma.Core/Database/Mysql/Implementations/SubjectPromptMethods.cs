namespace Pneuma.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ingestion.Models;

    /// <summary>MySQL per-subject prompt override methods.</summary>
    internal class SubjectPromptMethods : MysqlMethodsBase, ISubjectPromptMethods
    {
        internal SubjectPromptMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<SubjectPrompt> UpsertAsync(SubjectPrompt prompt, CancellationToken token = default)
        {
            if (prompt == null) throw new ArgumentNullException(nameof(prompt));
            prompt.LastUpdateUtc = DateTime.UtcNow;

            await Query(
                "DELETE FROM subjectprompts WHERE tenantid = " + Sanitizer.Str(prompt.TenantId) +
                " AND subjectid = " + Sanitizer.Str(prompt.SubjectId) +
                " AND promptkey = " + Sanitizer.Str(prompt.PromptKey) + ";",
                token).ConfigureAwait(false);

            string sql =
                "INSERT INTO subjectprompts (id, tenantid, subjectid, promptkey, content, mergemode, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(prompt.Id) + ", " + Sanitizer.Str(prompt.TenantId) + ", " +
                Sanitizer.Str(prompt.SubjectId) + ", " + Sanitizer.Str(prompt.PromptKey) + ", " +
                Sanitizer.Str(prompt.Content) + ", " + Sanitizer.Str(prompt.MergeMode.ToString()) + ", " +
                Sanitizer.Ts(prompt.CreatedUtc) + ", " + Sanitizer.Ts(prompt.LastUpdateUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return prompt;
        }

        /// <inheritdoc />
        public async Task<SubjectPrompt?> ReadAsync(string tenantId, string subjectId, string promptKey, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM subjectprompts WHERE tenantid = " + Sanitizer.Str(tenantId) +
                " AND subjectid = " + Sanitizer.Str(subjectId) +
                " AND promptkey = " + Sanitizer.Str(promptKey) + " LIMIT 1;",
                token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<SubjectPrompt>> EnumerateBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM subjectprompts WHERE tenantid = " + Sanitizer.Str(tenantId) +
                " AND subjectid = " + Sanitizer.Str(subjectId) + " ORDER BY promptkey ASC;",
                token).ConfigureAwait(false);
            List<SubjectPrompt> result = new List<SubjectPrompt>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string tenantId, string subjectId, string promptKey, CancellationToken token = default)
        {
            return DeleteIfExistsAsync(
                "SELECT id FROM subjectprompts WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) + " AND promptkey = " + Sanitizer.Str(promptKey) + ";",
                "DELETE FROM subjectprompts WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) + " AND promptkey = " + Sanitizer.Str(promptKey) + ";",
                token);
        }

        /// <inheritdoc />
        public async Task DeleteBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            await Query(
                "DELETE FROM subjectprompts WHERE tenantid = " + Sanitizer.Str(tenantId) +
                " AND subjectid = " + Sanitizer.Str(subjectId) + ";",
                token).ConfigureAwait(false);
        }

        internal static SubjectPrompt Map(DataRow row)
        {
            return new SubjectPrompt
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                SubjectId = RowReader.GetString(row, "subjectid"),
                PromptKey = RowReader.GetString(row, "promptkey"),
                Content = RowReader.GetNullableString(row, "content"),
                MergeMode = RowReader.GetEnum<PromptMergeModeEnum>(row, "mergemode", PromptMergeModeEnum.Append),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
