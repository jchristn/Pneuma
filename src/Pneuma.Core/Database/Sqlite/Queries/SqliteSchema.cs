namespace Pneuma.Core.Database.Sqlite.Queries
{
    using System.Collections.Generic;
    using Pneuma.Core.Database;

    /// <summary>
    /// SQLite schema definition, expressed as ordered, idempotent migrations.
    /// </summary>
    internal static class SqliteSchema
    {
        /// <summary>
        /// All schema migrations in version order.
        /// </summary>
        internal static List<SchemaMigration> Migrations
        {
            get
            {
                List<SchemaMigration> list = new List<SchemaMigration>();
                list.Add(new SchemaMigration(1, "Initial Pneuma schema", InitialStatements()));
                list.Add(new SchemaMigration(2, "Add ingestion job embedding/completion endpoint ids", new List<string>
                {
                    "ALTER TABLE ingestionjobs ADD COLUMN embeddingendpointid TEXT;",
                    "ALTER TABLE ingestionjobs ADD COLUMN completionendpointid TEXT;"
                }));
                list.Add(new SchemaMigration(3, "Add ingestion job RecallDB collection id", new List<string>
                {
                    "ALTER TABLE ingestionjobs ADD COLUMN collectionid TEXT;"
                }));
                list.Add(new SchemaMigration(4, "Add tenant LiteGraph tenant/graph GUIDs", new List<string>
                {
                    "ALTER TABLE tenants ADD COLUMN litegraphtenantguid TEXT;",
                    "ALTER TABLE tenants ADD COLUMN litegraphgraphguid TEXT;"
                }));
                list.Add(new SchemaMigration(5, "Add ingestion job event queue duration", new List<string>
                {
                    "ALTER TABLE ingestionjobevents ADD COLUMN queuedurationms REAL;"
                }));
                list.Add(new SchemaMigration(6, "Add subject slug, prompts, thinking, retention, deletion status", new List<string>
                {
                    "ALTER TABLE subjects ADD COLUMN urlslug TEXT;",
                    "ALTER TABLE subjects ADD COLUMN thinkingenabled INTEGER NOT NULL DEFAULT 0;",
                    "ALTER TABLE subjects ADD COLUMN systemprompt TEXT;",
                    "ALTER TABLE subjects ADD COLUMN ontologyclassifyprompt TEXT;",
                    "ALTER TABLE subjects ADD COLUMN ontologydefinitionprompt TEXT;",
                    "ALTER TABLE subjects ADD COLUMN historyretentiondays INTEGER NOT NULL DEFAULT 90;",
                    "ALTER TABLE subjects ADD COLUMN deletionstatus TEXT NOT NULL DEFAULT 'None';",
                    "CREATE INDEX IF NOT EXISTS idx_subjects_slug ON subjects (tenantid, urlslug);"
                }));
                list.Add(new SchemaMigration(7, "Add chat history and feedback tables", new List<string>
                {
                    "CREATE TABLE IF NOT EXISTS chatturns (" +
                        "id TEXT PRIMARY KEY, tenantid TEXT NOT NULL, subjectid TEXT, userid TEXT, " +
                        "question TEXT, answer TEXT, thinking TEXT, model TEXT, " +
                        "prompttokens INTEGER NOT NULL DEFAULT 0, completiontokens INTEGER NOT NULL DEFAULT 0, totaltokens INTEGER NOT NULL DEFAULT 0, " +
                        "timetofirsttokenms REAL, generationms REAL, thinkingms REAL, " +
                        "contextsize INTEGER NOT NULL DEFAULT 0, citationsjson TEXT, createdutc TEXT NOT NULL);",
                    "CREATE INDEX IF NOT EXISTS idx_chatturns_tenant_subject ON chatturns (tenantid, subjectid, createdutc);",
                    "CREATE TABLE IF NOT EXISTS chatfeedback (" +
                        "id TEXT PRIMARY KEY, tenantid TEXT NOT NULL, turnid TEXT NOT NULL, subjectid TEXT, userid TEXT, " +
                        "rating TEXT, comment TEXT, createdutc TEXT NOT NULL);",
                    "CREATE INDEX IF NOT EXISTS idx_chatfeedback_tenant_subject ON chatfeedback (tenantid, subjectid, createdutc);",
                    "CREATE INDEX IF NOT EXISTS idx_chatfeedback_turn ON chatfeedback (turnid);"
                }));
                list.Add(new SchemaMigration(8, "Add subject id to ingestion job events", new List<string>
                {
                    "ALTER TABLE ingestionjobevents ADD COLUMN subjectid TEXT;",
                    "UPDATE ingestionjobevents SET subjectid = (SELECT j.subjectid FROM ingestionjobs j WHERE j.id = ingestionjobevents.jobid) WHERE subjectid IS NULL;",
                    "CREATE INDEX IF NOT EXISTS idx_jobevents_tenant_subject ON ingestionjobevents (tenantid, subjectid, createdutc);"
                }));
                list.Add(new SchemaMigration(9, "Add subject ask-page tagline", new List<string>
                {
                    "ALTER TABLE subjects ADD COLUMN tagline TEXT;",
                    "UPDATE subjects SET tagline = 'Get an answer grounded in the archive, with the sources that support it.' WHERE tagline IS NULL;"
                }));
                list.Add(new SchemaMigration(10, "Add subject models, collection, and rerank/rewrite prompts", new List<string>
                {
                    "ALTER TABLE subjects ADD COLUMN embeddingmodel TEXT;",
                    "ALTER TABLE subjects ADD COLUMN inferencemodel TEXT;",
                    "ALTER TABLE subjects ADD COLUMN rerankingmodel TEXT;",
                    "ALTER TABLE subjects ADD COLUMN promptrewritemodel TEXT;",
                    "ALTER TABLE subjects ADD COLUMN collection TEXT;",
                    "ALTER TABLE subjects ADD COLUMN rerankingprompt TEXT;",
                    "ALTER TABLE subjects ADD COLUMN promptrewriteprompt TEXT;",
                    "UPDATE subjects SET rerankingprompt = 'Rank the candidate passages by how well they help answer the question. Consider only relevance, not length or writing style.' WHERE rerankingprompt IS NULL;",
                    "UPDATE subjects SET promptrewriteprompt = 'Rewrite the question into a single, self-contained search query for this subject''s archive: resolve references, expand abbreviations, and keep it concise.' WHERE promptrewriteprompt IS NULL;"
                }));
                list.Add(new SchemaMigration(11, "Add chat-turn performance telemetry", new List<string>
                {
                    "ALTER TABLE chatturns ADD COLUMN performancejson TEXT;",
                    "ALTER TABLE chatturns ADD COLUMN performanceschemaversion INTEGER NOT NULL DEFAULT 0;",
                    "CREATE TABLE IF NOT EXISTS chatturnperfevents (" +
                        "id TEXT PRIMARY KEY, tenantid TEXT NOT NULL, turnid TEXT NOT NULL, subjectid TEXT, " +
                        "stage TEXT, kind TEXT, provider TEXT, model TEXT, " +
                        "durationms REAL, timetofirsttokenms REAL, " +
                        "prompttokens INTEGER NOT NULL DEFAULT 0, completiontokens INTEGER NOT NULL DEFAULT 0, " +
                        "success INTEGER NOT NULL DEFAULT 1, createdutc TEXT NOT NULL);",
                    "CREATE INDEX IF NOT EXISTS idx_perfevents_tenant_subject ON chatturnperfevents (tenantid, subjectid, createdutc);",
                    "CREATE INDEX IF NOT EXISTS idx_perfevents_turn ON chatturnperfevents (turnid);"
                }));
                return list;
            }
        }

        private static List<string> InitialStatements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS accounts (" +
                    "id TEXT PRIMARY KEY, name TEXT NOT NULL, active INTEGER NOT NULL DEFAULT 1, " +
                    "isprotected INTEGER NOT NULL DEFAULT 0, createdutc TEXT NOT NULL, lastupdateutc TEXT NOT NULL);",

                "CREATE TABLE IF NOT EXISTS tenants (" +
                    "id TEXT PRIMARY KEY, accountid TEXT, parentid TEXT, name TEXT NOT NULL, region TEXT, " +
                    "active INTEGER NOT NULL DEFAULT 1, isprotected INTEGER NOT NULL DEFAULT 0, " +
                    "createdutc TEXT NOT NULL, lastupdateutc TEXT NOT NULL);",

                "CREATE TABLE IF NOT EXISTS administrators (" +
                    "id TEXT PRIMARY KEY, accountid TEXT, firstname TEXT, lastname TEXT, email TEXT NOT NULL, " +
                    "passwordsha256 TEXT, telephone TEXT, active INTEGER NOT NULL DEFAULT 1, " +
                    "isprotected INTEGER NOT NULL DEFAULT 0, createdutc TEXT NOT NULL, lastupdateutc TEXT NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_administrators_email ON administrators (email);",

                "CREATE TABLE IF NOT EXISTS users (" +
                    "id TEXT PRIMARY KEY, tenantid TEXT NOT NULL, firstname TEXT, lastname TEXT, email TEXT NOT NULL, " +
                    "passwordsha256 TEXT, isadmin INTEGER NOT NULL DEFAULT 0, istenantadmin INTEGER NOT NULL DEFAULT 0, " +
                    "active INTEGER NOT NULL DEFAULT 1, isprotected INTEGER NOT NULL DEFAULT 0, " +
                    "createdutc TEXT NOT NULL, lastupdateutc TEXT NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_users_tenant_email ON users (tenantid, email);",

                "CREATE TABLE IF NOT EXISTS credentials (" +
                    "id TEXT PRIMARY KEY, tenantid TEXT NOT NULL, userid TEXT NOT NULL, name TEXT, accesskey TEXT NOT NULL, " +
                    "secretkeyencrypted TEXT, secretkeylast4 TEXT, authmode TEXT, lastusedutc TEXT, expiresutc TEXT, " +
                    "active INTEGER NOT NULL DEFAULT 1, isprotected INTEGER NOT NULL DEFAULT 0, " +
                    "createdutc TEXT NOT NULL, lastupdateutc TEXT NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_credentials_accesskey ON credentials (accesskey);",
                "CREATE INDEX IF NOT EXISTS idx_credentials_tenant_user ON credentials (tenantid, userid);",

                "CREATE TABLE IF NOT EXISTS authsessions (" +
                    "id TEXT PRIMARY KEY, tenantid TEXT, accountid TEXT, administratorid TEXT, userid TEXT, credentialid TEXT, " +
                    "principaltype TEXT, authscheme TEXT, tokenid TEXT, sourceip TEXT, useragent TEXT, expiresutc TEXT NOT NULL, " +
                    "lastusedutc TEXT, revokedutc TEXT, revocationreason TEXT, active INTEGER NOT NULL DEFAULT 1, " +
                    "isprotected INTEGER NOT NULL DEFAULT 0, createdutc TEXT NOT NULL, lastupdateutc TEXT NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_authsessions_tenant ON authsessions (tenantid);",

                "CREATE TABLE IF NOT EXISTS userroles (" +
                    "id TEXT PRIMARY KEY, tenantid TEXT, name TEXT NOT NULL, isbuiltin INTEGER NOT NULL DEFAULT 0, " +
                    "active INTEGER NOT NULL DEFAULT 1, isprotected INTEGER NOT NULL DEFAULT 0, " +
                    "createdutc TEXT NOT NULL, lastupdateutc TEXT NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_userroles_tenant_name ON userroles (tenantid, name);",

                "CREATE TABLE IF NOT EXISTS permissions (" +
                    "id TEXT PRIMARY KEY, tenantid TEXT, name TEXT, resourcetypes TEXT, operationtypes TEXT, " +
                    "permissiontype TEXT, active INTEGER NOT NULL DEFAULT 1, isprotected INTEGER NOT NULL DEFAULT 0, " +
                    "createdutc TEXT NOT NULL, lastupdateutc TEXT NOT NULL);",

                "CREATE TABLE IF NOT EXISTS rolepermissionmaps (" +
                    "id TEXT PRIMARY KEY, tenantid TEXT, roleid TEXT NOT NULL, permissionid TEXT NOT NULL, " +
                    "active INTEGER NOT NULL DEFAULT 1, isprotected INTEGER NOT NULL DEFAULT 0, " +
                    "createdutc TEXT NOT NULL, lastupdateutc TEXT NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_rpm_role ON rolepermissionmaps (roleid);",

                "CREATE TABLE IF NOT EXISTS userroleassignments (" +
                    "id TEXT PRIMARY KEY, tenantid TEXT NOT NULL, userid TEXT NOT NULL, roleid TEXT, rolename TEXT, " +
                    "resourcescope TEXT, resourceid TEXT, inheritstochildren INTEGER NOT NULL DEFAULT 1, " +
                    "active INTEGER NOT NULL DEFAULT 1, isprotected INTEGER NOT NULL DEFAULT 0, " +
                    "createdutc TEXT NOT NULL, lastupdateutc TEXT NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_ura_tenant_user ON userroleassignments (tenantid, userid);",

                "CREATE TABLE IF NOT EXISTS userrolemaps (" +
                    "id TEXT PRIMARY KEY, tenantid TEXT NOT NULL, userid TEXT NOT NULL, roleid TEXT NOT NULL, " +
                    "active INTEGER NOT NULL DEFAULT 1, isprotected INTEGER NOT NULL DEFAULT 0, " +
                    "createdutc TEXT NOT NULL, lastupdateutc TEXT NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_urm_tenant_user ON userrolemaps (tenantid, userid);",

                "CREATE TABLE IF NOT EXISTS credentialscopeassignments (" +
                    "id TEXT PRIMARY KEY, tenantid TEXT NOT NULL, credentialid TEXT NOT NULL, roleid TEXT, rolename TEXT, " +
                    "resourcescope TEXT, resourceid TEXT, permissions TEXT, resourcetypes TEXT, " +
                    "active INTEGER NOT NULL DEFAULT 1, isprotected INTEGER NOT NULL DEFAULT 0, " +
                    "createdutc TEXT NOT NULL, lastupdateutc TEXT NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_csa_tenant_cred ON credentialscopeassignments (tenantid, credentialid);",

                "CREATE TABLE IF NOT EXISTS audit (" +
                    "id TEXT PRIMARY KEY, eventtype TEXT, tenantid TEXT, userid TEXT, credentialid TEXT, sessionid TEXT, " +
                    "resourceid TEXT, principaltype TEXT, authscheme TEXT, requestid TEXT, httpmethod TEXT, urlpath TEXT, " +
                    "sourceip TEXT, authenticationresult TEXT, authorizationresult TEXT, requiredresourcetype TEXT, " +
                    "requiredoperation TEXT, denialreason TEXT, bypassreason TEXT, statuscode INTEGER, createdutc TEXT NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_audit_tenant_created ON audit (tenantid, createdutc);",

                "CREATE TABLE IF NOT EXISTS requesthistory (" +
                    "id TEXT PRIMARY KEY, tenantid TEXT, userid TEXT, principalname TEXT, method TEXT, path TEXT, url TEXT, " +
                    "statuscode INTEGER, durationms REAL, sourceip TEXT, requestheaders TEXT, requestbody TEXT, " +
                    "requestbodybytes INTEGER, requestbodytruncated INTEGER, responseheaders TEXT, responsebody TEXT, " +
                    "responsebodybytes INTEGER, responsebodytruncated INTEGER, createdutc TEXT NOT NULL, completedutc TEXT);",
                "CREATE INDEX IF NOT EXISTS idx_reqhist_tenant_created ON requesthistory (tenantid, createdutc);",
                "CREATE INDEX IF NOT EXISTS idx_reqhist_created ON requesthistory (createdutc);",

                "CREATE TABLE IF NOT EXISTS subjects (" +
                    "id TEXT PRIMARY KEY, tenantid TEXT NOT NULL, displayname TEXT NOT NULL, type TEXT, description TEXT, " +
                    "graphrootnodeid TEXT, active INTEGER NOT NULL DEFAULT 1, isprotected INTEGER NOT NULL DEFAULT 0, " +
                    "createdutc TEXT NOT NULL, lastupdateutc TEXT NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_subjects_tenant ON subjects (tenantid);",

                "CREATE TABLE IF NOT EXISTS subjectlinks (" +
                    "id TEXT PRIMARY KEY, tenantid TEXT NOT NULL, subjectid TEXT NOT NULL, url TEXT NOT NULL, title TEXT, " +
                    "submittedbyuserid TEXT, status TEXT, lastingestedutc TEXT, lasterror TEXT, " +
                    "active INTEGER NOT NULL DEFAULT 1, isprotected INTEGER NOT NULL DEFAULT 0, " +
                    "createdutc TEXT NOT NULL, lastupdateutc TEXT NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_subjectlinks_tenant_subject ON subjectlinks (tenantid, subjectid);",

                "CREATE TABLE IF NOT EXISTS ingestionjobs (" +
                    "id TEXT PRIMARY KEY, tenantid TEXT NOT NULL, subjectid TEXT NOT NULL, linkid TEXT NOT NULL, sourceurl TEXT, " +
                    "status TEXT, stage TEXT, attemptcount INTEGER NOT NULL DEFAULT 0, error TEXT, documenttype TEXT, blobkey TEXT, " +
                    "graphnodeids TEXT, verbexdocumentids TEXT, startedutc TEXT, completedutc TEXT, " +
                    "createdutc TEXT NOT NULL, lastupdateutc TEXT NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_jobs_tenant_status ON ingestionjobs (tenantid, status);",
                "CREATE INDEX IF NOT EXISTS idx_jobs_status_created ON ingestionjobs (status, createdutc);",

                "CREATE TABLE IF NOT EXISTS ingestionjobevents (" +
                    "id TEXT PRIMARY KEY, tenantid TEXT NOT NULL, jobid TEXT NOT NULL, stage TEXT, status TEXT, message TEXT, " +
                    "durationms REAL, createdutc TEXT NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_jobevents_job ON ingestionjobevents (jobid, createdutc);",

                "CREATE TABLE IF NOT EXISTS modelrunners (" +
                    "id TEXT PRIMARY KEY, tenantid TEXT, name TEXT NOT NULL, provider TEXT, baseurl TEXT, apitype TEXT, " +
                    "authmaterialencrypted TEXT, capabilities TEXT, runnerusage TEXT, defaultmodel TEXT, defaultembeddingmodel TEXT, " +
                    "active INTEGER NOT NULL DEFAULT 1, isprotected INTEGER NOT NULL DEFAULT 0, " +
                    "createdutc TEXT NOT NULL, lastupdateutc TEXT NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_modelrunners_tenant_name ON modelrunners (tenantid, name);",

                "CREATE TABLE IF NOT EXISTS prompts (" +
                    "id TEXT PRIMARY KEY, tenantid TEXT, promptkey TEXT NOT NULL, name TEXT, content TEXT, version INTEGER NOT NULL DEFAULT 1, " +
                    "active INTEGER NOT NULL DEFAULT 1, isprotected INTEGER NOT NULL DEFAULT 0, " +
                    "createdutc TEXT NOT NULL, lastupdateutc TEXT NOT NULL);",
                "CREATE INDEX IF NOT EXISTS idx_prompts_tenant_key ON prompts (tenantid, promptkey);"
            };
        }
    }
}
