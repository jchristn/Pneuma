namespace Pneuma.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL statements for the ontology governance migration: tenant ontologies and their versions (node types, edge
    /// types, rules, taxonomy concepts and labels), rule violations, background operations and their items, the
    /// classification cache index, the subject's pinned version and classification settings, and the job counters.
    /// </summary>
    internal static class PostgresqlOntologySchema
    {
        /// <summary>Statements for migration 35.</summary>
        /// <returns>The statements, in order.</returns>
        internal static List<string> Migration35()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS ontologies (id TEXT NOT NULL PRIMARY KEY, tenantid TEXT NOT NULL, name TEXT NOT NULL, description TEXT, createdutc TEXT NOT NULL, lastupdateutc TEXT NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_ontologies_tenant_name ON ontologies (tenantid, name);",
                "CREATE TABLE IF NOT EXISTS ontologyversions (id TEXT NOT NULL PRIMARY KEY, tenantid TEXT NOT NULL, ontologyid TEXT NOT NULL, versionnumber INTEGER NOT NULL DEFAULT 1, status TEXT NOT NULL DEFAULT 'Draft', guidance TEXT, undeclaredtypeaction TEXT NOT NULL DEFAULT 'Allow', changesummary TEXT, basedonversionid TEXT, createdbyuserid TEXT, approvedbyuserid TEXT, approvedutc TEXT, retiredutc TEXT, createdutc TEXT NOT NULL, lastupdateutc TEXT NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_ontologyversions_tenant_ontology_number ON ontologyversions (tenantid, ontologyid, versionnumber);",
                "CREATE TABLE IF NOT EXISTS ontologynodetypes (tenantid TEXT NOT NULL, versionid TEXT NOT NULL, ordinal INTEGER NOT NULL DEFAULT 0, name TEXT NOT NULL, description TEXT);",
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_ontologynodetypes_version_name ON ontologynodetypes (tenantid, versionid, name);",
                "CREATE TABLE IF NOT EXISTS ontologyedgetypes (tenantid TEXT NOT NULL, versionid TEXT NOT NULL, ordinal INTEGER NOT NULL DEFAULT 0, name TEXT NOT NULL, description TEXT);",
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_ontologyedgetypes_version_name ON ontologyedgetypes (tenantid, versionid, name);",
                "CREATE TABLE IF NOT EXISTS ontologyrules (id TEXT NOT NULL PRIMARY KEY, tenantid TEXT NOT NULL, versionid TEXT NOT NULL, ordinal INTEGER NOT NULL DEFAULT 0, ruletype TEXT NOT NULL, nodetype TEXT, edgetype TEXT, fromnodetype TEXT, tonodetype TEXT, fieldname TEXT, rulepattern TEXT, maxcount INTEGER NOT NULL DEFAULT 1, minconfidence DOUBLE PRECISION NOT NULL DEFAULT 0.5, ruleaction TEXT NOT NULL DEFAULT 'Warn', description TEXT);",
                "CREATE INDEX IF NOT EXISTS idx_ontologyrules_tenant_version ON ontologyrules (tenantid, versionid);",
                "CREATE TABLE IF NOT EXISTS ontologyconcepts (tenantid TEXT NOT NULL, versionid TEXT NOT NULL, ordinal INTEGER NOT NULL DEFAULT 0, conceptkey TEXT NOT NULL, preflabel TEXT NOT NULL, broaderkey TEXT, definition TEXT, nodetype TEXT NOT NULL DEFAULT 'Topic', casesensitive INTEGER NOT NULL DEFAULT 0);",
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_ontologyconcepts_version_key ON ontologyconcepts (tenantid, versionid, conceptkey);",
                "CREATE TABLE IF NOT EXISTS ontologyconceptlabels (tenantid TEXT NOT NULL, versionid TEXT NOT NULL, conceptkey TEXT NOT NULL, ordinal INTEGER NOT NULL DEFAULT 0, label TEXT NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_ontologyconceptlabels_tenant_version ON ontologyconceptlabels (tenantid, versionid);",
                "CREATE TABLE IF NOT EXISTS ontologyviolations (id TEXT NOT NULL PRIMARY KEY, tenantid TEXT NOT NULL, subjectid TEXT NOT NULL, jobid TEXT, linkid TEXT, operationid TEXT, ontologyversionid TEXT, ruleid TEXT, ruletype TEXT, elementkind TEXT NOT NULL DEFAULT 'Node', nodetype TEXT, nodename TEXT, edgetype TEXT, fromnodetype TEXT, fromnodename TEXT, tonodetype TEXT, tonodename TEXT, content TEXT, confidence DOUBLE PRECISION NOT NULL DEFAULT 0.5, ruleaction TEXT NOT NULL DEFAULT 'Warn', status TEXT NOT NULL DEFAULT 'Recorded', message TEXT, resolvedbyuserid TEXT, resolvedutc TEXT, createdutc TEXT NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_ontologyviolations_tenant_subject ON ontologyviolations (tenantid, subjectid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_ontologyviolations_job ON ontologyviolations (jobid);",
                "CREATE INDEX IF NOT EXISTS idx_ontologyviolations_operation ON ontologyviolations (operationid);",
                "CREATE TABLE IF NOT EXISTS ontologyoperations (id TEXT NOT NULL PRIMARY KEY, tenantid TEXT NOT NULL, subjectid TEXT NOT NULL, kind TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'Queued', ontologyversionid TEXT, requestedbyuserid TEXT, samplesize INTEGER NOT NULL DEFAULT 10, total INTEGER NOT NULL DEFAULT 0, processed INTEGER NOT NULL DEFAULT 0, changed INTEGER NOT NULL DEFAULT 0, added INTEGER NOT NULL DEFAULT 0, removed INTEGER NOT NULL DEFAULT 0, driftrate DOUBLE PRECISION NOT NULL DEFAULT 0, error TEXT, claimtoken TEXT, createdutc TEXT NOT NULL, startedutc TEXT, finishedutc TEXT);",
                "CREATE INDEX IF NOT EXISTS idx_ontologyoperations_tenant_subject ON ontologyoperations (tenantid, subjectid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_ontologyoperations_status ON ontologyoperations (status);",
                "CREATE TABLE IF NOT EXISTS ontologyoperationitems (tenantid TEXT NOT NULL, operationid TEXT NOT NULL, ordinal INTEGER NOT NULL DEFAULT 0, nodeid TEXT, excerpt TEXT, ischanged INTEGER NOT NULL DEFAULT 0, detail TEXT);",
                "CREATE INDEX IF NOT EXISTS idx_ontologyoperationitems_tenant_operation ON ontologyoperationitems (tenantid, operationid);",
                "CREATE TABLE IF NOT EXISTS classificationcache (tenantid TEXT NOT NULL, cachekey TEXT NOT NULL, subjectid TEXT, blobkey TEXT NOT NULL, hits INTEGER NOT NULL DEFAULT 0, createdutc TEXT NOT NULL, lastusedutc TEXT NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_classificationcache_tenant_key ON classificationcache (tenantid, cachekey);",
                "CREATE INDEX IF NOT EXISTS idx_classificationcache_lastused ON classificationcache (lastusedutc);",
                "ALTER TABLE subjects ADD COLUMN IF NOT EXISTS ontologyversionid TEXT;",
                "ALTER TABLE subjects ADD COLUMN IF NOT EXISTS classificationtemperature DOUBLE PRECISION NOT NULL DEFAULT 0;",
                "ALTER TABLE subjects ADD COLUMN IF NOT EXISTS classificationcacheenabled INTEGER NOT NULL DEFAULT 1;",
                "ALTER TABLE ingestionjobs ADD COLUMN IF NOT EXISTS classificationcachehits INTEGER NOT NULL DEFAULT 0;",
                "ALTER TABLE ingestionjobs ADD COLUMN IF NOT EXISTS ontologyviolations INTEGER NOT NULL DEFAULT 0;",
                "ALTER TABLE ingestionjobs ADD COLUMN IF NOT EXISTS taxonomymatches INTEGER NOT NULL DEFAULT 0;"
            };
        }
    }
}
