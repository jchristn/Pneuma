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

    /// <summary>MySQL ontology version methods.</summary>
    internal class OntologyVersionMethods : MysqlMethodsBase, IOntologyVersionMethods
    {
        private const string _CountColumns =
            ", (SELECT COUNT(*) FROM ontologynodetypes n WHERE n.tenantid = v.tenantid AND n.versionid = v.id) AS nodetypecount" +
            ", (SELECT COUNT(*) FROM ontologyedgetypes e WHERE e.tenantid = v.tenantid AND e.versionid = v.id) AS edgetypecount" +
            ", (SELECT COUNT(*) FROM ontologyrules r WHERE r.tenantid = v.tenantid AND r.versionid = v.id) AS rulecount" +
            ", (SELECT COUNT(*) FROM ontologyconcepts c WHERE c.tenantid = v.tenantid AND c.versionid = v.id) AS conceptcount";

        internal OntologyVersionMethods(DatabaseDriverBase db) : base(db) { }

        /// <inheritdoc />
        public async Task<OntologyVersion> CreateAsync(OntologyVersion version, CancellationToken token = default)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            DateTime now = DateTime.UtcNow;
            version.CreatedUtc = now;
            version.LastUpdateUtc = now;
            List<string> statements = new List<string>
            {
                "INSERT INTO ontologyversions (id, tenantid, ontologyid, versionnumber, status, guidance, undeclaredtypeaction, changesummary, basedonversionid, " +
                "createdbyuserid, approvedbyuserid, approvedutc, retiredutc, createdutc, lastupdateutc) VALUES (" +
                Sanitizer.Str(version.Id) + ", " + Sanitizer.Str(version.TenantId) + ", " + Sanitizer.Str(version.OntologyId) + ", " +
                Sanitizer.Num(version.VersionNumber) + ", " + Sanitizer.Str(version.Status.ToString()) + ", " + Sanitizer.Str(version.Guidance) + ", " +
                Sanitizer.Str(version.UndeclaredTypeAction.ToString()) + ", " + Sanitizer.Str(version.ChangeSummary) + ", " + Sanitizer.Str(version.BasedOnVersionId) + ", " +
                Sanitizer.Str(version.CreatedByUserId) + ", " + Sanitizer.Str(version.ApprovedByUserId) + ", " + Sanitizer.Ts(version.ApprovedUtc) + ", " +
                Sanitizer.Ts(version.RetiredUtc) + ", " + Sanitizer.Ts(version.CreatedUtc) + ", " + Sanitizer.Ts(version.LastUpdateUtc) + ");"
            };
            statements.AddRange(ContentsInsertSql(version));
            await QueryTransaction(statements, token).ConfigureAwait(false);
            return version;
        }

        /// <inheritdoc />
        public async Task<OntologyVersion?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT v.*" + _CountColumns + " FROM ontologyversions v WHERE v.tenantid = " + Sanitizer.Str(tenantId) + " AND v.id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            OntologyVersion version = Map(table.Rows[0]);
            await LoadContentsAsync(version, token).ConfigureAwait(false);
            return version;
        }

        /// <inheritdoc />
        public async Task<List<OntologyVersion>> EnumerateAsync(string tenantId, string ontologyId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT v.*" + _CountColumns + " FROM ontologyversions v WHERE v.tenantid = " + Sanitizer.Str(tenantId) + " AND v.ontologyid = " + Sanitizer.Str(ontologyId) +
                " ORDER BY v.versionnumber DESC;", token).ConfigureAwait(false);
            List<OntologyVersion> result = new List<OntologyVersion>();
            foreach (DataRow row in table.Rows) result.Add(Map(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<int> NextVersionNumberAsync(string tenantId, string ontologyId, CancellationToken token = default)
        {
            DataTable table = await Query(
                "SELECT MAX(versionnumber) AS highest FROM ontologyversions WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND ontologyid = " + Sanitizer.Str(ontologyId) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return 1;
            return RowReader.GetInt(table.Rows[0], "highest") + 1;
        }

        /// <inheritdoc />
        public async Task<OntologyVersion> UpdateAsync(OntologyVersion version, CancellationToken token = default)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            version.LastUpdateUtc = DateTime.UtcNow;
            List<string> statements = new List<string>
            {
                "UPDATE ontologyversions SET guidance = " + Sanitizer.Str(version.Guidance) +
                ", undeclaredtypeaction = " + Sanitizer.Str(version.UndeclaredTypeAction.ToString()) +
                ", changesummary = " + Sanitizer.Str(version.ChangeSummary) +
                ", lastupdateutc = " + Sanitizer.Ts(version.LastUpdateUtc) +
                " WHERE tenantid = " + Sanitizer.Str(version.TenantId) + " AND id = " + Sanitizer.Str(version.Id) + ";"
            };
            statements.AddRange(DeleteContentsSql(version.TenantId, "(" + Sanitizer.Str(version.Id) + ")"));
            statements.AddRange(ContentsInsertSql(version));
            await QueryTransaction(statements, token).ConfigureAwait(false);
            return version;
        }

        /// <inheritdoc />
        public async Task UpdateStatusAsync(OntologyVersion version, CancellationToken token = default)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            version.LastUpdateUtc = DateTime.UtcNow;
            await Query(
                "UPDATE ontologyversions SET status = " + Sanitizer.Str(version.Status.ToString()) +
                ", changesummary = " + Sanitizer.Str(version.ChangeSummary) +
                ", approvedbyuserid = " + Sanitizer.Str(version.ApprovedByUserId) +
                ", approvedutc = " + Sanitizer.Ts(version.ApprovedUtc) +
                ", retiredutc = " + Sanitizer.Ts(version.RetiredUtc) +
                ", lastupdateutc = " + Sanitizer.Ts(version.LastUpdateUtc) +
                " WHERE tenantid = " + Sanitizer.Str(version.TenantId) + " AND id = " + Sanitizer.Str(version.Id) + ";", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            DataTable existing = await Query(
                "SELECT id FROM ontologyversions WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";", token).ConfigureAwait(false);
            if (existing.Rows.Count == 0) return false;
            List<string> statements = DeleteContentsSql(tenantId, "(" + Sanitizer.Str(id) + ")");
            statements.Add("DELETE FROM ontologyversions WHERE tenantid = " + Sanitizer.Str(tenantId) + " AND id = " + Sanitizer.Str(id) + ";");
            await QueryTransaction(statements, token).ConfigureAwait(false);
            return true;
        }

        /// <summary>Statements that delete the contents of the versions named by a parenthesized id list or subquery.</summary>
        internal static List<string> DeleteContentsSql(string tenantId, string versionIds)
        {
            string t = Sanitizer.Str(tenantId);
            return new List<string>
            {
                "DELETE FROM ontologyconceptlabels WHERE tenantid = " + t + " AND versionid IN " + versionIds + ";",
                "DELETE FROM ontologyconcepts WHERE tenantid = " + t + " AND versionid IN " + versionIds + ";",
                "DELETE FROM ontologyrules WHERE tenantid = " + t + " AND versionid IN " + versionIds + ";",
                "DELETE FROM ontologyedgetypes WHERE tenantid = " + t + " AND versionid IN " + versionIds + ";",
                "DELETE FROM ontologynodetypes WHERE tenantid = " + t + " AND versionid IN " + versionIds + ";"
            };
        }

        private static List<string> ContentsInsertSql(OntologyVersion version)
        {
            List<string> statements = new List<string>();
            string t = Sanitizer.Str(version.TenantId);
            string v = Sanitizer.Str(version.Id);
            for (int i = 0; i < version.NodeTypes.Count; i++)
            {
                OntologyNodeType type = version.NodeTypes[i];
                statements.Add("INSERT INTO ontologynodetypes (tenantid, versionid, ordinal, name, description) VALUES (" + t + ", " + v + ", " +
                    Sanitizer.Num(i) + ", " + Sanitizer.Str(type.Name) + ", " + Sanitizer.Str(type.Description) + ");");
            }
            for (int i = 0; i < version.EdgeTypes.Count; i++)
            {
                OntologyEdgeType type = version.EdgeTypes[i];
                statements.Add("INSERT INTO ontologyedgetypes (tenantid, versionid, ordinal, name, description) VALUES (" + t + ", " + v + ", " +
                    Sanitizer.Num(i) + ", " + Sanitizer.Str(type.Name) + ", " + Sanitizer.Str(type.Description) + ");");
            }
            for (int i = 0; i < version.Rules.Count; i++)
            {
                OntologyRule rule = version.Rules[i];
                statements.Add("INSERT INTO ontologyrules (id, tenantid, versionid, ordinal, ruletype, nodetype, edgetype, fromnodetype, tonodetype, fieldname, rulepattern, " +
                    "maxcount, minconfidence, ruleaction, description) VALUES (" + Sanitizer.Str(rule.Id) + ", " + t + ", " + v + ", " + Sanitizer.Num(i) + ", " +
                    Sanitizer.Str(rule.RuleType.ToString()) + ", " + Sanitizer.Str(rule.NodeType) + ", " + Sanitizer.Str(rule.EdgeType) + ", " +
                    Sanitizer.Str(rule.FromNodeType) + ", " + Sanitizer.Str(rule.ToNodeType) + ", " + Sanitizer.Str(rule.Field?.ToString()) + ", " +
                    Sanitizer.Str(rule.Pattern) + ", " + Sanitizer.Num(rule.MaxCount) + ", " + Sanitizer.Num(rule.MinConfidence) + ", " +
                    Sanitizer.Str(rule.Action.ToString()) + ", " + Sanitizer.Str(rule.Description) + ");");
            }
            for (int i = 0; i < version.Concepts.Count; i++)
            {
                OntologyConcept concept = version.Concepts[i];
                string key = Sanitizer.Str(concept.Key);
                statements.Add("INSERT INTO ontologyconcepts (tenantid, versionid, ordinal, conceptkey, preflabel, broaderkey, definition, nodetype, casesensitive) VALUES (" +
                    t + ", " + v + ", " + Sanitizer.Num(i) + ", " + key + ", " + Sanitizer.Str(concept.PrefLabel) + ", " + Sanitizer.Str(concept.BroaderKey) + ", " +
                    Sanitizer.Str(concept.Definition) + ", " + Sanitizer.Str(concept.NodeType) + ", " + Sanitizer.Bit(concept.CaseSensitive) + ");");
                for (int j = 0; j < concept.AltLabels.Count; j++)
                {
                    statements.Add("INSERT INTO ontologyconceptlabels (tenantid, versionid, conceptkey, ordinal, label) VALUES (" + t + ", " + v + ", " + key + ", " +
                        Sanitizer.Num(j) + ", " + Sanitizer.Str(concept.AltLabels[j]) + ");");
                }
            }
            return statements;
        }

        private async Task LoadContentsAsync(OntologyVersion version, CancellationToken token)
        {
            string where = " WHERE tenantid = " + Sanitizer.Str(version.TenantId) + " AND versionid = " + Sanitizer.Str(version.Id) + " ORDER BY ordinal ASC;";
            DataTable nodeTypes = await Query("SELECT name, description FROM ontologynodetypes" + where, token).ConfigureAwait(false);
            foreach (DataRow row in nodeTypes.Rows)
                version.NodeTypes.Add(new OntologyNodeType { Name = RowReader.GetString(row, "name"), Description = RowReader.GetNullableString(row, "description") });

            DataTable edgeTypes = await Query("SELECT name, description FROM ontologyedgetypes" + where, token).ConfigureAwait(false);
            foreach (DataRow row in edgeTypes.Rows)
                version.EdgeTypes.Add(new OntologyEdgeType { Name = RowReader.GetString(row, "name"), Description = RowReader.GetNullableString(row, "description") });

            DataTable rules = await Query("SELECT * FROM ontologyrules" + where, token).ConfigureAwait(false);
            foreach (DataRow row in rules.Rows) version.Rules.Add(MapRule(row));

            DataTable labels = await Query("SELECT conceptkey, label FROM ontologyconceptlabels" + where, token).ConfigureAwait(false);
            Dictionary<string, List<string>> labelsByKey = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (DataRow row in labels.Rows)
            {
                string key = RowReader.GetString(row, "conceptkey");
                List<string>? list;
                if (!labelsByKey.TryGetValue(key, out list))
                {
                    list = new List<string>();
                    labelsByKey[key] = list;
                }
                list.Add(RowReader.GetString(row, "label"));
            }

            DataTable concepts = await Query("SELECT * FROM ontologyconcepts" + where, token).ConfigureAwait(false);
            foreach (DataRow row in concepts.Rows)
            {
                string key = RowReader.GetString(row, "conceptkey");
                List<string>? alt;
                version.Concepts.Add(new OntologyConcept
                {
                    Key = key,
                    PrefLabel = RowReader.GetString(row, "preflabel"),
                    AltLabels = labelsByKey.TryGetValue(key, out alt) ? alt : new List<string>(),
                    BroaderKey = RowReader.GetNullableString(row, "broaderkey"),
                    Definition = RowReader.GetNullableString(row, "definition"),
                    NodeType = RowReader.GetString(row, "nodetype"),
                    CaseSensitive = RowReader.GetBool(row, "casesensitive")
                });
            }
        }

        private static OntologyRule MapRule(DataRow row)
        {
            return new OntologyRule
            {
                Id = RowReader.GetString(row, "id"),
                RuleType = RowReader.GetEnum<OntologyRuleTypeEnum>(row, "ruletype", OntologyRuleTypeEnum.EdgeEndpoints),
                NodeType = RowReader.GetNullableString(row, "nodetype"),
                EdgeType = RowReader.GetNullableString(row, "edgetype"),
                FromNodeType = RowReader.GetNullableString(row, "fromnodetype"),
                ToNodeType = RowReader.GetNullableString(row, "tonodetype"),
                Field = RowReader.GetNullableEnum<OntologyNodeFieldEnum>(row, "fieldname"),
                Pattern = RowReader.GetNullableString(row, "rulepattern"),
                MaxCount = RowReader.GetInt(row, "maxcount"),
                MinConfidence = RowReader.GetDouble(row, "minconfidence"),
                Action = RowReader.GetEnum<OntologyRuleActionEnum>(row, "ruleaction", OntologyRuleActionEnum.Warn),
                Description = RowReader.GetNullableString(row, "description")
            };
        }

        internal static OntologyVersion Map(DataRow row)
        {
            OntologyVersion version = new OntologyVersion
            {
                Id = RowReader.GetString(row, "id"),
                TenantId = RowReader.GetString(row, "tenantid"),
                OntologyId = RowReader.GetString(row, "ontologyid"),
                VersionNumber = RowReader.GetInt(row, "versionnumber"),
                Status = RowReader.GetEnum<OntologyVersionStatusEnum>(row, "status", OntologyVersionStatusEnum.Draft),
                Guidance = RowReader.GetNullableString(row, "guidance"),
                UndeclaredTypeAction = RowReader.GetEnum<UndeclaredTypeActionEnum>(row, "undeclaredtypeaction", UndeclaredTypeActionEnum.Allow),
                ChangeSummary = RowReader.GetNullableString(row, "changesummary"),
                BasedOnVersionId = RowReader.GetNullableString(row, "basedonversionid"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                ApprovedByUserId = RowReader.GetNullableString(row, "approvedbyuserid"),
                ApprovedUtc = RowReader.GetNullableDateTime(row, "approvedutc"),
                RetiredUtc = RowReader.GetNullableDateTime(row, "retiredutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
            if (row.Table.Columns.Contains("nodetypecount"))
            {
                version.NodeTypeCount = RowReader.GetInt(row, "nodetypecount");
                version.EdgeTypeCount = RowReader.GetInt(row, "edgetypecount");
                version.RuleCount = RowReader.GetInt(row, "rulecount");
                version.ConceptCount = RowReader.GetInt(row, "conceptcount");
            }
            return version;
        }
    }
}
