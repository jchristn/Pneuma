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

    /// <summary>SQLite persisted evaluation run methods.</summary>
    internal class EvalRunMethods : SqliteMethodsBase, IEvalRunMethods
    {
        internal EvalRunMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<EvalRun> CreateAsync(EvalRun run, CancellationToken token = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            run.CreatedUtc = DateTime.UtcNow;
            string sql = "INSERT INTO evalruns (id, tenantid, subjectid, status, category, totalfacts, passcount, partialcount, failcount, judgemodel, error, createdutc, finishedutc) VALUES (" +
                Sanitizer.Str(run.Id) + ", " + Sanitizer.Str(run.TenantId) + ", " + Sanitizer.Str(run.SubjectId) + ", " +
                Sanitizer.Str(run.Status.ToString()) + ", " + Sanitizer.Str(run.Category) + ", " +
                Sanitizer.Num(run.TotalFacts) + ", " + Sanitizer.Num(run.PassCount) + ", " + Sanitizer.Num(run.PartialCount) + ", " + Sanitizer.Num(run.FailCount) + ", " +
                Sanitizer.Str(run.JudgeModel) + ", " + Sanitizer.Str(run.Error) + ", " + Sanitizer.Ts(run.CreatedUtc) + ", " + Sanitizer.Ts(run.FinishedUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return run;
        }

        /// <inheritdoc />
        public async Task<EvalRun?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM evalruns WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<EvalRun>> EnumerateAsync(string tenantId, string? subjectId, CancellationToken token = default)
        {
            string where = "tenantid = " + Sanitizer.Str(tenantId);
            if (!String.IsNullOrEmpty(subjectId)) where += " AND subjectid = " + Sanitizer.Str(subjectId);
            DataTable table = await Query("SELECT * FROM evalruns WHERE " + where + " ORDER BY createdutc DESC;", token).ConfigureAwait(false);
            List<EvalRun> result = new List<EvalRun>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<EvalRun> UpdateAsync(EvalRun run, CancellationToken token = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            string sql = "UPDATE evalruns SET status = " + Sanitizer.Str(run.Status.ToString()) +
                ", totalfacts = " + Sanitizer.Num(run.TotalFacts) + ", passcount = " + Sanitizer.Num(run.PassCount) +
                ", partialcount = " + Sanitizer.Num(run.PartialCount) + ", failcount = " + Sanitizer.Num(run.FailCount) +
                ", judgemodel = " + Sanitizer.Str(run.JudgeModel) + ", error = " + Sanitizer.Str(run.Error) +
                ", finishedutc = " + Sanitizer.Ts(run.FinishedUtc) +
                " WHERE tenantid = " + Sanitizer.Str(run.TenantId) + " AND id = " + Sanitizer.Str(run.Id) + ";";
            await Query(sql, token).ConfigureAwait(false);
            return run;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            await Query("DELETE FROM evalruns WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            await Query("DELETE FROM evalruns WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) + ";", token).ConfigureAwait(false);
        }

        internal static EvalRun Map(DataRow row)
        {
            return new EvalRun
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                SubjectId = RowReader.GetString(row, "subjectid"),
                Status = RowReader.GetEnum<EvalRunStatusEnum>(row, "status", EvalRunStatusEnum.Pending),
                Category = RowReader.GetNullableString(row, "category"),
                TotalFacts = RowReader.GetInt(row, "totalfacts"),
                PassCount = RowReader.GetInt(row, "passcount"),
                PartialCount = RowReader.GetInt(row, "partialcount"),
                FailCount = RowReader.GetInt(row, "failcount"),
                JudgeModel = RowReader.GetNullableString(row, "judgemodel"),
                Error = RowReader.GetNullableString(row, "error"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                FinishedUtc = RowReader.GetNullableDateTime(row, "finishedutc")
            };
        }
    }
}
