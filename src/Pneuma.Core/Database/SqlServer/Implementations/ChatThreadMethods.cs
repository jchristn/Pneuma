namespace Pneuma.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Models;

    /// <summary>SQL Server persisted conversation-thread methods.</summary>
    internal class ChatThreadMethods : SqlServerMethodsBase, IChatThreadMethods
    {
        internal ChatThreadMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<ChatThread> CreateAsync(ChatThread thread, CancellationToken token = default)
        {
            if (thread == null) throw new ArgumentNullException(nameof(thread));
            thread.CreatedUtc = DateTime.UtcNow;
            thread.LastActivityUtc = thread.CreatedUtc;
            string sql =
                "INSERT INTO chatthreads (id, tenantid, subjectid, userid, title, createdutc, lastactivityutc) VALUES (" +
                Sanitizer.Str(thread.Id) + ", " + Sanitizer.Str(thread.TenantId) + ", " + Sanitizer.Str(thread.SubjectId) + ", " +
                Sanitizer.Str(thread.UserId) + ", " + Sanitizer.Str(thread.Title) + ", " + Sanitizer.Ts(thread.CreatedUtc) + ", " + Sanitizer.Ts(thread.LastActivityUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return thread;
        }

        /// <inheritdoc />
        public async Task<ChatThread?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM chatthreads WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<ChatThread>> EnumerateAsync(string tenantId, string? subjectId, CancellationToken token = default)
        {
            string where = "tenantid = " + Sanitizer.Str(tenantId);
            if (!String.IsNullOrEmpty(subjectId)) where += " AND subjectid = " + Sanitizer.Str(subjectId);
            DataTable table = await Query("SELECT * FROM chatthreads WHERE " + where + " ORDER BY lastactivityutc DESC;", token).ConfigureAwait(false);
            List<ChatThread> result = new List<ChatThread>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<ChatThread> UpdateAsync(ChatThread thread, CancellationToken token = default)
        {
            if (thread == null) throw new ArgumentNullException(nameof(thread));
            string sql = "UPDATE chatthreads SET title = " + Sanitizer.Str(thread.Title) + ", lastactivityutc = " + Sanitizer.Ts(thread.LastActivityUtc) +
                " WHERE tenantid = " + Sanitizer.Str(thread.TenantId) + " AND id = " + Sanitizer.Str(thread.Id) + ";";
            await Query(sql, token).ConfigureAwait(false);
            return thread;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            await Query("DELETE FROM chatthreads WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            await Query("DELETE FROM chatthreads WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) + ";", token).ConfigureAwait(false);
        }

        internal static ChatThread Map(DataRow row)
        {
            return new ChatThread
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                SubjectId = RowReader.GetNullableString(row, "subjectid"),
                UserId = RowReader.GetNullableString(row, "userid"),
                Title = RowReader.GetString(row, "title"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastActivityUtc = RowReader.GetDateTime(row, "lastactivityutc")
            };
        }
    }
}
