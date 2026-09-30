namespace Pneuma.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Database.Interfaces;
    using Pneuma.Core.Ontologies;

    /// <summary>PostgreSQL tenant ontology methods.</summary>
    internal class OntologyMethods : PostgresqlMethodsBase, IOntologyMethods
    {
        internal OntologyMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<TenantOntology> CreateAsync(TenantOntology ontology, CancellationToken token = default)
        {
            if (ontology == null) throw new ArgumentNullException(nameof(ontology));
            DateTime now = DateTime.UtcNow;
            ontology.CreatedUtc = now;
            ontology.LastUpdateUtc = now;
            await Query(
                "INSERT INTO ontologies (id, tenantid, name, description, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(ontology.Id) + ", " + Sanitizer.Str(ontology.TenantId) + ", " + Sanitizer.Str(ontology.Name) + ", " +
                Sanitizer.Str(ontology.Description) + ", " + Sanitizer.Ts(ontology.CreatedUtc) + ", " + Sanitizer.Ts(ontology.LastUpdateUtc) + ");", token).ConfigureAwait(false);
            return ontology;
        }

        /// <inheritdoc />
        public async Task<TenantOntology?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM ontologies WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<TenantOntology?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM ontologies WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND name = " + Sanitizer.Str(name) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<TenantOntology>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT * FROM ontologies WHERE tenantid = " + Sanitizer.Str(tenantId) + " ORDER BY name ASC;", token).ConfigureAwait(false);
            List<TenantOntology> result = new List<TenantOntology>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<TenantOntology> UpdateAsync(TenantOntology ontology, CancellationToken token = default)
        {
            if (ontology == null) throw new ArgumentNullException(nameof(ontology));
            ontology.LastUpdateUtc = DateTime.UtcNow;
            await Query(
                "UPDATE ontologies SET name = " + Sanitizer.Str(ontology.Name) + ", description = " + Sanitizer.Str(ontology.Description) +
                ", lastupdateutc = " + Sanitizer.Ts(ontology.LastUpdateUtc) +
                " WHERE tenantid = " + Sanitizer.Str(ontology.TenantId) + " AND id = " + Sanitizer.Str(ontology.Id) + ";", token).ConfigureAwait(false);
            return ontology;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable existing = await Query(
                "SELECT id FROM ontologies WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (existing.Rows.Count == 0) return false;

            string t = Sanitizer.Str(tenantId);
            string versions = "(SELECT id FROM ontologyversions WHERE tenantid = " + t + " AND ontologyid = " + Sanitizer.Str(id) + ")";
            List<string> statements = OntologyVersionMethods.DeleteContentsSql(tenantId, versions);
            statements.Add("DELETE FROM ontologyversions WHERE tenantid = " + t + " AND ontologyid = " + Sanitizer.Str(id) + ";");
            statements.Add("DELETE FROM ontologies WHERE tenantid = " + t + " AND id = " + Sanitizer.Str(id) + ";");
            await QueryTransaction(statements, token).ConfigureAwait(false);
            return true;
        }

        /// <summary>Statements that delete every ontology row of a tenant (ontologies, versions, contents, violations, operations, cache index).</summary>
        internal static List<string> DeleteByTenantSql(string tenantId)
        {
            string t = Sanitizer.Str(tenantId);
            return new List<string>
            {
                "DELETE FROM ontologyconceptlabels WHERE tenantid = " + t + ";",
                "DELETE FROM ontologyconcepts WHERE tenantid = " + t + ";",
                "DELETE FROM ontologyrules WHERE tenantid = " + t + ";",
                "DELETE FROM ontologyedgetypes WHERE tenantid = " + t + ";",
                "DELETE FROM ontologynodetypes WHERE tenantid = " + t + ";",
                "DELETE FROM ontologyversions WHERE tenantid = " + t + ";",
                "DELETE FROM ontologies WHERE tenantid = " + t + ";",
                "DELETE FROM ontologyviolations WHERE tenantid = " + t + ";",
                "DELETE FROM ontologyoperationitems WHERE tenantid = " + t + ";",
                "DELETE FROM ontologyoperations WHERE tenantid = " + t + ";",
                "DELETE FROM classificationcache WHERE tenantid = " + t + ";"
            };
        }

        /// <summary>Statements that delete a subject's ontology rows (violations, operations, and operation items).</summary>
        internal static List<string> DeleteBySubjectSql(string tenantId, string subjectId)
        {
            string t = Sanitizer.Str(tenantId);
            string s = Sanitizer.Str(subjectId);
            return new List<string>
            {
                "DELETE FROM ontologyviolations WHERE tenantid = " + t + " AND subjectid = " + s + ";",
                "DELETE FROM ontologyoperationitems WHERE tenantid = " + t + " AND operationid IN (SELECT id FROM ontologyoperations WHERE tenantid = " + t + " AND subjectid = " + s + ");",
                "DELETE FROM ontologyoperations WHERE tenantid = " + t + " AND subjectid = " + s + ";"
            };
        }

        /// <summary>Statement that deletes the violations an ingestion job recorded.</summary>
        internal static string DeleteViolationsByJobSql(string tenantId, string jobId)
        {
            return "DELETE FROM ontologyviolations WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND jobid = " + Sanitizer.Str(jobId) + ";";
        }

        internal static TenantOntology Map(DataRow row)
        {
            return new TenantOntology
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                Name = RowReader.GetString(row, "name"),
                Description = RowReader.GetNullableString(row, "description"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
