namespace Pneuma.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Models;

    /// <summary>PostgreSQL persisted chat tool-call methods.</summary>
    internal class ChatToolCallMethods : PostgresqlMethodsBase, IChatToolCallMethods
    {
        internal ChatToolCallMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task CreateManyAsync(List<ChatToolCall> calls, CancellationToken token = default)
        {
            if (calls == null || calls.Count == 0) return;
            StringBuilder builder = new StringBuilder();
            builder.Append("INSERT INTO chattoolcalls (id, tenantid, turnid, subjectid, toolname, argumentsjson, outputjson, success, durationms, sequence, createdutc) VALUES ");
            for (int i = 0; i < calls.Count; i++)
            {
                ChatToolCall c = calls[i];
                if (i > 0) builder.Append(", ");
                builder.Append("(" +
                    Sanitizer.Str(c.Id) + ", " + Sanitizer.Str(c.TenantId) + ", " + Sanitizer.Str(c.TurnId) + ", " +
                    Sanitizer.Str(c.SubjectId) + ", " + Sanitizer.Str(c.ToolName) + ", " + Sanitizer.Str(c.ArgumentsJson) + ", " +
                    Sanitizer.Str(c.OutputJson) + ", " + Sanitizer.Bit(c.Success) + ", " + Sanitizer.Num(c.DurationMs) + ", " +
                    Sanitizer.Num(c.Sequence) + ", " + Sanitizer.Ts(c.CreatedUtc) + ")");
            }
            builder.Append(";");
            await Query(builder.ToString(), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ChatToolCall>> EnumerateByTurnAsync(string tenantId, string turnId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM chattoolcalls WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND turnid = " + Sanitizer.Str(turnId) + " ORDER BY sequence ASC;",
                token).ConfigureAwait(false);
            List<ChatToolCall> result = new List<ChatToolCall>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task DeleteBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            await Query("DELETE FROM chattoolcalls WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) + ";", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteByTurnAsync(string tenantId, string turnId, CancellationToken token = default)
        {
            await Query("DELETE FROM chattoolcalls WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND turnid = " + Sanitizer.Str(turnId) + ";", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteOlderThanAsync(string tenantId, string subjectId, DateTime cutoffUtc, CancellationToken token = default)
        {
            await Query("DELETE FROM chattoolcalls WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) + " AND createdutc < " + Sanitizer.Ts(cutoffUtc) + ";", token).ConfigureAwait(false);
        }

        internal static ChatToolCall Map(DataRow row)
        {
            return new ChatToolCall
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                TurnId = RowReader.GetString(row, "turnid"),
                SubjectId = RowReader.GetNullableString(row, "subjectid"),
                ToolName = RowReader.GetString(row, "toolname"),
                ArgumentsJson = RowReader.GetNullableString(row, "argumentsjson"),
                OutputJson = RowReader.GetNullableString(row, "outputjson"),
                Success = RowReader.GetBool(row, "success"),
                DurationMs = RowReader.GetDouble(row, "durationms"),
                Sequence = RowReader.GetInt(row, "sequence"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
