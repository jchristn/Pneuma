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

    /// <summary>SQL Server persisted chat-turn methods.</summary>
    internal class ChatTurnMethods : SqlServerMethodsBase, IChatTurnMethods
    {
        internal ChatTurnMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<ChatTurnRecord> CreateAsync(ChatTurnRecord turn, CancellationToken token = default)
        {
            if (turn == null) throw new ArgumentNullException(nameof(turn));
            turn.CreatedUtc = DateTime.UtcNow;

            string sql =
                "INSERT INTO chatturns (id, tenantid, subjectid, userid, question, answer, thinking, model, prompttokens, completiontokens, totaltokens, timetofirsttokenms, generationms, thinkingms, contextsize, citationsjson, createdutc) VALUES (" +
                Sanitizer.Str(turn.Id) + ", " + Sanitizer.Str(turn.TenantId) + ", " +
                Sanitizer.Str(turn.SubjectId) + ", " + Sanitizer.Str(turn.UserId) + ", " +
                Sanitizer.Str(turn.Question) + ", " + Sanitizer.Str(turn.Answer) + ", " +
                Sanitizer.Str(turn.Thinking) + ", " + Sanitizer.Str(turn.Model) + ", " +
                Sanitizer.Num(turn.PromptTokens) + ", " + Sanitizer.Num(turn.CompletionTokens) + ", " +
                Sanitizer.Num(turn.TotalTokens) + ", " + Sanitizer.Num(turn.TimeToFirstTokenMs) + ", " +
                Sanitizer.Num(turn.GenerationMs) + ", " + Sanitizer.Num(turn.ThinkingMs) + ", " +
                Sanitizer.Num(turn.ContextSize) + ", " + Sanitizer.Str(turn.CitationsJson) + ", " +
                Sanitizer.Ts(turn.CreatedUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return turn;
        }

        /// <inheritdoc />
        public async Task<ChatTurnRecord?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM chatturns WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";",
                token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<ChatTurnRecord>> EnumerateAsync(string tenantId, string? subjectId, CancellationToken token = default)
        {
            string where = "tenantid = " + Sanitizer.Str(tenantId);
            if (!String.IsNullOrEmpty(subjectId)) where += " AND subjectid = " + Sanitizer.Str(subjectId);
            DataTable table = await Query(
                "SELECT * FROM chatturns WHERE " + where + " ORDER BY createdutc DESC;", token).ConfigureAwait(false);
            List<ChatTurnRecord> result = new List<ChatTurnRecord>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task DeleteBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            await Query(DeleteBySubjectSql(tenantId, subjectId), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteOlderThanAsync(string tenantId, string subjectId, DateTime cutoffUtc, CancellationToken token = default)
        {
            await Query(
                "DELETE FROM chatturns WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) +
                " AND createdutc < " + Sanitizer.Ts(cutoffUtc) + ";", token).ConfigureAwait(false);
        }

        internal static string DeleteBySubjectSql(string tenantId, string subjectId)
        {
            return "DELETE FROM chatturns WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) + ";";
        }

        internal static ChatTurnRecord Map(DataRow row)
        {
            return new ChatTurnRecord
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                SubjectId = RowReader.GetNullableString(row, "subjectid"),
                UserId = RowReader.GetNullableString(row, "userid"),
                Question = RowReader.GetString(row, "question"),
                Answer = RowReader.GetString(row, "answer"),
                Thinking = RowReader.GetNullableString(row, "thinking"),
                Model = RowReader.GetNullableString(row, "model"),
                PromptTokens = RowReader.GetInt(row, "prompttokens"),
                CompletionTokens = RowReader.GetInt(row, "completiontokens"),
                TotalTokens = RowReader.GetInt(row, "totaltokens"),
                TimeToFirstTokenMs = RowReader.GetDouble(row, "timetofirsttokenms"),
                GenerationMs = RowReader.GetDouble(row, "generationms"),
                ThinkingMs = RowReader.GetDouble(row, "thinkingms"),
                ContextSize = RowReader.GetInt(row, "contextsize"),
                CitationsJson = RowReader.GetNullableString(row, "citationsjson"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
