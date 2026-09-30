namespace Pneuma.Core.Database.Mysql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// MySQL statements for the ontology governance migration: tenant ontologies and their versions (node types, edge
    /// types, rules, taxonomy concepts and labels), rule violations, background operations and their items, the
    /// classification cache index, the subject's pinned version and classification settings, and the job counters.
    /// </summary>
    internal static class MysqlOntologySchema
    {
        /// <summary>Statements for migration 35.</summary>
        /// <returns>The statements, in order.</returns>
        internal static List<string> Migration35()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS ontologies (id VARCHAR(64) NOT NULL PRIMARY KEY, tenantid VARCHAR(64) NOT NULL, name VARCHAR(256) NOT NULL, description TEXT, createdutc VARCHAR(32) NOT NULL, lastupdateutc VARCHAR(32) NOT NULL, UNIQUE KEY idx_ontologies_tenant_name (tenantid, name));",
                "CREATE TABLE IF NOT EXISTS ontologyversions (id VARCHAR(64) NOT NULL PRIMARY KEY, tenantid VARCHAR(64) NOT NULL, ontologyid VARCHAR(64) NOT NULL, versionnumber INT NOT NULL DEFAULT 1, status VARCHAR(256) NOT NULL DEFAULT 'Draft', guidance TEXT, undeclaredtypeaction VARCHAR(256) NOT NULL DEFAULT 'Allow', changesummary TEXT, basedonversionid VARCHAR(64), createdbyuserid VARCHAR(64), approvedbyuserid VARCHAR(64), approvedutc VARCHAR(32), retiredutc VARCHAR(32), createdutc VARCHAR(32) NOT NULL, lastupdateutc VARCHAR(32) NOT NULL, UNIQUE KEY idx_ontologyversions_tenant_ontology_number (tenantid, ontologyid, versionnumber));",
                "CREATE TABLE IF NOT EXISTS ontologynodetypes (tenantid VARCHAR(64) NOT NULL, versionid VARCHAR(64) NOT NULL, ordinal INT NOT NULL DEFAULT 0, name VARCHAR(256) NOT NULL, description TEXT, UNIQUE KEY idx_ontologynodetypes_version_name (tenantid, versionid, name));",
                "CREATE TABLE IF NOT EXISTS ontologyedgetypes (tenantid VARCHAR(64) NOT NULL, versionid VARCHAR(64) NOT NULL, ordinal INT NOT NULL DEFAULT 0, name VARCHAR(256) NOT NULL, description TEXT, UNIQUE KEY idx_ontologyedgetypes_version_name (tenantid, versionid, name));",
                "CREATE TABLE IF NOT EXISTS ontologyrules (id VARCHAR(64) NOT NULL PRIMARY KEY, tenantid VARCHAR(64) NOT NULL, versionid VARCHAR(64) NOT NULL, ordinal INT NOT NULL DEFAULT 0, ruletype VARCHAR(256) NOT NULL, nodetype VARCHAR(256), edgetype VARCHAR(256), fromnodetype VARCHAR(256), tonodetype VARCHAR(256), fieldname VARCHAR(256), rulepattern TEXT, maxcount INT NOT NULL DEFAULT 1, minconfidence DOUBLE NOT NULL DEFAULT 0.5, ruleaction VARCHAR(256) NOT NULL DEFAULT 'Warn', description TEXT, KEY idx_ontologyrules_tenant_version (tenantid, versionid));",
                "CREATE TABLE IF NOT EXISTS ontologyconcepts (tenantid VARCHAR(64) NOT NULL, versionid VARCHAR(64) NOT NULL, ordinal INT NOT NULL DEFAULT 0, conceptkey VARCHAR(512) NOT NULL, preflabel VARCHAR(256) NOT NULL, broaderkey VARCHAR(512), definition TEXT, nodetype VARCHAR(256) NOT NULL DEFAULT 'Topic', casesensitive INT NOT NULL DEFAULT 0, UNIQUE KEY idx_ontologyconcepts_version_key (tenantid, versionid, conceptkey));",
                "CREATE TABLE IF NOT EXISTS ontologyconceptlabels (tenantid VARCHAR(64) NOT NULL, versionid VARCHAR(64) NOT NULL, conceptkey VARCHAR(512) NOT NULL, ordinal INT NOT NULL DEFAULT 0, label VARCHAR(256) NOT NULL, KEY idx_ontologyconceptlabels_tenant_version (tenantid, versionid));",
                "CREATE TABLE IF NOT EXISTS ontologyviolations (id VARCHAR(64) NOT NULL PRIMARY KEY, tenantid VARCHAR(64) NOT NULL, subjectid VARCHAR(64) NOT NULL, jobid VARCHAR(64), linkid VARCHAR(64), operationid VARCHAR(64), ontologyversionid VARCHAR(64), ruleid VARCHAR(64), ruletype VARCHAR(256), elementkind VARCHAR(256) NOT NULL DEFAULT 'Node', nodetype VARCHAR(256), nodename TEXT, edgetype VARCHAR(256), fromnodetype VARCHAR(256), fromnodename TEXT, tonodetype VARCHAR(256), tonodename TEXT, content TEXT, confidence DOUBLE NOT NULL DEFAULT 0.5, ruleaction VARCHAR(256) NOT NULL DEFAULT 'Warn', status VARCHAR(256) NOT NULL DEFAULT 'Recorded', message TEXT, resolvedbyuserid VARCHAR(64), resolvedutc VARCHAR(32), createdutc VARCHAR(32) NOT NULL, KEY idx_ontologyviolations_tenant_subject (tenantid, subjectid, createdutc), KEY idx_ontologyviolations_job (jobid), KEY idx_ontologyviolations_operation (operationid));",
                "CREATE TABLE IF NOT EXISTS ontologyoperations (id VARCHAR(64) NOT NULL PRIMARY KEY, tenantid VARCHAR(64) NOT NULL, subjectid VARCHAR(64) NOT NULL, kind VARCHAR(256) NOT NULL, status VARCHAR(256) NOT NULL DEFAULT 'Queued', ontologyversionid VARCHAR(64), requestedbyuserid VARCHAR(64), samplesize INT NOT NULL DEFAULT 10, total INT NOT NULL DEFAULT 0, processed INT NOT NULL DEFAULT 0, changed INT NOT NULL DEFAULT 0, added INT NOT NULL DEFAULT 0, removed INT NOT NULL DEFAULT 0, driftrate DOUBLE NOT NULL DEFAULT 0, error TEXT, claimtoken VARCHAR(64), createdutc VARCHAR(32) NOT NULL, startedutc VARCHAR(32), finishedutc VARCHAR(32), KEY idx_ontologyoperations_tenant_subject (tenantid, subjectid, createdutc), KEY idx_ontologyoperations_status (status));",
                "CREATE TABLE IF NOT EXISTS ontologyoperationitems (tenantid VARCHAR(64) NOT NULL, operationid VARCHAR(64) NOT NULL, ordinal INT NOT NULL DEFAULT 0, nodeid VARCHAR(64), excerpt TEXT, ischanged INT NOT NULL DEFAULT 0, detail TEXT, KEY idx_ontologyoperationitems_tenant_operation (tenantid, operationid));",
                "CREATE TABLE IF NOT EXISTS classificationcache (tenantid VARCHAR(64) NOT NULL, cachekey VARCHAR(64) NOT NULL, subjectid VARCHAR(64), blobkey VARCHAR(256) NOT NULL, hits INT NOT NULL DEFAULT 0, createdutc VARCHAR(32) NOT NULL, lastusedutc VARCHAR(32) NOT NULL, UNIQUE KEY idx_classificationcache_tenant_key (tenantid, cachekey), KEY idx_classificationcache_lastused (lastusedutc));",
                "ALTER TABLE subjects ADD COLUMN ontologyversionid VARCHAR(64);",
                "ALTER TABLE subjects ADD COLUMN classificationtemperature DOUBLE NOT NULL DEFAULT 0;",
                "ALTER TABLE subjects ADD COLUMN classificationcacheenabled INT NOT NULL DEFAULT 1;",
                "ALTER TABLE ingestionjobs ADD COLUMN classificationcachehits INT NOT NULL DEFAULT 0;",
                "ALTER TABLE ingestionjobs ADD COLUMN ontologyviolations INT NOT NULL DEFAULT 0;",
                "ALTER TABLE ingestionjobs ADD COLUMN taxonomymatches INT NOT NULL DEFAULT 0;"
            };
        }
    }
}
