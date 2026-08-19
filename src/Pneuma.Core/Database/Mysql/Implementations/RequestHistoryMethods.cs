namespace Pneuma.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;

    /// <summary>MySQL request history methods.</summary>
    internal class RequestHistoryMethods : MysqlMethodsBase, IRequestHistoryMethods
    {
        internal RequestHistoryMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task CreateAsync(RequestHistoryEntry entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            string requestHeaders = JsonSerializer.Serialize(entry.RequestHeaders);
            string responseHeaders = JsonSerializer.Serialize(entry.ResponseHeaders);

            string sql =
                "INSERT INTO requesthistory (id, tenantid, userid, principalname, method, path, url, statuscode, durationms, sourceip, requestheaders, requestbody, requestbodybytes, requestbodytruncated, responseheaders, responsebody, responsebodybytes, responsebodytruncated, createdutc, completedutc) VALUES (" +
                Sanitizer.Str(entry.Id) + ", " + Sanitizer.Str(entry.TenantId) + ", " +
                Sanitizer.Str(entry.UserId) + ", " + Sanitizer.Str(entry.PrincipalName) + ", " +
                Sanitizer.Str(entry.Method) + ", " + Sanitizer.Str(entry.Path) + ", " +
                Sanitizer.Str(entry.Url) + ", " + entry.StatusCode.ToString(CultureInfo.InvariantCulture) + ", " +
                entry.DurationMs.ToString(CultureInfo.InvariantCulture) + ", " + Sanitizer.Str(entry.SourceIp) + ", " +
                Sanitizer.Str(requestHeaders) + ", " + Sanitizer.Str(entry.RequestBody) + ", " +
                entry.RequestBodyBytes.ToString(CultureInfo.InvariantCulture) + ", " + Sanitizer.Bit(entry.RequestBodyTruncated) + ", " +
                Sanitizer.Str(responseHeaders) + ", " + Sanitizer.Str(entry.ResponseBody) + ", " +
                entry.ResponseBodyBytes.ToString(CultureInfo.InvariantCulture) + ", " + Sanitizer.Bit(entry.ResponseBodyTruncated) + ", " +
                Sanitizer.Ts(entry.CreatedUtc) + ", " + Sanitizer.Ts(entry.CompletedUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<RequestHistoryEntry?> ReadAsync(string? tenantId, string id, CancellationToken token = default)
        {
            string sql;
            if (tenantId == null)
            {
                sql = "SELECT * FROM requesthistory WHERE id = " + Sanitizer.Str(id) + ";";
            }
            else
            {
                sql = "SELECT * FROM requesthistory WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";";
            }

            DataTable table = await Query(sql, token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return MapFull(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<RequestHistoryPage> EnumerateAsync(RequestHistoryFilter filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            string where = BuildWhere(filter);

            DataTable countTable = await Query("SELECT COUNT(*) AS cnt FROM requesthistory" + where + ";", token).ConfigureAwait(false);
            long total = countTable.Rows.Count == 0 ? 0 : RowReader.GetLong(countTable.Rows[0], "cnt");

            int offset = (filter.PageNumber - 1) * filter.PageSize;
            string listSql =
                "SELECT id, tenantid, userid, principalname, method, path, url, statuscode, durationms, sourceip, " +
                "requestbodybytes, requestbodytruncated, responsebodybytes, responsebodytruncated, createdutc, completedutc " +
                "FROM requesthistory" + where +
                " ORDER BY createdutc DESC LIMIT " + filter.PageSize.ToString(CultureInfo.InvariantCulture) +
                " OFFSET " + offset.ToString(CultureInfo.InvariantCulture) + ";";

            DataTable table = await Query(listSql, token).ConfigureAwait(false);
            RequestHistoryPage page = new RequestHistoryPage
            {
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize,
                TotalCount = total
            };
            foreach (DataRow row in table.Rows) page.Items.Add(MapList(row));
            return page;
        }

        /// <inheritdoc />
        public async Task<RequestHistorySummary> SummarizeAsync(RequestHistoryFilter filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            DateTime toUtc = (filter.ToUtc ?? DateTime.UtcNow).ToUniversalTime();
            DateTime fromUtc = (filter.FromUtc ?? toUtc.AddHours(-24)).ToUniversalTime();

            string where = BuildWhere(filter);
            string sql = "SELECT createdutc, statuscode, durationms FROM requesthistory" + where + ";";
            DataTable table = await Query(sql, token).ConfigureAwait(false);

            int bucketMinutes = filter.BucketMinutes;
            List<RequestHistoryBucket> buckets = new List<RequestHistoryBucket>();
            Dictionary<long, RequestHistoryBucket> bucketByIndex = new Dictionary<long, RequestHistoryBucket>();
            Dictionary<long, double> durationSumByIndex = new Dictionary<long, double>();
            Dictionary<long, long> durationCountByIndex = new Dictionary<long, long>();

            long index = 0;
            DateTime cursor = fromUtc;
            while (cursor < toUtc)
            {
                DateTime bucketEnd = cursor.AddMinutes(bucketMinutes);
                if (bucketEnd > toUtc) bucketEnd = toUtc;
                RequestHistoryBucket bucket = new RequestHistoryBucket
                {
                    BucketStartUtc = cursor,
                    BucketEndUtc = bucketEnd
                };
                buckets.Add(bucket);
                bucketByIndex[index] = bucket;
                durationSumByIndex[index] = 0;
                durationCountByIndex[index] = 0;
                cursor = bucketEnd;
                index++;
            }

            long totalCount = 0;
            long totalSuccess = 0;
            long totalFailure = 0;
            double totalDurationSum = 0;

            foreach (DataRow row in table.Rows)
            {
                DateTime created = RowReader.GetDateTime(row, "createdutc");
                if (created < fromUtc || created >= toUtc) continue;
                int statusCode = RowReader.GetInt(row, "statuscode");
                double duration = RowReader.GetDouble(row, "durationms");

                long bucketIndex = (long)((created - fromUtc).TotalMinutes / bucketMinutes);
                if (bucketIndex < 0) bucketIndex = 0;
                if (!bucketByIndex.ContainsKey(bucketIndex)) continue;

                bool success = statusCode >= 200 && statusCode <= 399;
                RequestHistoryBucket bucket = bucketByIndex[bucketIndex];
                if (success) bucket.SuccessCount++;
                else bucket.FailureCount++;
                durationSumByIndex[bucketIndex] += duration;
                durationCountByIndex[bucketIndex]++;

                totalCount++;
                if (success) totalSuccess++;
                else totalFailure++;
                totalDurationSum += duration;
            }

            foreach (KeyValuePair<long, RequestHistoryBucket> kvp in bucketByIndex)
            {
                long count = durationCountByIndex[kvp.Key];
                kvp.Value.AverageDurationMs = count == 0 ? 0 : durationSumByIndex[kvp.Key] / count;
            }

            RequestHistorySummary summary = new RequestHistorySummary
            {
                TotalCount = totalCount,
                TotalSuccess = totalSuccess,
                TotalFailure = totalFailure,
                AverageDurationMs = totalCount == 0 ? 0 : totalDurationSum / totalCount,
                Buckets = buckets
            };
            return summary;
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string? tenantId, string id, CancellationToken token = default)
        {
            string filter = tenantId == null ? String.Empty : " AND tenantid = " + Sanitizer.Str(tenantId);
            return DeleteIfExistsAsync(
                "SELECT id FROM requesthistory WHERE id = " + Sanitizer.Str(id) + filter + ";",
                "DELETE FROM requesthistory WHERE id = " + Sanitizer.Str(id) + filter + ";",
                token);
        }

        /// <inheritdoc />
        public async Task<int> DeleteManyAsync(RequestHistoryFilter filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            string where = BuildWhere(filter);
            DataTable count = await Query("SELECT COUNT(*) AS cnt FROM requesthistory" + where + ";", token).ConfigureAwait(false);
            int affected = count.Rows.Count == 0 ? 0 : RowReader.GetInt(count.Rows[0], "cnt");
            if (affected == 0) return 0;
            await Query("DELETE FROM requesthistory" + where + ";", token).ConfigureAwait(false);
            return affected;
        }

        /// <inheritdoc />
        public async Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken token = default)
        {
            string where = " WHERE createdutc < " + Sanitizer.Ts(olderThanUtc);
            DataTable count = await Query("SELECT COUNT(*) AS cnt FROM requesthistory" + where + ";", token).ConfigureAwait(false);
            int affected = count.Rows.Count == 0 ? 0 : RowReader.GetInt(count.Rows[0], "cnt");
            if (affected == 0) return 0;
            await Query("DELETE FROM requesthistory" + where + ";", token).ConfigureAwait(false);
            return affected;
        }

        private static string BuildWhere(RequestHistoryFilter filter)
        {
            List<string> conditions = new List<string>();
            if (filter.TenantId != null) conditions.Add("tenantid = " + Sanitizer.Str(filter.TenantId));
            if (filter.UserId != null) conditions.Add("userid = " + Sanitizer.Str(filter.UserId));
            if (filter.Method != null) conditions.Add("method = " + Sanitizer.Str(filter.Method));
            if (filter.StatusCode.HasValue) conditions.Add("statuscode = " + filter.StatusCode.Value.ToString(CultureInfo.InvariantCulture));
            if (!String.IsNullOrEmpty(filter.PathContains)) conditions.Add("path LIKE '%" + filter.PathContains.Replace("'", "''") + "%'");
            if (filter.FromUtc.HasValue) conditions.Add("createdutc >= " + Sanitizer.Ts(filter.FromUtc));
            if (filter.ToUtc.HasValue) conditions.Add("createdutc <= " + Sanitizer.Ts(filter.ToUtc));

            if (conditions.Count == 0) return String.Empty;
            return " WHERE " + String.Join(" AND ", conditions);
        }

        private static Dictionary<string, string> DeserializeHeaders(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
            try
            {
                Dictionary<string, string>? parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return parsed ?? new Dictionary<string, string>();
            }
            catch (JsonException)
            {
                return new Dictionary<string, string>();
            }
        }

        internal static RequestHistoryEntry MapFull(DataRow row)
        {
            return new RequestHistoryEntry
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetNullableString(row, "tenantid"),
                UserId = RowReader.GetNullableString(row, "userid"),
                PrincipalName = RowReader.GetNullableString(row, "principalname"),
                Method = RowReader.GetString(row, "method"),
                Path = RowReader.GetString(row, "path"),
                Url = RowReader.GetString(row, "url"),
                StatusCode = RowReader.GetInt(row, "statuscode"),
                DurationMs = RowReader.GetDouble(row, "durationms"),
                SourceIp = RowReader.GetNullableString(row, "sourceip"),
                RequestHeaders = DeserializeHeaders(RowReader.GetNullableString(row, "requestheaders")),
                RequestBody = RowReader.GetNullableString(row, "requestbody"),
                RequestBodyBytes = RowReader.GetLong(row, "requestbodybytes"),
                RequestBodyTruncated = RowReader.GetBool(row, "requestbodytruncated"),
                ResponseHeaders = DeserializeHeaders(RowReader.GetNullableString(row, "responseheaders")),
                ResponseBody = RowReader.GetNullableString(row, "responsebody"),
                ResponseBodyBytes = RowReader.GetLong(row, "responsebodybytes"),
                ResponseBodyTruncated = RowReader.GetBool(row, "responsebodytruncated"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                CompletedUtc = RowReader.GetNullableDateTime(row, "completedutc")
            };
        }

        internal static RequestHistoryEntry MapList(DataRow row)
        {
            return new RequestHistoryEntry
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetNullableString(row, "tenantid"),
                UserId = RowReader.GetNullableString(row, "userid"),
                PrincipalName = RowReader.GetNullableString(row, "principalname"),
                Method = RowReader.GetString(row, "method"),
                Path = RowReader.GetString(row, "path"),
                Url = RowReader.GetString(row, "url"),
                StatusCode = RowReader.GetInt(row, "statuscode"),
                DurationMs = RowReader.GetDouble(row, "durationms"),
                SourceIp = RowReader.GetNullableString(row, "sourceip"),
                RequestHeaders = new Dictionary<string, string>(),
                RequestBody = null,
                RequestBodyBytes = RowReader.GetLong(row, "requestbodybytes"),
                RequestBodyTruncated = RowReader.GetBool(row, "requestbodytruncated"),
                ResponseHeaders = new Dictionary<string, string>(),
                ResponseBody = null,
                ResponseBodyBytes = RowReader.GetLong(row, "responsebodybytes"),
                ResponseBodyTruncated = RowReader.GetBool(row, "responsebodytruncated"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                CompletedUtc = RowReader.GetNullableDateTime(row, "completedutc")
            };
        }
    }
}
