namespace Pneuma.Core.Database.Mysql.Implementations
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

    /// <summary>MySQL ontology operation methods.</summary>
    internal class OntologyOperationMethods : MysqlMethodsBase, IOntologyOperationMethods
    {
        internal OntologyOperationMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<OntologyOperation> CreateAsync(OntologyOperation operation, CancellationToken token = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            operation.CreatedUtc = DateTime.UtcNow;
            await Query(
                "INSERT INTO ontologyoperations (id, tenantid, subjectid, kind, status, ontologyversionid, requestedbyuserid, samplesize, total, processed, changed, added, removed, " +
                "driftrate, error, claimtoken, createdutc, startedutc, finishedutc) VALUES (" +
                Sanitizer.Str(operation.Id) + ", " + Sanitizer.Str(operation.TenantId) + ", " + Sanitizer.Str(operation.SubjectId) + ", " +
                Sanitizer.Str(operation.Kind.ToString()) + ", " + Sanitizer.Str(operation.Status.ToString()) + ", " + Sanitizer.Str(operation.OntologyVersionId) + ", " +
                Sanitizer.Str(operation.RequestedByUserId) + ", " + Sanitizer.Num(operation.SampleSize) + ", " + Sanitizer.Num(operation.Total) + ", " +
                Sanitizer.Num(operation.Processed) + ", " + Sanitizer.Num(operation.Changed) + ", " + Sanitizer.Num(operation.Added) + ", " + Sanitizer.Num(operation.Removed) + ", " +
                Sanitizer.Num(operation.DriftRate) + ", " + Sanitizer.Str(operation.Error) + ", " + Sanitizer.Str(operation.ClaimToken) + ", " +
                Sanitizer.Ts(operation.CreatedUtc) + ", " + Sanitizer.Ts(operation.StartedUtc) + ", " + Sanitizer.Ts(operation.FinishedUtc) + ");", token).ConfigureAwait(false);
            return operation;
        }

        /// <inheritdoc />
        public async Task<OntologyOperation?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM ontologyoperations WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<OntologyOperation>> EnumerateAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM ontologyoperations WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) +
                " ORDER BY createdutc DESC;", token).ConfigureAwait(false);
            List<OntologyOperation> result = new List<OntologyOperation>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<bool> ExistsActiveAsync(string tenantId, string subjectId, OntologyOperationKindEnum kind, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT id FROM ontologyoperations WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) +
                " AND kind = " + Sanitizer.Str(kind.ToString()) + " AND status IN ('Queued', 'Running');", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        /// <inheritdoc />
        public async Task<OntologyOperation?> ClaimNextQueuedAsync(string claimToken, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(claimToken)) throw new ArgumentNullException(nameof(claimToken));
            DataTable queued = await Query("SELECT id, tenantid FROM ontologyoperations WHERE status = 'Queued' ORDER BY createdutc ASC;", token).ConfigureAwait(false);
            if (queued.Rows.Count == 0) return null;
            string tenantId = RowReader.GetString(queued.Rows[0], "tenantid");
            string id = RowReader.GetString(queued.Rows[0], "id");
            string t = Sanitizer.Str(tenantId);
            string o = Sanitizer.Str(id);
            await Query(
                "UPDATE ontologyoperations SET status = 'Running', claimtoken = " + Sanitizer.Str(claimToken) + ", startedutc = " + Sanitizer.Ts(DateTime.UtcNow) +
                " WHERE tenantid = " + t + " AND id = " + o + " AND status = 'Queued';", token).ConfigureAwait(false);
            OntologyOperation? claimed = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (claimed == null || !String.Equals(claimed.ClaimToken, claimToken, StringComparison.Ordinal)) return null;
            return claimed;
        }

        /// <inheritdoc />
        public async Task UpdateAsync(OntologyOperation operation, CancellationToken token = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            await Query(
                "UPDATE ontologyoperations SET status = " + Sanitizer.Str(operation.Status.ToString()) +
                ", ontologyversionid = " + Sanitizer.Str(operation.OntologyVersionId) +
                ", total = " + Sanitizer.Num(operation.Total) +
                ", processed = " + Sanitizer.Num(operation.Processed) +
                ", changed = " + Sanitizer.Num(operation.Changed) +
                ", added = " + Sanitizer.Num(operation.Added) +
                ", removed = " + Sanitizer.Num(operation.Removed) +
                ", driftrate = " + Sanitizer.Num(operation.DriftRate) +
                ", error = " + Sanitizer.Str(operation.Error) +
                ", startedutc = " + Sanitizer.Ts(operation.StartedUtc) +
                ", finishedutc = " + Sanitizer.Ts(operation.FinishedUtc) +
                " WHERE tenantid = " + Sanitizer.Str(operation.TenantId) + " AND id = " + Sanitizer.Str(operation.Id) + ";", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> FailRunningAsync(string error, CancellationToken token = default)
        {
            DataTable running = await Query("SELECT id FROM ontologyoperations WHERE status = 'Running';", token).ConfigureAwait(false);
            if (running.Rows.Count == 0) return 0;
            await Query(
                "UPDATE ontologyoperations SET status = 'Failed', error = " + Sanitizer.Str(error) + ", finishedutc = " + Sanitizer.Ts(DateTime.UtcNow) +
                " WHERE status = 'Running';", token).ConfigureAwait(false);
            return running.Rows.Count;
        }

        /// <inheritdoc />
        public async Task AddItemsAsync(string tenantId, List<OntologyOperationItem> items, CancellationToken token = default)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (items.Count == 0) return;
            string t = Sanitizer.Str(tenantId);
            List<string> statements = new List<string>(items.Count);
            foreach (OntologyOperationItem item in items)
            {
                statements.Add("INSERT INTO ontologyoperationitems (tenantid, operationid, ordinal, nodeid, excerpt, ischanged, detail) VALUES (" + t + ", " +
                    Sanitizer.Str(item.OperationId) + ", " + Sanitizer.Num(item.Ordinal) + ", " + Sanitizer.Str(item.NodeId) + ", " + Sanitizer.Str(item.Excerpt) + ", " +
                    Sanitizer.Bit(item.Changed) + ", " + Sanitizer.Str(item.Detail) + ");");
            }
            await QueryTransaction(statements, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<OntologyOperationItem>> EnumerateItemsAsync(string tenantId, string operationId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM ontologyoperationitems WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND operationid = " + Sanitizer.Str(operationId) +
                " ORDER BY ordinal ASC;", token).ConfigureAwait(false);
            List<OntologyOperationItem> result = new List<OntologyOperationItem>();
            foreach (DataRow row in table.Rows)
            {
                result.Add(new OntologyOperationItem
                {
                    OperationId = RowReader.GetString(row, "operationid"),
                    Ordinal = RowReader.GetInt(row, "ordinal"),
                    NodeId = RowReader.GetNullableString(row, "nodeid"),
                    Excerpt = RowReader.GetNullableString(row, "excerpt"),
                    Changed = RowReader.GetBool(row, "ischanged"),
                    Detail = RowReader.GetNullableString(row, "detail")
                });
            }
            return result;
        }

        internal static OntologyOperation Map(DataRow row)
        {
            return new OntologyOperation
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                SubjectId = RowReader.GetString(row, "subjectid"),
                Kind = RowReader.GetEnum<OntologyOperationKindEnum>(row, "kind", OntologyOperationKindEnum.Validate),
                Status = RowReader.GetEnum<OntologyOperationStatusEnum>(row, "status", OntologyOperationStatusEnum.Queued),
                OntologyVersionId = RowReader.GetNullableString(row, "ontologyversionid"),
                RequestedByUserId = RowReader.GetNullableString(row, "requestedbyuserid"),
                SampleSize = RowReader.GetInt(row, "samplesize"),
                Total = RowReader.GetInt(row, "total"),
                Processed = RowReader.GetInt(row, "processed"),
                Changed = RowReader.GetInt(row, "changed"),
                Added = RowReader.GetInt(row, "added"),
                Removed = RowReader.GetInt(row, "removed"),
                DriftRate = RowReader.GetDouble(row, "driftrate"),
                Error = RowReader.GetNullableString(row, "error"),
                ClaimToken = RowReader.GetNullableString(row, "claimtoken"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                StartedUtc = RowReader.GetNullableDateTime(row, "startedutc"),
                FinishedUtc = RowReader.GetNullableDateTime(row, "finishedutc")
            };
        }
    }
}
