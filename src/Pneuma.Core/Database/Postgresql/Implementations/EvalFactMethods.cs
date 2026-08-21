namespace Pneuma.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Models;

    /// <summary>PostgreSQL persisted evaluation fact methods.</summary>
    internal class EvalFactMethods : PostgresqlMethodsBase, IEvalFactMethods
    {
        internal EvalFactMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<EvalFact> CreateAsync(EvalFact fact, CancellationToken token = default)
        {
            if (fact == null) throw new ArgumentNullException(nameof(fact));
            fact.CreatedUtc = DateTime.UtcNow;
            string sql = "INSERT INTO evalfacts (id, tenantid, subjectid, question, expectedanswer, category, createdutc) VALUES (" +
                Sanitizer.Str(fact.Id) + ", " + Sanitizer.Str(fact.TenantId) + ", " + Sanitizer.Str(fact.SubjectId) + ", " +
                Sanitizer.Str(fact.Question) + ", " + Sanitizer.Str(fact.ExpectedAnswer) + ", " + Sanitizer.Str(fact.Category) + ", " + Sanitizer.Ts(fact.CreatedUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return fact;
        }

        /// <inheritdoc />
        public async Task<EvalFact?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM evalfacts WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<EvalFact>> EnumerateBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM evalfacts WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) + " ORDER BY createdutc DESC;", token).ConfigureAwait(false);
            List<EvalFact> result = new List<EvalFact>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            await Query("DELETE FROM evalfacts WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            await Query("DELETE FROM evalfacts WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) + ";", token).ConfigureAwait(false);
        }

        internal static EvalFact Map(DataRow row)
        {
            return new EvalFact
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                SubjectId = RowReader.GetString(row, "subjectid"),
                Question = RowReader.GetString(row, "question"),
                ExpectedAnswer = RowReader.GetString(row, "expectedanswer"),
                Category = RowReader.GetNullableString(row, "category"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
