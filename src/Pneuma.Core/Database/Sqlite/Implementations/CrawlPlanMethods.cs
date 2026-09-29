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

    /// <summary>SQLite crawl plan methods.</summary>
    internal class CrawlPlanMethods : SqliteMethodsBase, ICrawlPlanMethods
    {
        internal CrawlPlanMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<CrawlPlan> CreateAsync(CrawlPlan plan, CancellationToken token = default)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            DateTime now = DateTime.UtcNow;
            plan.CreatedUtc = now;
            plan.LastUpdateUtc = now;
            List<string> statements = new List<string>();
            statements.Add(
                "INSERT INTO crawlplans (id, tenantid, subjectid, name, plantype, enabled, status, filterminsizebytes, filtermaxsizebytes, filtermaxobjects, " +
                "scheduletype, scheduleintervalminutes, schedulecron, scheduletimezone, processadditions, processupdates, processdeletions, maxdeletionfraction, " +
                "retryfailedobjects, operationretentiondays, lastoperationid, lastrunutc, lastsuccessutc, nextrunutc, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(plan.Id) + ", " + Sanitizer.Str(plan.TenantId) + ", " + Sanitizer.Str(plan.SubjectId) + ", " + Sanitizer.Str(plan.Name) + ", " +
                Sanitizer.Str(plan.Type.ToString()) + ", " + Sanitizer.Bit(plan.Enabled) + ", " + Sanitizer.Str(plan.Status.ToString()) + ", " +
                Long(plan.Filter.MinSizeBytes) + ", " + Long(plan.Filter.MaxSizeBytes) + ", " + Sanitizer.Num(plan.Filter.MaxObjects) + ", " +
                Sanitizer.Str(plan.Schedule.Type.ToString()) + ", " + Sanitizer.Num(plan.Schedule.IntervalMinutes) + ", " + Sanitizer.Str(plan.Schedule.CronExpression) + ", " +
                Sanitizer.Str(plan.Schedule.TimeZone) + ", " + Sanitizer.Bit(plan.ProcessAdditions) + ", " + Sanitizer.Bit(plan.ProcessUpdates) + ", " +
                Sanitizer.Bit(plan.ProcessDeletions) + ", " + Sanitizer.Num(plan.MaxDeletionFraction) + ", " + Sanitizer.Bit(plan.RetryFailedObjects) + ", " +
                Sanitizer.Num(plan.OperationRetentionDays) + ", " + Sanitizer.Str(plan.LastOperationId) + ", " + Sanitizer.Ts(plan.LastRunUtc) + ", " +
                Sanitizer.Ts(plan.LastSuccessUtc) + ", " + Sanitizer.Ts(plan.NextRunUtc) + ", " + Sanitizer.Ts(plan.CreatedUtc) + ", " + Sanitizer.Ts(plan.LastUpdateUtc) + ");");
            statements.AddRange(SettingsInsertSql(plan));
            await QueryTransaction(statements, token).ConfigureAwait(false);
            return plan;
        }

        /// <inheritdoc />
        public async Task<CrawlPlan?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM crawlplans WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            CrawlPlan plan = Map(table.Rows[0]);
            await LoadChildrenAsync(new List<CrawlPlan> { plan }, token).ConfigureAwait(false);
            return plan;
        }

        /// <inheritdoc />
        public async Task<List<CrawlPlan>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM crawlplans WHERE tenantid = " + Sanitizer.Str(tenantId) + " ORDER BY createdutc DESC;", token).ConfigureAwait(false);
            List<CrawlPlan> plans = MapAll(table);
            await LoadChildrenAsync(plans, token).ConfigureAwait(false);
            return plans;
        }

        /// <inheritdoc />
        public async Task<List<CrawlPlan>> EnumerateBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM crawlplans WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND subjectid = " + Sanitizer.Str(subjectId) +
                " ORDER BY createdutc DESC;", token).ConfigureAwait(false);
            List<CrawlPlan> plans = MapAll(table);
            await LoadChildrenAsync(plans, token).ConfigureAwait(false);
            return plans;
        }

        /// <inheritdoc />
        public async Task<List<CrawlPlan>> EnumerateDueAsync(DateTime nowUtc, CancellationToken token = default)
        {
            string now = Sanitizer.Ts(nowUtc);
            DataTable table = await Query(
                "SELECT * FROM crawlplans WHERE enabled = 1 AND scheduletype <> 'Manual' AND nextrunutc IS NOT NULL AND nextrunutc <= " + now +
                " AND (status = 'Idle' OR (claimexpiresutc IS NOT NULL AND claimexpiresutc < " + now + ")) ORDER BY nextrunutc ASC;", token).ConfigureAwait(false);
            return MapAll(table);
        }

        /// <inheritdoc />
        public async Task<List<CrawlPlan>> EnumerateBusyAsync(CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM crawlplans WHERE status <> 'Idle';", token).ConfigureAwait(false);
            return MapAll(table);
        }

        /// <inheritdoc />
        public async Task<CrawlPlan> UpdateAsync(CrawlPlan plan, CancellationToken token = default)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            plan.LastUpdateUtc = DateTime.UtcNow;
            string t = Sanitizer.Str(plan.TenantId);
            string p = Sanitizer.Str(plan.Id);
            List<string> statements = new List<string>();
            statements.Add(
                "UPDATE crawlplans SET name = " + Sanitizer.Str(plan.Name) +
                ", enabled = " + Sanitizer.Bit(plan.Enabled) +
                ", filterminsizebytes = " + Long(plan.Filter.MinSizeBytes) +
                ", filtermaxsizebytes = " + Long(plan.Filter.MaxSizeBytes) +
                ", filtermaxobjects = " + Sanitizer.Num(plan.Filter.MaxObjects) +
                ", scheduletype = " + Sanitizer.Str(plan.Schedule.Type.ToString()) +
                ", scheduleintervalminutes = " + Sanitizer.Num(plan.Schedule.IntervalMinutes) +
                ", schedulecron = " + Sanitizer.Str(plan.Schedule.CronExpression) +
                ", scheduletimezone = " + Sanitizer.Str(plan.Schedule.TimeZone) +
                ", processadditions = " + Sanitizer.Bit(plan.ProcessAdditions) +
                ", processupdates = " + Sanitizer.Bit(plan.ProcessUpdates) +
                ", processdeletions = " + Sanitizer.Bit(plan.ProcessDeletions) +
                ", maxdeletionfraction = " + Sanitizer.Num(plan.MaxDeletionFraction) +
                ", retryfailedobjects = " + Sanitizer.Bit(plan.RetryFailedObjects) +
                ", operationretentiondays = " + Sanitizer.Num(plan.OperationRetentionDays) +
                ", nextrunutc = " + Sanitizer.Ts(plan.NextRunUtc) +
                ", lastupdateutc = " + Sanitizer.Ts(plan.LastUpdateUtc) +
                " WHERE tenantid = " + t + " AND id = " + p + ";");
            statements.Add("DELETE FROM crawlplansettings WHERE tenantid = " + t + " AND planid = " + p + ";");
            statements.AddRange(SettingsInsertSql(plan));
            await QueryTransaction(statements, token).ConfigureAwait(false);
            return plan;
        }

        /// <inheritdoc />
        public async Task UpdateRunStateAsync(CrawlPlan plan, CancellationToken token = default)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            string claim = plan.Status == CrawlPlanStatusEnum.Idle ? ", claimtoken = NULL, claimexpiresutc = NULL" : String.Empty;
            await Query(
                "UPDATE crawlplans SET status = " + Sanitizer.Str(plan.Status.ToString()) +
                ", lastoperationid = " + Sanitizer.Str(plan.LastOperationId) +
                ", lastrunutc = " + Sanitizer.Ts(plan.LastRunUtc) +
                ", lastsuccessutc = " + Sanitizer.Ts(plan.LastSuccessUtc) +
                ", nextrunutc = " + Sanitizer.Ts(plan.NextRunUtc) + claim +
                " WHERE tenantid = " + Sanitizer.Str(plan.TenantId) + " AND id = " + Sanitizer.Str(plan.Id) + ";", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> TryClaimAsync(string tenantId, string id, string claimToken, DateTime expiresUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(claimToken)) throw new ArgumentNullException(nameof(claimToken));
            string t = Sanitizer.Str(tenantId);
            string p = Sanitizer.Str(id);
            string now = Sanitizer.Ts(DateTime.UtcNow);
            await Query(
                "UPDATE crawlplans SET status = 'Running', claimtoken = " + Sanitizer.Str(claimToken) + ", claimexpiresutc = " + Sanitizer.Ts(expiresUtc) +
                " WHERE tenantid = " + t + " AND id = " + p +
                " AND (status = 'Idle' OR (claimexpiresutc IS NOT NULL AND claimexpiresutc < " + now + "));", token).ConfigureAwait(false);
            DataTable table = await Query("SELECT claimtoken FROM crawlplans WHERE tenantid = " + t + " AND id = " + p + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return false;
            return String.Equals(RowReader.GetNullableString(table.Rows[0], "claimtoken"), claimToken, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public async Task RenewClaimAsync(string tenantId, string id, string claimToken, DateTime expiresUtc, CancellationToken token = default)
        {
            await Query(
                "UPDATE crawlplans SET claimexpiresutc = " + Sanitizer.Ts(expiresUtc) +
                " WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + " AND claimtoken = " + Sanitizer.Str(claimToken) + ";", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task SetStatusAsync(string tenantId, string id, CrawlPlanStatusEnum status, CancellationToken token = default)
        {
            string claim = status == CrawlPlanStatusEnum.Idle ? ", claimtoken = NULL, claimexpiresutc = NULL" : String.Empty;
            await Query(
                "UPDATE crawlplans SET status = " + Sanitizer.Str(status.ToString()) + claim +
                " WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable existing = await Query(
                "SELECT id FROM crawlplans WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (existing.Rows.Count == 0) return false;
            await QueryTransaction(DeleteByPlanSql(tenantId, id), token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc />
        public async Task<Dictionary<string, string>> ReadSecretsAsync(string tenantId, string planId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT name, ciphertext FROM crawlplansecrets WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND planid = " + Sanitizer.Str(planId) + ";", token).ConfigureAwait(false);
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (DataRow row in table.Rows) result[RowReader.GetString(row, "name")] = RowReader.GetString(row, "ciphertext");
            return result;
        }

        /// <inheritdoc />
        public async Task SetSecretAsync(string tenantId, string planId, string name, string ciphertext, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (String.IsNullOrEmpty(ciphertext)) throw new ArgumentNullException(nameof(ciphertext));
            string t = Sanitizer.Str(tenantId);
            string p = Sanitizer.Str(planId);
            string n = Sanitizer.Str(name);
            await QueryTransaction(new List<string>
            {
                "DELETE FROM crawlplansecrets WHERE tenantid = " + t + " AND planid = " + p + " AND name = " + n + ";",
                "INSERT INTO crawlplansecrets (tenantid, planid, name, ciphertext, lastupdateutc) VALUES (" + t + ", " + p + ", " + n + ", " +
                    Sanitizer.Str(ciphertext) + ", " + Sanitizer.Ts(DateTime.UtcNow) + ");"
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteSecretAsync(string tenantId, string planId, string name, CancellationToken token = default)
        {
            await Query(
                "DELETE FROM crawlplansecrets WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND planid = " + Sanitizer.Str(planId) +
                " AND name = " + Sanitizer.Str(name) + ";", token).ConfigureAwait(false);
        }

        /// <summary>Statements that delete a plan and everything that belongs to it.</summary>
        internal static List<string> DeleteByPlanSql(string tenantId, string planId)
        {
            string t = Sanitizer.Str(tenantId);
            string p = Sanitizer.Str(planId);
            return new List<string>
            {
                "DELETE FROM crawloperationobjects WHERE tenantid = " + t + " AND operationid IN (SELECT id FROM crawloperations WHERE tenantid = " + t + " AND planid = " + p + ");",
                "DELETE FROM crawloperations WHERE tenantid = " + t + " AND planid = " + p + ";",
                "DELETE FROM crawlobjects WHERE tenantid = " + t + " AND planid = " + p + ";",
                "DELETE FROM crawlplansecrets WHERE tenantid = " + t + " AND planid = " + p + ";",
                "DELETE FROM crawlplansettings WHERE tenantid = " + t + " AND planid = " + p + ";",
                "DELETE FROM crawlplans WHERE tenantid = " + t + " AND id = " + p + ";"
            };
        }

        /// <summary>Statements that delete every crawl plan of a subject and everything that belongs to them.</summary>
        internal static List<string> DeleteBySubjectSql(string tenantId, string subjectId)
        {
            string t = Sanitizer.Str(tenantId);
            string plans = "(SELECT id FROM crawlplans WHERE tenantid = " + t + " AND subjectid = " + Sanitizer.Str(subjectId) + ")";
            return new List<string>
            {
                "DELETE FROM crawloperationobjects WHERE tenantid = " + t + " AND operationid IN (SELECT id FROM crawloperations WHERE tenantid = " + t + " AND planid IN " + plans + ");",
                "DELETE FROM crawloperations WHERE tenantid = " + t + " AND planid IN " + plans + ";",
                "DELETE FROM crawlobjects WHERE tenantid = " + t + " AND planid IN " + plans + ";",
                "DELETE FROM crawlplansecrets WHERE tenantid = " + t + " AND planid IN " + plans + ";",
                "DELETE FROM crawlplansettings WHERE tenantid = " + t + " AND planid IN " + plans + ";",
                "DELETE FROM crawlplans WHERE tenantid = " + t + " AND subjectid = " + Sanitizer.Str(subjectId) + ";"
            };
        }

        private static List<string> SettingsInsertSql(CrawlPlan plan)
        {
            List<string> statements = new List<string>();
            string t = Sanitizer.Str(plan.TenantId);
            string p = Sanitizer.Str(plan.Id);
            foreach (CrawlPlanSetting row in CrawlSettingsCodec.ToRows(plan))
            {
                statements.Add(
                    "INSERT INTO crawlplansettings (tenantid, planid, name, ordinal, settingvalue) VALUES (" + t + ", " + p + ", " +
                    Sanitizer.Str(row.Name) + ", " + Sanitizer.Num(row.Ordinal) + ", " + Sanitizer.Str(row.Value) + ");");
            }
            return statements;
        }

        private async Task LoadChildrenAsync(List<CrawlPlan> plans, CancellationToken token)
        {
            if (plans.Count == 0) return;
            string t = Sanitizer.Str(plans[0].TenantId);
            string ids = String.Join(", ", plans.Select(p => Sanitizer.Str(p.Id)));
            DataTable settings = await Query(
                "SELECT planid, name, ordinal, settingvalue FROM crawlplansettings WHERE tenantid = " + t + " AND planid IN (" + ids + ");", token).ConfigureAwait(false);
            DataTable secrets = await Query(
                "SELECT planid, name FROM crawlplansecrets WHERE tenantid = " + t + " AND planid IN (" + ids + ");", token).ConfigureAwait(false);

            Dictionary<string, List<CrawlPlanSetting>> rowsByPlan = new Dictionary<string, List<CrawlPlanSetting>>(StringComparer.Ordinal);
            foreach (DataRow row in settings.Rows)
            {
                string planId = RowReader.GetString(row, "planid");
                List<CrawlPlanSetting>? list;
                if (!rowsByPlan.TryGetValue(planId, out list))
                {
                    list = new List<CrawlPlanSetting>();
                    rowsByPlan[planId] = list;
                }
                list.Add(new CrawlPlanSetting
                {
                    Name = RowReader.GetString(row, "name"),
                    Ordinal = RowReader.GetInt(row, "ordinal"),
                    Value = RowReader.GetNullableString(row, "settingvalue") ?? String.Empty
                });
            }

            foreach (CrawlPlan plan in plans)
            {
                List<CrawlPlanSetting>? rows;
                CrawlSettingsCodec.ApplyRows(plan, rowsByPlan.TryGetValue(plan.Id, out rows) ? rows : new List<CrawlPlanSetting>());
                plan.SecretsSet = secrets.Rows.Cast<DataRow>()
                    .Where(r => RowReader.GetString(r, "planid") == plan.Id)
                    .Select(r => RowReader.GetString(r, "name"))
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();
            }
        }

        private static string Long(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static List<CrawlPlan> MapAll(DataTable table)
        {
            List<CrawlPlan> result = new List<CrawlPlan>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        internal static CrawlPlan Map(DataRow row)
        {
            CrawlPlan plan = new CrawlPlan
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                SubjectId = RowReader.GetString(row, "subjectid"),
                Name = RowReader.GetString(row, "name"),
                Type = RowReader.GetEnum<CrawlPlanTypeEnum>(row, "plantype", CrawlPlanTypeEnum.Web),
                Enabled = RowReader.GetBool(row, "enabled"),
                Status = RowReader.GetEnum<CrawlPlanStatusEnum>(row, "status", CrawlPlanStatusEnum.Idle),
                ProcessAdditions = RowReader.GetBool(row, "processadditions"),
                ProcessUpdates = RowReader.GetBool(row, "processupdates"),
                ProcessDeletions = RowReader.GetBool(row, "processdeletions"),
                MaxDeletionFraction = RowReader.GetDouble(row, "maxdeletionfraction"),
                RetryFailedObjects = RowReader.GetBool(row, "retryfailedobjects"),
                OperationRetentionDays = RowReader.GetInt(row, "operationretentiondays"),
                LastOperationId = RowReader.GetNullableString(row, "lastoperationid"),
                LastRunUtc = RowReader.GetNullableDateTime(row, "lastrunutc"),
                LastSuccessUtc = RowReader.GetNullableDateTime(row, "lastsuccessutc"),
                NextRunUtc = RowReader.GetNullableDateTime(row, "nextrunutc"),
                ClaimExpiresUtc = RowReader.GetNullableDateTime(row, "claimexpiresutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
            plan.Filter.MinSizeBytes = RowReader.GetLong(row, "filterminsizebytes");
            plan.Filter.MaxSizeBytes = RowReader.GetLong(row, "filtermaxsizebytes");
            plan.Filter.MaxObjects = RowReader.GetInt(row, "filtermaxobjects");
            plan.Schedule.Type = RowReader.GetEnum<CrawlScheduleTypeEnum>(row, "scheduletype", CrawlScheduleTypeEnum.Manual);
            plan.Schedule.IntervalMinutes = RowReader.GetInt(row, "scheduleintervalminutes");
            plan.Schedule.CronExpression = RowReader.GetNullableString(row, "schedulecron");
            plan.Schedule.TimeZone = RowReader.GetNullableString(row, "scheduletimezone") ?? "UTC";
            return plan;
        }
    }
}
