namespace Pneuma.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Crawling;
    using Pneuma.Core.Database;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Enums;

    /// <summary>SQLite crawl operation methods.</summary>
    internal class CrawlOperationMethods : SqliteMethodsBase, ICrawlOperationMethods
    {
        private const int _BatchSize = 200;

        internal CrawlOperationMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<CrawlOperation> CreateAsync(CrawlOperation operation, CancellationToken token = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            await Query(
                "INSERT INTO crawloperations (id, tenantid, planid, subjectid, triggeredby, status, enumerated, added, updated, retried, unchanged, deleted, missing, skipped, failed, " +
                "bytesenumerated, helddeletions, error, startedutc, enumeratedutc, dispatchedutc, finishedutc) VALUES (" +
                Sanitizer.Str(operation.Id) + ", " + Sanitizer.Str(operation.TenantId) + ", " + Sanitizer.Str(operation.PlanId) + ", " +
                Sanitizer.Str(operation.SubjectId) + ", " + Sanitizer.Str(operation.Trigger.ToString()) + ", " + Sanitizer.Str(operation.Status.ToString()) + ", " +
                Sanitizer.Num(operation.Enumerated) + ", " + Sanitizer.Num(operation.Added) + ", " + Sanitizer.Num(operation.Updated) + ", " +
                Sanitizer.Num(operation.Retried) + ", " + Sanitizer.Num(operation.Unchanged) + ", " + Sanitizer.Num(operation.Deleted) + ", " +
                Sanitizer.Num(operation.Missing) + ", " + Sanitizer.Num(operation.Skipped) + ", " + Sanitizer.Num(operation.Failed) + ", " +
                operation.BytesEnumerated.ToString(CultureInfo.InvariantCulture) + ", " + Sanitizer.Num(operation.HeldDeletions) + ", " +
                Sanitizer.Str(operation.Error) + ", " + Sanitizer.Ts(operation.StartedUtc) + ", " + Sanitizer.Ts(operation.EnumeratedUtc) + ", " +
                Sanitizer.Ts(operation.DispatchedUtc) + ", " + Sanitizer.Ts(operation.FinishedUtc) + ");", token).ConfigureAwait(false);
            return operation;
        }

        /// <inheritdoc />
        public async Task<CrawlOperation?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM crawloperations WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task UpdateAsync(CrawlOperation operation, CancellationToken token = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            await Query(
                "UPDATE crawloperations SET status = " + Sanitizer.Str(operation.Status.ToString()) +
                ", enumerated = " + Sanitizer.Num(operation.Enumerated) +
                ", added = " + Sanitizer.Num(operation.Added) +
                ", updated = " + Sanitizer.Num(operation.Updated) +
                ", retried = " + Sanitizer.Num(operation.Retried) +
                ", unchanged = " + Sanitizer.Num(operation.Unchanged) +
                ", deleted = " + Sanitizer.Num(operation.Deleted) +
                ", missing = " + Sanitizer.Num(operation.Missing) +
                ", skipped = " + Sanitizer.Num(operation.Skipped) +
                ", failed = " + Sanitizer.Num(operation.Failed) +
                ", bytesenumerated = " + operation.BytesEnumerated.ToString(CultureInfo.InvariantCulture) +
                ", helddeletions = " + Sanitizer.Num(operation.HeldDeletions) +
                ", error = " + Sanitizer.Str(operation.Error) +
                ", enumeratedutc = " + Sanitizer.Ts(operation.EnumeratedUtc) +
                ", dispatchedutc = " + Sanitizer.Ts(operation.DispatchedUtc) +
                ", finishedutc = " + Sanitizer.Ts(operation.FinishedUtc) +
                " WHERE tenantid = " + Sanitizer.Str(operation.TenantId) + " AND id = " + Sanitizer.Str(operation.Id) + ";", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<CrawlOperation>> EnumerateByPlanAsync(string tenantId, string planId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM crawloperations WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND planid = " + Sanitizer.Str(planId) +
                " ORDER BY startedutc DESC;", token).ConfigureAwait(false);
            return MapAll(table);
        }

        /// <inheritdoc />
        public async Task<List<CrawlOperation>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM crawloperations WHERE tenantid = " + Sanitizer.Str(tenantId) + " ORDER BY startedutc DESC;", token).ConfigureAwait(false);
            return MapAll(table);
        }

        /// <inheritdoc />
        public async Task<List<CrawlOperation>> EnumerateByStatusAsync(CrawlOperationStatusEnum status, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM crawloperations WHERE status = " + Sanitizer.Str(status.ToString()) + " ORDER BY startedutc ASC;", token).ConfigureAwait(false);
            return MapAll(table);
        }

        /// <inheritdoc />
        public async Task<int> DeleteFinishedBeforeAsync(string tenantId, string planId, DateTime cutoffUtc, CancellationToken token = default)
        {
            string t = Sanitizer.Str(tenantId);
            string where = " WHERE tenantid = " + t + " AND planid = " + Sanitizer.Str(planId) + " AND finishedutc IS NOT NULL AND startedutc < " + Sanitizer.Ts(cutoffUtc);
            DataTable table = await Query("SELECT id FROM crawloperations" + where + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return 0;
            await QueryTransaction(new List<string>
            {
                "DELETE FROM crawloperationobjects WHERE tenantid = " + t + " AND operationid IN (SELECT id FROM crawloperations" + where + ");",
                "DELETE FROM crawloperations" + where + ";"
            }, token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        /// <inheritdoc />
        public async Task CreateObjectsAsync(IEnumerable<CrawlOperationObject> objects, CancellationToken token = default)
        {
            if (objects == null) throw new ArgumentNullException(nameof(objects));
            List<string> statements = objects.Select(o =>
                "INSERT INTO crawloperationobjects (id, tenantid, operationid, externalkey, crawlaction, succeeded, linkid, jobid, detail, createdutc) VALUES (" +
                Sanitizer.Str(o.Id) + ", " + Sanitizer.Str(o.TenantId) + ", " + Sanitizer.Str(o.OperationId) + ", " + Sanitizer.Str(o.ExternalKey) + ", " +
                Sanitizer.Str(o.Action.ToString()) + ", " + Outcome(o.Succeeded) + ", " + Sanitizer.Str(o.LinkId) + ", " + Sanitizer.Str(o.JobId) + ", " +
                Sanitizer.Str(o.Detail) + ", " + Sanitizer.Ts(o.CreatedUtc) + ");").ToList();
            await RunBatchesAsync(statements, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task UpdateObjectsAsync(IEnumerable<CrawlOperationObject> objects, CancellationToken token = default)
        {
            if (objects == null) throw new ArgumentNullException(nameof(objects));
            List<string> statements = objects.Select(o =>
                "UPDATE crawloperationobjects SET succeeded = " + Outcome(o.Succeeded) +
                ", linkid = " + Sanitizer.Str(o.LinkId) +
                ", jobid = " + Sanitizer.Str(o.JobId) +
                ", detail = " + Sanitizer.Str(o.Detail) +
                " WHERE tenantid = " + Sanitizer.Str(o.TenantId) + " AND id = " + Sanitizer.Str(o.Id) + ";").ToList();
            await RunBatchesAsync(statements, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<CrawlOperationObject>> EnumerateObjectsAsync(string tenantId, string operationId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM crawloperationobjects WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND operationid = " + Sanitizer.Str(operationId) +
                " ORDER BY createdutc ASC;", token).ConfigureAwait(false);
            List<CrawlOperationObject> result = new List<CrawlOperationObject>();
            foreach (DataRow row in table.Rows) result.Add(MapObject(row));
            return result;
        }

        private async Task RunBatchesAsync(List<string> statements, CancellationToken token)
        {
            for (int i = 0; i < statements.Count; i += _BatchSize)
            {
                await QueryTransaction(statements.Skip(i).Take(_BatchSize).ToList(), token).ConfigureAwait(false);
            }
        }

        private static string Outcome(bool? succeeded)
        {
            if (succeeded == null) return "NULL";
            return succeeded.Value ? "1" : "0";
        }

        private static List<CrawlOperation> MapAll(DataTable table)
        {
            List<CrawlOperation> result = new List<CrawlOperation>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        internal static CrawlOperation Map(DataRow row)
        {
            return new CrawlOperation
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                PlanId = RowReader.GetString(row, "planid"),
                SubjectId = RowReader.GetString(row, "subjectid"),
                Trigger = RowReader.GetEnum<CrawlTriggerEnum>(row, "triggeredby", CrawlTriggerEnum.Manual),
                Status = RowReader.GetEnum<CrawlOperationStatusEnum>(row, "status", CrawlOperationStatusEnum.Running),
                Enumerated = RowReader.GetInt(row, "enumerated"),
                Added = RowReader.GetInt(row, "added"),
                Updated = RowReader.GetInt(row, "updated"),
                Retried = RowReader.GetInt(row, "retried"),
                Unchanged = RowReader.GetInt(row, "unchanged"),
                Deleted = RowReader.GetInt(row, "deleted"),
                Missing = RowReader.GetInt(row, "missing"),
                Skipped = RowReader.GetInt(row, "skipped"),
                Failed = RowReader.GetInt(row, "failed"),
                BytesEnumerated = RowReader.GetLong(row, "bytesenumerated"),
                HeldDeletions = RowReader.GetInt(row, "helddeletions"),
                Error = RowReader.GetNullableString(row, "error"),
                StartedUtc = RowReader.GetDateTime(row, "startedutc"),
                EnumeratedUtc = RowReader.GetNullableDateTime(row, "enumeratedutc"),
                DispatchedUtc = RowReader.GetNullableDateTime(row, "dispatchedutc"),
                FinishedUtc = RowReader.GetNullableDateTime(row, "finishedutc")
            };
        }

        internal static CrawlOperationObject MapObject(DataRow row)
        {
            object raw = row["succeeded"];
            bool? succeeded = (raw == null || raw == DBNull.Value) ? (bool?)null : RowReader.GetBool(row, "succeeded");
            return new CrawlOperationObject
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                OperationId = RowReader.GetString(row, "operationid"),
                ExternalKey = RowReader.GetString(row, "externalkey"),
                Action = RowReader.GetEnum<CrawlActionEnum>(row, "crawlaction", CrawlActionEnum.Add),
                Succeeded = succeeded,
                LinkId = RowReader.GetNullableString(row, "linkid"),
                JobId = RowReader.GetNullableString(row, "jobid"),
                Detail = RowReader.GetNullableString(row, "detail"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
