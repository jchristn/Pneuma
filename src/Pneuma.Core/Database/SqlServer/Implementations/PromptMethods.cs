namespace Pneuma.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Models;

    /// <summary>SQL Server prompt methods.</summary>
    internal class PromptMethods : SqlServerMethodsBase, IPromptMethods
    {
        internal PromptMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<Prompt> CreateAsync(Prompt prompt, CancellationToken token = default)
        {
            if (prompt == null) throw new ArgumentNullException(nameof(prompt));
            prompt.CreatedUtc = DateTime.UtcNow;
            prompt.LastUpdateUtc = prompt.CreatedUtc;

            string sql =
                "INSERT INTO prompts (id, tenantid, promptkey, name, content, version, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(prompt.Id) + ", " + Sanitizer.Str(prompt.TenantId) + ", " +
                Sanitizer.Str(prompt.Key) + ", " + Sanitizer.Str(prompt.Name) + ", " +
                Sanitizer.Str(prompt.Content) + ", " + prompt.Version.ToString(CultureInfo.InvariantCulture) + ", " +
                Sanitizer.Bit(prompt.Active) + ", " + Sanitizer.Bit(prompt.IsProtected) + ", " +
                Sanitizer.Ts(prompt.CreatedUtc) + ", " + Sanitizer.Ts(prompt.LastUpdateUtc) + ");";
            await Query(sql, token).ConfigureAwait(false);
            return prompt;
        }

        /// <inheritdoc />
        public async Task<Prompt?> ReadAsync(string id, CancellationToken token = default)
        {
            DataTable table = await Query("SELECT * FROM prompts WHERE id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<Prompt?> ReadByKeyAsync(string? tenantId, string key, CancellationToken token = default)
        {
            if (tenantId != null)
            {
                DataTable scoped = await Query(
                    "SELECT TOP 1 * FROM prompts WHERE promptkey = " + Sanitizer.Str(key) + " AND tenantid = " + Sanitizer.Str(tenantId) + ";",
                    token).ConfigureAwait(false);
                if (scoped.Rows.Count > 0) return Map(scoped.Rows[0]);
            }

            DataTable global = await Query(
                "SELECT TOP 1 * FROM prompts WHERE promptkey = " + Sanitizer.Str(key) + " AND tenantid IS NULL;",
                token).ConfigureAwait(false);
            if (global.Rows.Count > 0) return Map(global.Rows[0]);
            return null;
        }

        /// <inheritdoc />
        public async Task<List<Prompt>> EnumerateAsync(string? tenantId, CancellationToken token = default)
        {
            string sql;
            if (tenantId == null)
            {
                sql = "SELECT * FROM prompts WHERE tenantid IS NULL ORDER BY createdutc ASC;";
            }
            else
            {
                sql = "SELECT * FROM prompts WHERE (tenantid IS NULL OR tenantid = " + Sanitizer.Str(tenantId) + ") ORDER BY createdutc ASC;";
            }

            DataTable table = await Query(sql, token).ConfigureAwait(false);
            List<Prompt> result = new List<Prompt>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<Prompt> UpdateAsync(Prompt prompt, CancellationToken token = default)
        {
            if (prompt == null) throw new ArgumentNullException(nameof(prompt));
            prompt.LastUpdateUtc = DateTime.UtcNow;

            string sql =
                "UPDATE prompts SET tenantid = " + Sanitizer.Str(prompt.TenantId) +
                ", promptkey = " + Sanitizer.Str(prompt.Key) +
                ", name = " + Sanitizer.Str(prompt.Name) +
                ", content = " + Sanitizer.Str(prompt.Content) +
                ", version = " + prompt.Version.ToString(CultureInfo.InvariantCulture) +
                ", active = " + Sanitizer.Bit(prompt.Active) +
                ", isprotected = " + Sanitizer.Bit(prompt.IsProtected) +
                ", lastupdateutc = " + Sanitizer.Ts(prompt.LastUpdateUtc) +
                " WHERE id = " + Sanitizer.Str(prompt.Id) + ";";
            await Query(sql, token).ConfigureAwait(false);
            return prompt;
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            return DeleteIfExistsAsync(
                "SELECT id FROM prompts WHERE id = " + Sanitizer.Str(id) + ";",
                "DELETE FROM prompts WHERE id = " + Sanitizer.Str(id) + ";",
                token);
        }

        internal static Prompt Map(DataRow row)
        {
            return new Prompt
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetNullableString(row, "tenantid"),
                Key = RowReader.GetString(row, "promptkey"),
                Name = RowReader.GetString(row, "name"),
                Content = RowReader.GetString(row, "content"),
                Version = RowReader.GetInt(row, "version"),
                Active = RowReader.GetBool(row, "active"),
                IsProtected = RowReader.GetBool(row, "isprotected"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
