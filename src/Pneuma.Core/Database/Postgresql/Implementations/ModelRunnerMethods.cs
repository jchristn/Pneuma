namespace Pneuma.Core.Database.Postgresql.Implementations
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

    /// <summary>PostgreSQL model runner methods.</summary>
    internal class ModelRunnerMethods : PostgresqlMethodsBase, IModelRunnerMethods
    {
        internal ModelRunnerMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<ModelRunner> CreateAsync(ModelRunner runner, CancellationToken token = default)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            runner.CreatedUtc = DateTime.UtcNow;
            runner.LastUpdateUtc = runner.CreatedUtc;

            string sql =
                "INSERT INTO modelrunners (id, tenantid, name, provider, baseurl, apitype, authmaterialencrypted, capabilities, runnerusage, defaultmodel, defaultembeddingmodel, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(runner.Id) + ", " + Sanitizer.Str(runner.TenantId) + ", " +
                Sanitizer.Str(runner.Name) + ", " + Sanitizer.Str(runner.Provider.ToString()) + ", " +
                Sanitizer.Str(runner.BaseUrl) + ", " + Sanitizer.Str(runner.ApiType) + ", " +
                Sanitizer.Str(runner.AuthMaterialEncrypted) + ", " + Sanitizer.Str(JsonColumn.FromEnums(runner.Capabilities)) + ", " +
                Sanitizer.Str(runner.Usage.ToString()) + ", " + Sanitizer.Str(runner.DefaultModel) + ", " +
                Sanitizer.Str(runner.DefaultEmbeddingModel) + ", " + Sanitizer.Bit(runner.Active) + ", " +
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
                    "SELECT * FROM modelrunners WHERE name = " + Sanitizer.Str(name) + " AND tenantid = " + Sanitizer.Str(tenantId) + " LIMIT 1;",
                    token).ConfigureAwait(false);
                if (scoped.Rows.Count > 0) return Map(scoped.Rows[0]);
            }

            DataTable global = await Query(
                "SELECT * FROM modelrunners WHERE name = " + Sanitizer.Str(name) + " AND tenantid IS NULL LIMIT 1;",
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
                Active = RowReader.GetBool(row, "active"),
                IsProtected = RowReader.GetBool(row, "isprotected"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
