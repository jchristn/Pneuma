namespace Pneuma.Core.Database.Mysql.Implementations
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

    /// <summary>MySQL crawl object methods.</summary>
    internal class CrawlObjectMethods : MysqlMethodsBase, ICrawlObjectMethods
    {
        private const int _BatchSize = 200;

        internal CrawlObjectMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<List<CrawlObject>> EnumerateByPlanAsync(string tenantId, string planId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM crawlobjects WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND planid = " + Sanitizer.Str(planId) + ";", token).ConfigureAwait(false);
            List<CrawlObject> result = new List<CrawlObject>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result.OrderBy(o => o.ExternalKey, StringComparer.Ordinal).ToList();
        }

        /// <inheritdoc />
        public async Task<CrawlObject?> ReadByLinkAsync(string tenantId, string linkId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM crawlobjects WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND linkid = " + Sanitizer.Str(linkId) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task CreateManyAsync(IEnumerable<CrawlObject> objects, CancellationToken token = default)
        {
            if (objects == null) throw new ArgumentNullException(nameof(objects));
            List<string> statements = objects.Select(o =>
                "INSERT INTO crawlobjects (id, tenantid, planid, externalkey, linkid, versiontoken, sizebytes, contenttype, status, lasterror, lastoperationid, firstseenutc, lastseenutc) VALUES (" +
                Sanitizer.Str(o.Id) + ", " + Sanitizer.Str(o.TenantId) + ", " + Sanitizer.Str(o.PlanId) + ", " + Sanitizer.Str(o.ExternalKey) + ", " +
                Sanitizer.Str(o.LinkId) + ", " + Sanitizer.Str(o.VersionToken) + ", " + o.SizeBytes.ToString(CultureInfo.InvariantCulture) + ", " +
                Sanitizer.Str(o.ContentType) + ", " + Sanitizer.Str(o.Status.ToString()) + ", " + Sanitizer.Str(o.LastError) + ", " +
                Sanitizer.Str(o.LastOperationId) + ", " + Sanitizer.Ts(o.FirstSeenUtc) + ", " + Sanitizer.Ts(o.LastSeenUtc) + ");").ToList();
            await RunBatchesAsync(statements, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task UpdateManyAsync(IEnumerable<CrawlObject> objects, CancellationToken token = default)
        {
            if (objects == null) throw new ArgumentNullException(nameof(objects));
            List<string> statements = objects.Select(o =>
                "UPDATE crawlobjects SET linkid = " + Sanitizer.Str(o.LinkId) +
                ", versiontoken = " + Sanitizer.Str(o.VersionToken) +
                ", sizebytes = " + o.SizeBytes.ToString(CultureInfo.InvariantCulture) +
                ", contenttype = " + Sanitizer.Str(o.ContentType) +
                ", status = " + Sanitizer.Str(o.Status.ToString()) +
                ", lasterror = " + Sanitizer.Str(o.LastError) +
                ", lastoperationid = " + Sanitizer.Str(o.LastOperationId) +
                ", lastseenutc = " + Sanitizer.Ts(o.LastSeenUtc) +
                " WHERE tenantid = " + Sanitizer.Str(o.TenantId) + " AND id = " + Sanitizer.Str(o.Id) + ";").ToList();
            await RunBatchesAsync(statements, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteManyAsync(string tenantId, IEnumerable<string> ids, CancellationToken token = default)
        {
            if (ids == null) return;
            string t = Sanitizer.Str(tenantId);
            List<string> statements = ids.Where(i => !String.IsNullOrEmpty(i))
                .Select(i => "DELETE FROM crawlobjects WHERE tenantid = " + t + " AND id = " + Sanitizer.Str(i) + ";").ToList();
            await RunBatchesAsync(statements, token).ConfigureAwait(false);
        }

        private async Task RunBatchesAsync(List<string> statements, CancellationToken token)
        {
            for (int i = 0; i < statements.Count; i += _BatchSize)
            {
                await QueryTransaction(statements.Skip(i).Take(_BatchSize).ToList(), token).ConfigureAwait(false);
            }
        }

        internal static CrawlObject Map(DataRow row)
        {
            return new CrawlObject
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                PlanId = RowReader.GetString(row, "planid"),
                ExternalKey = RowReader.GetString(row, "externalkey"),
                LinkId = RowReader.GetNullableString(row, "linkid"),
                VersionToken = RowReader.GetNullableString(row, "versiontoken"),
                SizeBytes = RowReader.GetLong(row, "sizebytes"),
                ContentType = RowReader.GetNullableString(row, "contenttype"),
                Status = RowReader.GetEnum<CrawlObjectStatusEnum>(row, "status", CrawlObjectStatusEnum.Active),
                LastError = RowReader.GetNullableString(row, "lasterror"),
                LastOperationId = RowReader.GetNullableString(row, "lastoperationid"),
                FirstSeenUtc = RowReader.GetDateTime(row, "firstseenutc"),
                LastSeenUtc = RowReader.GetDateTime(row, "lastseenutc")
            };
        }
    }
}
