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

    /// <summary>SQL Server model runner methods.</summary>
    internal class ModelRunnerMethods : SqlServerMethodsBase, IModelRunnerMethods
    {
        internal ModelRunnerMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<ModelRunner> CreateAsync(ModelRunner runner, CancellationToken token = default)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            runner.CreatedUtc = DateTime.UtcNow;
            runner.LastUpdateUtc = runner.CreatedUtc;

            string sql =
                "INSERT INTO modelrunners (id, tenantid, name, provider, baseurl, apitype, authmaterialencrypted, capabilities, runnerusage, defaultmodel, defaultembeddingmodel, deployment, apiversion, region, project, accesskeyid, sessiontokenencrypted, contextsize, maxconcurrentrequests, maxqueuedepth, maximumtimeoutms, healthcheckenabled, healthcheckurl, healthcheckmethod, healthcheckintervalms, healthchecktimeoutms, healthcheckexpectedstatuscode, healthythreshold, unhealthythreshold, healthcheckuseauth, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(runner.Id) + ", " + Sanitizer.Str(runner.TenantId) + ", " +
                Sanitizer.Str(runner.Name) + ", " + Sanitizer.Str(runner.Provider.ToString()) + ", " +
                Sanitizer.Str(runner.BaseUrl) + ", " + Sanitizer.Str(runner.ApiType) + ", " +
                Sanitizer.Str(runner.AuthMaterialEncrypted) + ", " + Sanitizer.Str(JsonColumn.FromEnums(runner.Capabilities)) + ", " +
                Sanitizer.Str(runner.Usage.ToString()) + ", " + Sanitizer.Str(runner.DefaultModel) + ", " +
                Sanitizer.Str(runner.DefaultEmbeddingModel) + ", " +
                Sanitizer.Str(runner.Deployment) + ", " + Sanitizer.Str(runner.ApiVersion) + ", " +
                Sanitizer.Str(runner.Region) + ", " + Sanitizer.Str(runner.Project) + ", " +
                Sanitizer.Str(runner.AccessKeyId) + ", " + Sanitizer.Str(runner.SessionTokenEncrypted) + ", " +
                Sanitizer.Num(runner.ContextSize) + ", " +
                Sanitizer.Num(runner.MaxConcurrentRequests) + ", " + Sanitizer.Num(runner.MaxQueueDepth) + ", " + Sanitizer.Num(runner.MaximumTimeoutMs) + ", " +
                Sanitizer.Bit(runner.HealthCheckEnabled) + ", " + Sanitizer.Str(runner.HealthCheckUrl) + ", " + Sanitizer.Str(runner.HealthCheckMethod) + ", " +
                Sanitizer.Num(runner.HealthCheckIntervalMs) + ", " + Sanitizer.Num(runner.HealthCheckTimeoutMs) + ", " + Sanitizer.Num(runner.HealthCheckExpectedStatusCode) + ", " +
                Sanitizer.Num(runner.HealthyThreshold) + ", " + Sanitizer.Num(runner.UnhealthyThreshold) + ", " + Sanitizer.Bit(runner.HealthCheckUseAuth) + ", " +
                Sanitizer.Bit(runner.Active) + ", " +
                Sanitizer.Bit(runner.IsProtected) + ", " + Sanitizer.Ts(runner.CreatedUtc) + ", " +
                Sanitizer.Ts(runner.LastUpdateUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return runner;
        }

        /// <inheritdoc />
        public async Task<ModelRunner?> ReadAsync(string id, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM modelrunners WHERE id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<ModelRunner?> ReadByNameAsync(string? tenantId, string name, CancellationToken token = default)
        {
            if (tenantId != null)
            {
                DataTable scoped = await Query(
                    "SELECT TOP 1 * FROM modelrunners WHERE name = " + Sanitizer.Str(name) + " AND tenantid = " + Sanitizer.Str(tenantId) + ";",
                    token).ConfigureAwait(false);
                if (scoped.Rows.Count > 0) return Map(scoped.Rows[0]);
            }

            DataTable global = await Query(
                "SELECT TOP 1 * FROM modelrunners WHERE name = " + Sanitizer.Str(name) + " AND tenantid IS NULL;",
                token).ConfigureAwait(false);
            if (global.Rows.Count > 0) return Map(global.Rows[0]);
            return null;
        }

        /// <inheritdoc />
        public async Task<List<ModelRunner>> EnumerateAsync(string? tenantId, CancellationToken token = default)
        {
            string sql;
            if (tenantId == null)
            {
                sql = "SELECT * FROM modelrunners WHERE tenantid IS NULL ORDER BY createdutc ASC;";
            }
            else
            {
                sql = "SELECT * FROM modelrunners WHERE (tenantid IS NULL OR tenantid = " + Sanitizer.Str(tenantId) + ") ORDER BY createdutc ASC;";
            }

            DataTable table = await Query(sql, token).ConfigureAwait(false);
            List<ModelRunner> result = new List<ModelRunner>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<ModelRunner> UpdateAsync(ModelRunner runner, CancellationToken token = default)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            runner.LastUpdateUtc = DateTime.UtcNow;

            string sql =
                "UPDATE modelrunners SET tenantid = " + Sanitizer.Str(runner.TenantId) +
                ", name = " + Sanitizer.Str(runner.Name) +
                ", provider = " + Sanitizer.Str(runner.Provider.ToString()) +
                ", baseurl = " + Sanitizer.Str(runner.BaseUrl) +
                ", apitype = " + Sanitizer.Str(runner.ApiType) +
                ", authmaterialencrypted = " + Sanitizer.Str(runner.AuthMaterialEncrypted) +
                ", capabilities = " + Sanitizer.Str(JsonColumn.FromEnums(runner.Capabilities)) +
                ", runnerusage = " + Sanitizer.Str(runner.Usage.ToString()) +
                ", defaultmodel = " + Sanitizer.Str(runner.DefaultModel) +
                ", defaultembeddingmodel = " + Sanitizer.Str(runner.DefaultEmbeddingModel) +
                ", deployment = " + Sanitizer.Str(runner.Deployment) +
                ", apiversion = " + Sanitizer.Str(runner.ApiVersion) +
                ", region = " + Sanitizer.Str(runner.Region) +
                ", project = " + Sanitizer.Str(runner.Project) +
                ", accesskeyid = " + Sanitizer.Str(runner.AccessKeyId) +
                ", sessiontokenencrypted = " + Sanitizer.Str(runner.SessionTokenEncrypted) +
                ", contextsize = " + Sanitizer.Num(runner.ContextSize) +
                ", maxconcurrentrequests = " + Sanitizer.Num(runner.MaxConcurrentRequests) +
                ", maxqueuedepth = " + Sanitizer.Num(runner.MaxQueueDepth) +
                ", maximumtimeoutms = " + Sanitizer.Num(runner.MaximumTimeoutMs) +
                ", healthcheckenabled = " + Sanitizer.Bit(runner.HealthCheckEnabled) +
                ", healthcheckurl = " + Sanitizer.Str(runner.HealthCheckUrl) +
                ", healthcheckmethod = " + Sanitizer.Str(runner.HealthCheckMethod) +
                ", healthcheckintervalms = " + Sanitizer.Num(runner.HealthCheckIntervalMs) +
                ", healthchecktimeoutms = " + Sanitizer.Num(runner.HealthCheckTimeoutMs) +
                ", healthcheckexpectedstatuscode = " + Sanitizer.Num(runner.HealthCheckExpectedStatusCode) +
                ", healthythreshold = " + Sanitizer.Num(runner.HealthyThreshold) +
                ", unhealthythreshold = " + Sanitizer.Num(runner.UnhealthyThreshold) +
                ", healthcheckuseauth = " + Sanitizer.Bit(runner.HealthCheckUseAuth) +
                ", active = " + Sanitizer.Bit(runner.Active) +
                ", isprotected = " + Sanitizer.Bit(runner.IsProtected) +
                ", lastupdateutc = " + Sanitizer.Ts(runner.LastUpdateUtc) +
                " WHERE id = " + Sanitizer.Str(runner.Id) + ";";
            await Query(sql, token).ConfigureAwait(false);
            return runner;
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            return DeleteIfExistsAsync(
                "SELECT id FROM modelrunners WHERE id = " + Sanitizer.Str(id) + ";",
                "DELETE FROM modelrunners WHERE id = " + Sanitizer.Str(id) + ";",
                token);
        }

        /// <inheritdoc />
        public async Task<List<ModelRunner>> EnumerateAllAsync(CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM modelrunners ORDER BY createdutc ASC;", token).ConfigureAwait(false);
            List<ModelRunner> result = new List<ModelRunner>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAnyAsync(CancellationToken token = default)
        {
            DataTable table = await Query("SELECT TOP 1 id FROM modelrunners;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        internal static ModelRunner Map(DataRow row)
        {
            return new ModelRunner
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetNullableString(row, "tenantid"),
                Name = RowReader.GetString(row, "name"),
                Provider = RowReader.GetEnum<ModelRunnerProviderEnum>(row, "provider", ModelRunnerProviderEnum.Ollama),
                BaseUrl = RowReader.GetString(row, "baseurl"),
                ApiType = RowReader.GetNullableString(row, "apitype"),
                AuthMaterialEncrypted = RowReader.GetNullableString(row, "authmaterialencrypted"),
                Capabilities = RowReader.GetEnumList<ModelCapabilityEnum>(row, "capabilities"),
                Usage = RowReader.GetEnum<ModelRunnerUsageEnum>(row, "runnerusage", ModelRunnerUsageEnum.Both),
                DefaultModel = RowReader.GetNullableString(row, "defaultmodel"),
                DefaultEmbeddingModel = RowReader.GetNullableString(row, "defaultembeddingmodel"),
                Deployment = RowReader.GetNullableString(row, "deployment"),
                ApiVersion = RowReader.GetNullableString(row, "apiversion"),
                Region = RowReader.GetNullableString(row, "region"),
                Project = RowReader.GetNullableString(row, "project"),
                AccessKeyId = RowReader.GetNullableString(row, "accesskeyid"),
                SessionTokenEncrypted = RowReader.GetNullableString(row, "sessiontokenencrypted"),
                ContextSize = RowReader.GetInt(row, "contextsize"),
                MaxConcurrentRequests = RowReader.GetInt(row, "maxconcurrentrequests"),
                MaxQueueDepth = RowReader.GetInt(row, "maxqueuedepth"),
                MaximumTimeoutMs = RowReader.GetInt(row, "maximumtimeoutms"),
                HealthCheckEnabled = RowReader.GetBool(row, "healthcheckenabled"),
                HealthCheckUrl = RowReader.GetNullableString(row, "healthcheckurl"),
                HealthCheckMethod = RowReader.GetNullableString(row, "healthcheckmethod"),
                HealthCheckIntervalMs = RowReader.GetInt(row, "healthcheckintervalms"),
                HealthCheckTimeoutMs = RowReader.GetInt(row, "healthchecktimeoutms"),
                HealthCheckExpectedStatusCode = RowReader.GetInt(row, "healthcheckexpectedstatuscode"),
                HealthyThreshold = RowReader.GetInt(row, "healthythreshold"),
                UnhealthyThreshold = RowReader.GetInt(row, "unhealthythreshold"),
                HealthCheckUseAuth = RowReader.GetBool(row, "healthcheckuseauth"),
                Active = RowReader.GetBool(row, "active"),
                IsProtected = RowReader.GetBool(row, "isprotected"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
