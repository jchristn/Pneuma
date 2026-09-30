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

    /// <summary>SQL Server subject starter question methods.</summary>
    internal class SubjectQuestionMethods : SqlServerMethodsBase, ISubjectQuestionMethods
    {
        internal SubjectQuestionMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<List<SubjectQuestion>> EnumerateBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM subjectquestions WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) + " ORDER BY position, createdutc;", token).ConfigureAwait(false);
            List<SubjectQuestion> result = new List<SubjectQuestion>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<List<SubjectQuestion>> ReplaceAsync(string tenantId, string subjectId, List<SubjectQuestion> questions, CancellationToken token = default)
        {
            if (questions == null) throw new ArgumentNullException(nameof(questions));
            List<string> statements = new List<string>
            {
                "DELETE FROM subjectquestions WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) + ";"
            };
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < questions.Count; i++)
            {
                SubjectQuestion q = questions[i];
                q.TenantId = tenantId;
                q.SubjectId = subjectId;
                q.Position = i;
                q.CreatedUtc = now;
                statements.Add("INSERT INTO subjectquestions (id, tenantid, subjectid, question, kind, position, origin, createdutc) VALUES (" +
                    Sanitizer.Str(q.Id) + ", " + Sanitizer.Str(tenantId) + ", " + Sanitizer.Str(subjectId) + ", " + Sanitizer.Str(q.Question) + ", " +
                    Sanitizer.Str(q.Kind.ToString()) + ", " + Sanitizer.Num(q.Position) + ", " + Sanitizer.Str(q.Origin.ToString()) + ", " + Sanitizer.Ts(q.CreatedUtc) + ");");
            }
            await QueryTransaction(statements, token).ConfigureAwait(false);
            return questions;
        }

        /// <inheritdoc />
        public async Task DeleteBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            await Query("DELETE FROM subjectquestions WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) + ";", token).ConfigureAwait(false);
        }

        internal static SubjectQuestion Map(DataRow row)
        {
            SubjectQuestionKindEnum kind;
            if (!Enum.TryParse(RowReader.GetString(row, "kind"), true, out kind)) kind = SubjectQuestionKindEnum.Fact;
            SubjectQuestionOriginEnum origin;
            if (!Enum.TryParse(RowReader.GetString(row, "origin"), true, out origin)) origin = SubjectQuestionOriginEnum.User;
            return new SubjectQuestion
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                SubjectId = RowReader.GetString(row, "subjectid"),
                Question = RowReader.GetString(row, "question"),
                Kind = kind,
                Position = RowReader.GetInt(row, "position"),
                Origin = origin,
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
