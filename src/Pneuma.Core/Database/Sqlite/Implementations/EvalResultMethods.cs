namespace Pneuma.Core.Database.Sqlite.Implementations
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

    /// <summary>SQLite persisted evaluation result methods.</summary>
    internal class EvalResultMethods : SqliteMethodsBase, IEvalResultMethods
    {
        internal EvalResultMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<EvalResult> CreateAsync(EvalResult result, CancellationToken token = default)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            result.CreatedUtc = DateTime.UtcNow;
            string sql = "INSERT INTO evalresults (id, tenantid, runid, factid, question, expectedanswer, producedanswer, verdict, score, reason, failuremode, category, createdutc) VALUES (" +
                Sanitizer.Str(result.Id) + ", " + Sanitizer.Str(result.TenantId) + ", " + Sanitizer.Str(result.RunId) + ", " + Sanitizer.Str(result.FactId) + ", " +
                Sanitizer.Str(result.Question) + ", " + Sanitizer.Str(result.ExpectedAnswer) + ", " + Sanitizer.Str(result.ProducedAnswer) + ", " +
                Sanitizer.Str(result.Verdict.ToString()) + ", " + Sanitizer.Num(result.Score) + ", " + Sanitizer.Str(result.Reason) + ", " +
                Sanitizer.Str(result.FailureMode) + ", " + Sanitizer.Str(result.Category) + ", " + Sanitizer.Ts(result.CreatedUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return result;
        }

        /// <inheritdoc />
        public async Task<List<EvalResult>> EnumerateByRunAsync(string tenantId, string runId, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM evalresults WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND runid = " + Sanitizer.Str(runId) + " ORDER BY createdutc ASC;", token).ConfigureAwait(false);
            List<EvalResult> result = new List<EvalResult>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task DeleteByRunAsync(string tenantId, string runId, CancellationToken token = default)
        {
            await Query("DELETE FROM evalresults WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND runid = " + Sanitizer.Str(runId) + ";", token).ConfigureAwait(false);
        }

        internal static EvalResult Map(DataRow row)
        {
            return new EvalResult
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                RunId = RowReader.GetString(row, "runid"),
                FactId = RowReader.GetString(row, "factid"),
                Question = RowReader.GetString(row, "question"),
                ExpectedAnswer = RowReader.GetString(row, "expectedanswer"),
                ProducedAnswer = RowReader.GetString(row, "producedanswer"),
                Verdict = RowReader.GetEnum<EvalVerdictEnum>(row, "verdict", EvalVerdictEnum.Unknown),
                Score = RowReader.GetDouble(row, "score"),
                Reason = RowReader.GetNullableString(row, "reason"),
                FailureMode = RowReader.GetNullableString(row, "failuremode"),
                Category = RowReader.GetNullableString(row, "category"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
