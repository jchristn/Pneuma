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
    using Pneuma.Core.Ontologies;

    /// <summary>SQL Server ontology violation methods.</summary>
    internal class OntologyViolationMethods : SqlServerMethodsBase, IOntologyViolationMethods
    {
        internal OntologyViolationMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task CreateManyAsync(List<OntologyViolation> violations, CancellationToken token = default)
        {
            if (violations == null) throw new ArgumentNullException(nameof(violations));
            if (violations.Count == 0) return;
            List<string> statements = new List<string>(violations.Count);
            foreach (OntologyViolation v in violations)
            {
                statements.Add(
                    "INSERT INTO ontologyviolations (id, tenantid, subjectid, jobid, linkid, operationid, ontologyversionid, ruleid, ruletype, elementkind, nodetype, nodename, " +
                    "edgetype, fromnodetype, fromnodename, tonodetype, tonodename, content, confidence, ruleaction, status, message, resolvedbyuserid, resolvedutc, createdutc) VALUES (" +
                    Sanitizer.Str(v.Id) + ", " + Sanitizer.Str(v.TenantId) + ", " + Sanitizer.Str(v.SubjectId) + ", " + Sanitizer.Str(v.JobId) + ", " +
                    Sanitizer.Str(v.LinkId) + ", " + Sanitizer.Str(v.OperationId) + ", " + Sanitizer.Str(v.OntologyVersionId) + ", " + Sanitizer.Str(v.RuleId) + ", " +
                    Sanitizer.Str(v.RuleType?.ToString()) + ", " + Sanitizer.Str(v.ElementKind.ToString()) + ", " + Sanitizer.Str(v.NodeType) + ", " +
                    Sanitizer.Str(v.NodeName) + ", " + Sanitizer.Str(v.EdgeType) + ", " + Sanitizer.Str(v.FromNodeType) + ", " + Sanitizer.Str(v.FromNodeName) + ", " +
                    Sanitizer.Str(v.ToNodeType) + ", " + Sanitizer.Str(v.ToNodeName) + ", " + Sanitizer.Str(v.Content) + ", " + Sanitizer.Num(v.Confidence) + ", " +
                    Sanitizer.Str(v.Action.ToString()) + ", " + Sanitizer.Str(v.Status.ToString()) + ", " + Sanitizer.Str(v.Message) + ", " +
                    Sanitizer.Str(v.ResolvedByUserId) + ", " + Sanitizer.Ts(v.ResolvedUtc) + ", " + Sanitizer.Ts(v.CreatedUtc) + ");");
            }
            await QueryTransaction(statements, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<OntologyViolation?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM ontologyviolations WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<OntologyViolation>> EnumerateAsync(string tenantId, string subjectId, OntologyViolationStatusEnum? status, string? jobId, string? operationId, CancellationToken token = default)
        {
            string sql = "SELECT * FROM ontologyviolations WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId);
            if (status != null) sql += " AND status = " + Sanitizer.Str(status.Value.ToString());
            if (!String.IsNullOrWhiteSpace(jobId)) sql += " AND jobid = " + Sanitizer.Str(jobId);
            if (!String.IsNullOrWhiteSpace(operationId)) sql += " AND operationid = " + Sanitizer.Str(operationId);
            DataTable table = await Query(sql + " ORDER BY createdutc DESC;", token).ConfigureAwait(false);
            List<OntologyViolation> result = new List<OntologyViolation>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task UpdateStatusAsync(OntologyViolation violation, CancellationToken token = default)
        {
            if (violation == null) throw new ArgumentNullException(nameof(violation));
            await Query(
                "UPDATE ontologyviolations SET status = " + Sanitizer.Str(violation.Status.ToString()) +
                ", resolvedbyuserid = " + Sanitizer.Str(violation.ResolvedByUserId) +
                ", resolvedutc = " + Sanitizer.Ts(violation.ResolvedUtc) +
                " WHERE tenantid = " + Sanitizer.Str(violation.TenantId) + " AND id = " + Sanitizer.Str(violation.Id) + ";", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteValidationFindingsAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            await Query(
                "DELETE FROM ontologyviolations WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) +
                " AND operationid IS NOT NULL AND status = 'Recorded';", token).ConfigureAwait(false);
        }

        internal static OntologyViolation Map(DataRow row)
        {
            return new OntologyViolation
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                SubjectId = RowReader.GetString(row, "subjectid"),
                JobId = RowReader.GetNullableString(row, "jobid"),
                LinkId = RowReader.GetNullableString(row, "linkid"),
                OperationId = RowReader.GetNullableString(row, "operationid"),
                OntologyVersionId = RowReader.GetNullableString(row, "ontologyversionid"),
                RuleId = RowReader.GetNullableString(row, "ruleid"),
                RuleType = RowReader.GetNullableEnum<OntologyRuleTypeEnum>(row, "ruletype"),
                ElementKind = RowReader.GetEnum<OntologyElementKindEnum>(row, "elementkind", OntologyElementKindEnum.Node),
                NodeType = RowReader.GetNullableString(row, "nodetype"),
                NodeName = RowReader.GetNullableString(row, "nodename"),
                EdgeType = RowReader.GetNullableString(row, "edgetype"),
                FromNodeType = RowReader.GetNullableString(row, "fromnodetype"),
                FromNodeName = RowReader.GetNullableString(row, "fromnodename"),
                ToNodeType = RowReader.GetNullableString(row, "tonodetype"),
                ToNodeName = RowReader.GetNullableString(row, "tonodename"),
                Content = RowReader.GetNullableString(row, "content"),
                Confidence = RowReader.GetDouble(row, "confidence"),
                Action = RowReader.GetEnum<OntologyRuleActionEnum>(row, "ruleaction", OntologyRuleActionEnum.Warn),
                Status = RowReader.GetEnum<OntologyViolationStatusEnum>(row, "status", OntologyViolationStatusEnum.Recorded),
                Message = RowReader.GetNullableString(row, "message") ?? String.Empty,
                ResolvedByUserId = RowReader.GetNullableString(row, "resolvedbyuserid"),
                ResolvedUtc = RowReader.GetNullableDateTime(row, "resolvedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
