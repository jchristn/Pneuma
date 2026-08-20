namespace Pneuma.Core.Database.SqlServer.Implementations
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

    /// <summary>SQL Server chat feedback methods.</summary>
    internal class ChatFeedbackMethods : SqlServerMethodsBase, IChatFeedbackMethods
    {
        internal ChatFeedbackMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<ChatFeedback> CreateAsync(ChatFeedback feedback, CancellationToken token = default)
        {
            if (feedback == null) throw new ArgumentNullException(nameof(feedback));
            feedback.CreatedUtc = DateTime.UtcNow;

            string sql =
                "INSERT INTO chatfeedback (id, tenantid, turnid, subjectid, userid, rating, comment, createdutc) VALUES (" +
                Sanitizer.Str(feedback.Id) + ", " + Sanitizer.Str(feedback.TenantId) + ", " +
                Sanitizer.Str(feedback.TurnId) + ", " + Sanitizer.Str(feedback.SubjectId) + ", " +
                Sanitizer.Str(feedback.UserId) + ", " + Sanitizer.Str(feedback.Rating.ToString()) + ", " +
                Sanitizer.Str(feedback.Comment) + ", " + Sanitizer.Ts(feedback.CreatedUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return feedback;
        }

        /// <inheritdoc />
        public async Task<ChatFeedback?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM chatfeedback WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";",
                token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<ChatFeedback>> EnumerateAsync(string tenantId, string? subjectId, CancellationToken token = default)
        {
            string where = "tenantid = " + Sanitizer.Str(tenantId);
            if (!String.IsNullOrEmpty(subjectId)) where += " AND subjectid = " + Sanitizer.Str(subjectId);
            DataTable table = await Query(
                "SELECT * FROM chatfeedback WHERE " + where + " ORDER BY createdutc DESC;", token).ConfigureAwait(false);
            List<ChatFeedback> result = new List<ChatFeedback>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task DeleteBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            await Query(DeleteBySubjectSql(tenantId, subjectId), token).ConfigureAwait(false);
        }

        internal static string DeleteBySubjectSql(string tenantId, string subjectId)
        {
            return "DELETE FROM chatfeedback WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) + ";";
        }

        internal static ChatFeedback Map(DataRow row)
        {
            return new ChatFeedback
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                TurnId = RowReader.GetString(row, "turnid"),
                SubjectId = RowReader.GetNullableString(row, "subjectid"),
                UserId = RowReader.GetNullableString(row, "userid"),
                Rating = RowReader.GetEnum<FeedbackRatingEnum>(row, "rating", FeedbackRatingEnum.None),
                Comment = RowReader.GetNullableString(row, "comment"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
