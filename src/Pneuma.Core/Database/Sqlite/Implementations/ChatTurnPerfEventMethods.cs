namespace Pneuma.Core.Database.Sqlite.Implementations
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

    /// <summary>SQLite persisted chat-turn performance-event methods.</summary>
    internal class ChatTurnPerfEventMethods : SqliteMethodsBase, IChatTurnPerfEventMethods
    {
        internal ChatTurnPerfEventMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task CreateManyAsync(List<ChatTurnPerfEvent> events, CancellationToken token = default)
        {
            if (events == null || events.Count == 0) return;
            StringBuilder builder = new StringBuilder();
            builder.Append("INSERT INTO chatturnperfevents (id, tenantid, turnid, subjectid, stage, kind, provider, model, durationms, timetofirsttokenms, prompttokens, completiontokens, success, createdutc) VALUES ");
            for (int i = 0; i < events.Count; i++)
            {
                ChatTurnPerfEvent e = events[i];
                if (i > 0) builder.Append(", ");
                builder.Append("(" +
                    Sanitizer.Str(e.Id) + ", " + Sanitizer.Str(e.TenantId) + ", " + Sanitizer.Str(e.TurnId) + ", " +
                    Sanitizer.Str(e.SubjectId) + ", " + Sanitizer.Str(e.Stage) + ", " + Sanitizer.Str(e.Kind) + ", " +
                    Sanitizer.Str(e.Provider) + ", " + Sanitizer.Str(e.Model) + ", " +
                    Sanitizer.Num(e.DurationMs) + ", " + Sanitizer.Num(e.TimeToFirstTokenMs) + ", " +
                    Sanitizer.Num(e.PromptTokens) + ", " + Sanitizer.Num(e.CompletionTokens) + ", " +
                    Sanitizer.Bit(e.Success) + ", " + Sanitizer.Ts(e.CreatedUtc) + ")");
            }
            builder.Append(";");
            await Query(builder.ToString(), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ChatTurnPerfEvent>> EnumerateByTurnAsync(string tenantId, string turnId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM chatturnperfevents WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND turnid = " + Sanitizer.Str(turnId) + " ORDER BY createdutc ASC;",
                token).ConfigureAwait(false);
            List<ChatTurnPerfEvent> result = new List<ChatTurnPerfEvent>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<List<ChatTurnPerfEvent>> EnumerateBySubjectAsync(string tenantId, string? subjectId, DateTime sinceUtc, CancellationToken token = default)
        {
            string where = "tenantid = " + Sanitizer.Str(tenantId) + " AND createdutc >= " + Sanitizer.Ts(sinceUtc);
            if (!String.IsNullOrEmpty(subjectId)) where += " AND subjectid = " + Sanitizer.Str(subjectId);
            DataTable table = await Query("SELECT * FROM chatturnperfevents WHERE " + where + " ORDER BY createdutc DESC;", token).ConfigureAwait(false);
            List<ChatTurnPerfEvent> result = new List<ChatTurnPerfEvent>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task DeleteBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            await Query("DELETE FROM chatturnperfevents WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) + ";", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteOlderThanAsync(string tenantId, string subjectId, DateTime cutoffUtc, CancellationToken token = default)
        {
            await Query("DELETE FROM chatturnperfevents WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) + " AND createdutc < " + Sanitizer.Ts(cutoffUtc) + ";", token).ConfigureAwait(false);
        }

        internal static ChatTurnPerfEvent Map(DataRow row)
        {
            return new ChatTurnPerfEvent
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                TurnId = RowReader.GetString(row, "turnid"),
                SubjectId = RowReader.GetNullableString(row, "subjectid"),
                Stage = RowReader.GetString(row, "stage"),
                Kind = RowReader.GetString(row, "kind"),
                Provider = RowReader.GetNullableString(row, "provider"),
                Model = RowReader.GetNullableString(row, "model"),
                DurationMs = RowReader.GetDouble(row, "durationms"),
                TimeToFirstTokenMs = RowReader.GetDouble(row, "timetofirsttokenms"),
                PromptTokens = RowReader.GetInt(row, "prompttokens"),
                CompletionTokens = RowReader.GetInt(row, "completiontokens"),
                Success = RowReader.GetBool(row, "success"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
