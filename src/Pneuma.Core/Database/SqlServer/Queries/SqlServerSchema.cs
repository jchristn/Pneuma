namespace Pneuma.Core.Database.SqlServer.Queries
{
    using System.Collections.Generic;
    using Pneuma.Core.Database;

    /// <summary>
    /// SQL Server schema definition, expressed as ordered, idempotent migrations.
    /// </summary>
    internal static class SqlServerSchema
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
                    "ALTER TABLE dbo.ingestionjobs ADD embeddingendpointid NVARCHAR(MAX);",
                    "ALTER TABLE dbo.ingestionjobs ADD completionendpointid NVARCHAR(MAX);"
                }));
                list.Add(new SchemaMigration(3, "Add ingestion job RecallDB collection id", new List<string>
                {
                    "ALTER TABLE dbo.ingestionjobs ADD collectionid NVARCHAR(MAX);"
                }));
                list.Add(new SchemaMigration(4, "Add tenant LiteGraph tenant/graph GUIDs", new List<string>
                {
                    "ALTER TABLE dbo.tenants ADD litegraphtenantguid NVARCHAR(MAX);",
                    "ALTER TABLE dbo.tenants ADD litegraphgraphguid NVARCHAR(MAX);"
                }));
                list.Add(new SchemaMigration(5, "Add ingestion job event queue duration", new List<string>
                {
                    "ALTER TABLE dbo.ingestionjobevents ADD queuedurationms FLOAT;"
                }));
                list.Add(new SchemaMigration(6, "Add subject slug, prompts, thinking, retention, deletion status", new List<string>
                {
                    "ALTER TABLE dbo.subjects ADD urlslug NVARCHAR(512);",
                    "ALTER TABLE dbo.subjects ADD thinkingenabled BIT NOT NULL DEFAULT 0;",
                    "ALTER TABLE dbo.subjects ADD systemprompt NVARCHAR(MAX);",
                    "ALTER TABLE dbo.subjects ADD ontologyclassifyprompt NVARCHAR(MAX);",
                    "ALTER TABLE dbo.subjects ADD ontologydefinitionprompt NVARCHAR(MAX);",
                    "ALTER TABLE dbo.subjects ADD historyretentiondays INT NOT NULL DEFAULT 90;",
                    "ALTER TABLE dbo.subjects ADD deletionstatus NVARCHAR(32) NOT NULL DEFAULT 'None';",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_subjects_slug' AND object_id = OBJECT_ID(N'dbo.subjects')) CREATE INDEX idx_subjects_slug ON dbo.subjects (tenantid, urlslug);"
                }));
                list.Add(new SchemaMigration(7, "Add chat history and feedback tables", new List<string>
                {
                    "IF OBJECT_ID(N'dbo.chatturns', N'U') IS NULL CREATE TABLE dbo.chatturns (" +
                        "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, subjectid NVARCHAR(64), userid NVARCHAR(64), " +
                        "question NVARCHAR(MAX), answer NVARCHAR(MAX), thinking NVARCHAR(MAX), model NVARCHAR(512), " +
                        "prompttokens INT NOT NULL DEFAULT 0, completiontokens INT NOT NULL DEFAULT 0, totaltokens INT NOT NULL DEFAULT 0, " +
                        "timetofirsttokenms FLOAT, generationms FLOAT, thinkingms FLOAT, " +
                        "contextsize INT NOT NULL DEFAULT 0, citationsjson NVARCHAR(MAX), createdutc NVARCHAR(32) NOT NULL);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_chatturns_tenant_subject' AND object_id = OBJECT_ID(N'dbo.chatturns')) CREATE INDEX idx_chatturns_tenant_subject ON dbo.chatturns (tenantid, subjectid, createdutc);",
                    "IF OBJECT_ID(N'dbo.chatfeedback', N'U') IS NULL CREATE TABLE dbo.chatfeedback (" +
                        "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, turnid NVARCHAR(64) NOT NULL, subjectid NVARCHAR(64), userid NVARCHAR(64), " +
                        "rating NVARCHAR(16), comment NVARCHAR(MAX), createdutc NVARCHAR(32) NOT NULL);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_chatfeedback_tenant_subject' AND object_id = OBJECT_ID(N'dbo.chatfeedback')) CREATE INDEX idx_chatfeedback_tenant_subject ON dbo.chatfeedback (tenantid, subjectid, createdutc);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_chatfeedback_turn' AND object_id = OBJECT_ID(N'dbo.chatfeedback')) CREATE INDEX idx_chatfeedback_turn ON dbo.chatfeedback (turnid);"
                }));
                list.Add(new SchemaMigration(8, "Add subject id to ingestion job events", new List<string>
                {
                    "IF COL_LENGTH('dbo.ingestionjobevents', 'subjectid') IS NULL ALTER TABLE dbo.ingestionjobevents ADD subjectid NVARCHAR(64);",
                    "UPDATE e SET e.subjectid = j.subjectid FROM dbo.ingestionjobevents e INNER JOIN dbo.ingestionjobs j ON j.id = e.jobid WHERE e.subjectid IS NULL;",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_jobevents_tenant_subject' AND object_id = OBJECT_ID(N'dbo.ingestionjobevents')) CREATE INDEX idx_jobevents_tenant_subject ON dbo.ingestionjobevents (tenantid, subjectid, createdutc);"
                }));
                list.Add(new SchemaMigration(9, "Add subject ask-page tagline", new List<string>
                {
                    "IF COL_LENGTH('dbo.subjects', 'tagline') IS NULL ALTER TABLE dbo.subjects ADD tagline NVARCHAR(MAX);",
                    "UPDATE dbo.subjects SET tagline = 'Get an answer grounded in the archive, with the sources that support it.' WHERE tagline IS NULL;"
                }));
                list.Add(new SchemaMigration(10, "Add subject models, collection, and rerank/rewrite prompts", new List<string>
                {
                    "IF COL_LENGTH('dbo.subjects', 'embeddingmodel') IS NULL ALTER TABLE dbo.subjects ADD embeddingmodel NVARCHAR(64);",
                    "IF COL_LENGTH('dbo.subjects', 'inferencemodel') IS NULL ALTER TABLE dbo.subjects ADD inferencemodel NVARCHAR(64);",
                    "IF COL_LENGTH('dbo.subjects', 'rerankingmodel') IS NULL ALTER TABLE dbo.subjects ADD rerankingmodel NVARCHAR(64);",
                    "IF COL_LENGTH('dbo.subjects', 'promptrewritemodel') IS NULL ALTER TABLE dbo.subjects ADD promptrewritemodel NVARCHAR(64);",
                    "IF COL_LENGTH('dbo.subjects', 'collection') IS NULL ALTER TABLE dbo.subjects ADD collection NVARCHAR(64);",
                    "IF COL_LENGTH('dbo.subjects', 'rerankingprompt') IS NULL ALTER TABLE dbo.subjects ADD rerankingprompt NVARCHAR(MAX);",
                    "IF COL_LENGTH('dbo.subjects', 'promptrewriteprompt') IS NULL ALTER TABLE dbo.subjects ADD promptrewriteprompt NVARCHAR(MAX);",
                    "UPDATE dbo.subjects SET rerankingprompt = 'Rank the candidate passages by how well they help answer the question. Consider only relevance, not length or writing style.' WHERE rerankingprompt IS NULL;",
                    "UPDATE dbo.subjects SET promptrewriteprompt = 'Rewrite the question into a single, self-contained search query for this subject''s archive: resolve references, expand abbreviations, and keep it concise.' WHERE promptrewriteprompt IS NULL;"
                }));
                list.Add(new SchemaMigration(11, "Add chat-turn performance telemetry", new List<string>
                {
                    "IF COL_LENGTH('dbo.chatturns', 'performancejson') IS NULL ALTER TABLE dbo.chatturns ADD performancejson NVARCHAR(MAX);",
                    "IF COL_LENGTH('dbo.chatturns', 'performanceschemaversion') IS NULL ALTER TABLE dbo.chatturns ADD performanceschemaversion INT NOT NULL DEFAULT 0;",
                    "IF OBJECT_ID(N'dbo.chatturnperfevents', N'U') IS NULL CREATE TABLE dbo.chatturnperfevents (" +
                        "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, turnid NVARCHAR(64) NOT NULL, subjectid NVARCHAR(64), " +
                        "stage NVARCHAR(128), kind NVARCHAR(64), provider NVARCHAR(128), model NVARCHAR(512), " +
                        "durationms FLOAT, timetofirsttokenms FLOAT, " +
                        "prompttokens INT NOT NULL DEFAULT 0, completiontokens INT NOT NULL DEFAULT 0, " +
                        "success BIT NOT NULL DEFAULT 1, createdutc NVARCHAR(32) NOT NULL);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_perfevents_tenant_subject' AND object_id = OBJECT_ID(N'dbo.chatturnperfevents')) CREATE INDEX idx_perfevents_tenant_subject ON dbo.chatturnperfevents (tenantid, subjectid, createdutc);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_perfevents_turn' AND object_id = OBJECT_ID(N'dbo.chatturnperfevents')) CREATE INDEX idx_perfevents_turn ON dbo.chatturnperfevents (turnid);"
                }));
                list.Add(new SchemaMigration(12, "Add subject and chat-turn retrieval facet filters", new List<string>
                {
                    "IF COL_LENGTH('dbo.subjects', 'retrievalfilterjson') IS NULL ALTER TABLE dbo.subjects ADD retrievalfilterjson NVARCHAR(MAX);",
                    "IF COL_LENGTH('dbo.chatturns', 'retrievalfilterjson') IS NULL ALTER TABLE dbo.chatturns ADD retrievalfilterjson NVARCHAR(MAX);"
                }));
                list.Add(new SchemaMigration(13, "Add conversation threads and tool-call trace", new List<string>
                {
                    "IF COL_LENGTH('dbo.chatturns', 'threadid') IS NULL ALTER TABLE dbo.chatturns ADD threadid NVARCHAR(64);",
                    "IF OBJECT_ID(N'dbo.chatthreads', N'U') IS NULL CREATE TABLE dbo.chatthreads (" +
                        "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, subjectid NVARCHAR(64), userid NVARCHAR(64), " +
                        "title NVARCHAR(512), createdutc NVARCHAR(32) NOT NULL, lastactivityutc NVARCHAR(32) NOT NULL);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_chatthreads_tenant_subject' AND object_id = OBJECT_ID(N'dbo.chatthreads')) CREATE INDEX idx_chatthreads_tenant_subject ON dbo.chatthreads (tenantid, subjectid, lastactivityutc);",
                    "IF OBJECT_ID(N'dbo.chattoolcalls', N'U') IS NULL CREATE TABLE dbo.chattoolcalls (" +
                        "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, turnid NVARCHAR(64) NOT NULL, subjectid NVARCHAR(64), " +
                        "toolname NVARCHAR(128), argumentsjson NVARCHAR(MAX), outputjson NVARCHAR(MAX), success BIT NOT NULL DEFAULT 1, " +
                        "durationms FLOAT, sequence INT NOT NULL DEFAULT 0, createdutc NVARCHAR(32) NOT NULL);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_chattoolcalls_turn' AND object_id = OBJECT_ID(N'dbo.chattoolcalls')) CREATE INDEX idx_chattoolcalls_turn ON dbo.chattoolcalls (turnid);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_chattoolcalls_tenant_subject' AND object_id = OBJECT_ID(N'dbo.chattoolcalls')) CREATE INDEX idx_chattoolcalls_tenant_subject ON dbo.chattoolcalls (tenantid, subjectid, createdutc);"
                }));
                return list;
            }
        }

        private static List<string> InitialStatements()
        {
            return new List<string>
            {
                "IF OBJECT_ID(N'dbo.accounts', N'U') IS NULL CREATE TABLE dbo.accounts (" +
                    "id NVARCHAR(64) PRIMARY KEY, name NVARCHAR(512) NOT NULL, active BIT NOT NULL DEFAULT 1, " +
                    "isprotected BIT NOT NULL DEFAULT 0, createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",

                "IF OBJECT_ID(N'dbo.tenants', N'U') IS NULL CREATE TABLE dbo.tenants (" +
                    "id NVARCHAR(64) PRIMARY KEY, accountid NVARCHAR(64), parentid NVARCHAR(64), name NVARCHAR(512) NOT NULL, region NVARCHAR(64), " +
                    "active BIT NOT NULL DEFAULT 1, isprotected BIT NOT NULL DEFAULT 0, " +
                    "createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",

                "IF OBJECT_ID(N'dbo.administrators', N'U') IS NULL CREATE TABLE dbo.administrators (" +
                    "id NVARCHAR(64) PRIMARY KEY, accountid NVARCHAR(64), firstname NVARCHAR(512), lastname NVARCHAR(512), email NVARCHAR(320) NOT NULL, " +
                    "passwordsha256 NVARCHAR(64), telephone NVARCHAR(64), active BIT NOT NULL DEFAULT 1, " +
                    "isprotected BIT NOT NULL DEFAULT 0, createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_administrators_email' AND object_id = OBJECT_ID(N'dbo.administrators')) CREATE UNIQUE INDEX idx_administrators_email ON dbo.administrators (email);",

                "IF OBJECT_ID(N'dbo.users', N'U') IS NULL CREATE TABLE dbo.users (" +
                    "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, firstname NVARCHAR(512), lastname NVARCHAR(512), email NVARCHAR(320) NOT NULL, " +
                    "passwordsha256 NVARCHAR(64), isadmin BIT NOT NULL DEFAULT 0, istenantadmin BIT NOT NULL DEFAULT 0, " +
                    "active BIT NOT NULL DEFAULT 1, isprotected BIT NOT NULL DEFAULT 0, " +
                    "createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_users_tenant_email' AND object_id = OBJECT_ID(N'dbo.users')) CREATE UNIQUE INDEX idx_users_tenant_email ON dbo.users (tenantid, email);",

                "IF OBJECT_ID(N'dbo.credentials', N'U') IS NULL CREATE TABLE dbo.credentials (" +
                    "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, userid NVARCHAR(64) NOT NULL, name NVARCHAR(512), accesskey NVARCHAR(512) NOT NULL, " +
                    "secretkeyencrypted NVARCHAR(MAX), secretkeylast4 NVARCHAR(64), authmode NVARCHAR(64), lastusedutc NVARCHAR(32), expiresutc NVARCHAR(32), " +
                    "active BIT NOT NULL DEFAULT 1, isprotected BIT NOT NULL DEFAULT 0, " +
                    "createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_credentials_accesskey' AND object_id = OBJECT_ID(N'dbo.credentials')) CREATE UNIQUE INDEX idx_credentials_accesskey ON dbo.credentials (accesskey);",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_credentials_tenant_user' AND object_id = OBJECT_ID(N'dbo.credentials')) CREATE INDEX idx_credentials_tenant_user ON dbo.credentials (tenantid, userid);",

                "IF OBJECT_ID(N'dbo.authsessions', N'U') IS NULL CREATE TABLE dbo.authsessions (" +
                    "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64), accountid NVARCHAR(64), administratorid NVARCHAR(64), userid NVARCHAR(64), credentialid NVARCHAR(64), " +
                    "principaltype NVARCHAR(64), authscheme NVARCHAR(64), tokenid NVARCHAR(64), sourceip NVARCHAR(64), useragent NVARCHAR(512), expiresutc NVARCHAR(32) NOT NULL, " +
                    "lastusedutc NVARCHAR(32), revokedutc NVARCHAR(32), revocationreason NVARCHAR(MAX), active BIT NOT NULL DEFAULT 1, " +
                    "isprotected BIT NOT NULL DEFAULT 0, createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_authsessions_tenant' AND object_id = OBJECT_ID(N'dbo.authsessions')) CREATE INDEX idx_authsessions_tenant ON dbo.authsessions (tenantid);",

                "IF OBJECT_ID(N'dbo.userroles', N'U') IS NULL CREATE TABLE dbo.userroles (" +
                    "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64), name NVARCHAR(512) NOT NULL, isbuiltin BIT NOT NULL DEFAULT 0, " +
                    "active BIT NOT NULL DEFAULT 1, isprotected BIT NOT NULL DEFAULT 0, " +
                    "createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_userroles_tenant_name' AND object_id = OBJECT_ID(N'dbo.userroles')) CREATE INDEX idx_userroles_tenant_name ON dbo.userroles (tenantid, name);",

                "IF OBJECT_ID(N'dbo.permissions', N'U') IS NULL CREATE TABLE dbo.permissions (" +
                    "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64), name NVARCHAR(512), resourcetypes NVARCHAR(MAX), operationtypes NVARCHAR(MAX), " +
                    "permissiontype NVARCHAR(64), active BIT NOT NULL DEFAULT 1, isprotected BIT NOT NULL DEFAULT 0, " +
                    "createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",

                "IF OBJECT_ID(N'dbo.rolepermissionmaps', N'U') IS NULL CREATE TABLE dbo.rolepermissionmaps (" +
                    "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64), roleid NVARCHAR(64) NOT NULL, permissionid NVARCHAR(64) NOT NULL, " +
                    "active BIT NOT NULL DEFAULT 1, isprotected BIT NOT NULL DEFAULT 0, " +
                    "createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_rpm_role' AND object_id = OBJECT_ID(N'dbo.rolepermissionmaps')) CREATE INDEX idx_rpm_role ON dbo.rolepermissionmaps (roleid);",

                "IF OBJECT_ID(N'dbo.userroleassignments', N'U') IS NULL CREATE TABLE dbo.userroleassignments (" +
                    "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, userid NVARCHAR(64) NOT NULL, roleid NVARCHAR(64), rolename NVARCHAR(512), " +
                    "resourcescope NVARCHAR(64), resourceid NVARCHAR(64), inheritstochildren BIT NOT NULL DEFAULT 1, " +
                    "active BIT NOT NULL DEFAULT 1, isprotected BIT NOT NULL DEFAULT 0, " +
                    "createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_ura_tenant_user' AND object_id = OBJECT_ID(N'dbo.userroleassignments')) CREATE INDEX idx_ura_tenant_user ON dbo.userroleassignments (tenantid, userid);",

                "IF OBJECT_ID(N'dbo.userrolemaps', N'U') IS NULL CREATE TABLE dbo.userrolemaps (" +
                    "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, userid NVARCHAR(64) NOT NULL, roleid NVARCHAR(64) NOT NULL, " +
                    "active BIT NOT NULL DEFAULT 1, isprotected BIT NOT NULL DEFAULT 0, " +
                    "createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_urm_tenant_user' AND object_id = OBJECT_ID(N'dbo.userrolemaps')) CREATE INDEX idx_urm_tenant_user ON dbo.userrolemaps (tenantid, userid);",

                "IF OBJECT_ID(N'dbo.credentialscopeassignments', N'U') IS NULL CREATE TABLE dbo.credentialscopeassignments (" +
                    "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, credentialid NVARCHAR(64) NOT NULL, roleid NVARCHAR(64), rolename NVARCHAR(512), " +
                    "resourcescope NVARCHAR(64), resourceid NVARCHAR(64), permissions NVARCHAR(MAX), resourcetypes NVARCHAR(MAX), " +
                    "active BIT NOT NULL DEFAULT 1, isprotected BIT NOT NULL DEFAULT 0, " +
                    "createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_csa_tenant_cred' AND object_id = OBJECT_ID(N'dbo.credentialscopeassignments')) CREATE INDEX idx_csa_tenant_cred ON dbo.credentialscopeassignments (tenantid, credentialid);",

                "IF OBJECT_ID(N'dbo.audit', N'U') IS NULL CREATE TABLE dbo.audit (" +
                    "id NVARCHAR(64) PRIMARY KEY, eventtype NVARCHAR(64), tenantid NVARCHAR(64), userid NVARCHAR(64), credentialid NVARCHAR(64), sessionid NVARCHAR(64), " +
                    "resourceid NVARCHAR(64), principaltype NVARCHAR(64), authscheme NVARCHAR(64), requestid NVARCHAR(64), httpmethod NVARCHAR(64), urlpath NVARCHAR(512), " +
                    "sourceip NVARCHAR(64), authenticationresult NVARCHAR(64), authorizationresult NVARCHAR(64), requiredresourcetype NVARCHAR(64), " +
                    "requiredoperation NVARCHAR(64), denialreason NVARCHAR(MAX), bypassreason NVARCHAR(MAX), statuscode INT, createdutc NVARCHAR(32) NOT NULL);",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_audit_tenant_created' AND object_id = OBJECT_ID(N'dbo.audit')) CREATE INDEX idx_audit_tenant_created ON dbo.audit (tenantid, createdutc);",

                "IF OBJECT_ID(N'dbo.requesthistory', N'U') IS NULL CREATE TABLE dbo.requesthistory (" +
                    "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64), userid NVARCHAR(64), principalname NVARCHAR(512), method NVARCHAR(64), path NVARCHAR(512), url NVARCHAR(512), " +
                    "statuscode INT, durationms FLOAT, sourceip NVARCHAR(64), requestheaders NVARCHAR(MAX), requestbody NVARCHAR(MAX), " +
                    "requestbodybytes BIGINT, requestbodytruncated BIT NOT NULL DEFAULT 0, responseheaders NVARCHAR(MAX), responsebody NVARCHAR(MAX), " +
                    "responsebodybytes BIGINT, responsebodytruncated BIT NOT NULL DEFAULT 0, createdutc NVARCHAR(32) NOT NULL, completedutc NVARCHAR(32));",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_reqhist_tenant_created' AND object_id = OBJECT_ID(N'dbo.requesthistory')) CREATE INDEX idx_reqhist_tenant_created ON dbo.requesthistory (tenantid, createdutc);",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_reqhist_created' AND object_id = OBJECT_ID(N'dbo.requesthistory')) CREATE INDEX idx_reqhist_created ON dbo.requesthistory (createdutc);",

                "IF OBJECT_ID(N'dbo.subjects', N'U') IS NULL CREATE TABLE dbo.subjects (" +
                    "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, displayname NVARCHAR(512) NOT NULL, type NVARCHAR(64), description NVARCHAR(MAX), " +
                    "graphrootnodeid NVARCHAR(64), active BIT NOT NULL DEFAULT 1, isprotected BIT NOT NULL DEFAULT 0, " +
                    "createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_subjects_tenant' AND object_id = OBJECT_ID(N'dbo.subjects')) CREATE INDEX idx_subjects_tenant ON dbo.subjects (tenantid);",

                "IF OBJECT_ID(N'dbo.subjectlinks', N'U') IS NULL CREATE TABLE dbo.subjectlinks (" +
                    "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, subjectid NVARCHAR(64) NOT NULL, url NVARCHAR(512) NOT NULL, title NVARCHAR(512), " +
                    "submittedbyuserid NVARCHAR(64), status NVARCHAR(64), lastingestedutc NVARCHAR(32), lasterror NVARCHAR(MAX), " +
                    "active BIT NOT NULL DEFAULT 1, isprotected BIT NOT NULL DEFAULT 0, " +
                    "createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_subjectlinks_tenant_subject' AND object_id = OBJECT_ID(N'dbo.subjectlinks')) CREATE INDEX idx_subjectlinks_tenant_subject ON dbo.subjectlinks (tenantid, subjectid);",

                "IF OBJECT_ID(N'dbo.ingestionjobs', N'U') IS NULL CREATE TABLE dbo.ingestionjobs (" +
                    "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, subjectid NVARCHAR(64) NOT NULL, linkid NVARCHAR(64) NOT NULL, sourceurl NVARCHAR(512), " +
                    "status NVARCHAR(64), stage NVARCHAR(64), attemptcount INT NOT NULL DEFAULT 0, error NVARCHAR(MAX), documenttype NVARCHAR(64), blobkey NVARCHAR(512), " +
                    "graphnodeids NVARCHAR(MAX), verbexdocumentids NVARCHAR(MAX), startedutc NVARCHAR(32), completedutc NVARCHAR(32), " +
                    "createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_jobs_tenant_status' AND object_id = OBJECT_ID(N'dbo.ingestionjobs')) CREATE INDEX idx_jobs_tenant_status ON dbo.ingestionjobs (tenantid, status);",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_jobs_status_created' AND object_id = OBJECT_ID(N'dbo.ingestionjobs')) CREATE INDEX idx_jobs_status_created ON dbo.ingestionjobs (status, createdutc);",

                "IF OBJECT_ID(N'dbo.ingestionjobevents', N'U') IS NULL CREATE TABLE dbo.ingestionjobevents (" +
                    "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, jobid NVARCHAR(64) NOT NULL, stage NVARCHAR(64), status NVARCHAR(64), message NVARCHAR(MAX), " +
                    "durationms FLOAT, createdutc NVARCHAR(32) NOT NULL);",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_jobevents_job' AND object_id = OBJECT_ID(N'dbo.ingestionjobevents')) CREATE INDEX idx_jobevents_job ON dbo.ingestionjobevents (jobid, createdutc);",

                "IF OBJECT_ID(N'dbo.modelrunners', N'U') IS NULL CREATE TABLE dbo.modelrunners (" +
                    "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64), name NVARCHAR(512) NOT NULL, provider NVARCHAR(64), baseurl NVARCHAR(512), apitype NVARCHAR(64), " +
                    "authmaterialencrypted NVARCHAR(MAX), capabilities NVARCHAR(MAX), runnerusage NVARCHAR(64), defaultmodel NVARCHAR(512), defaultembeddingmodel NVARCHAR(512), " +
                    "active BIT NOT NULL DEFAULT 1, isprotected BIT NOT NULL DEFAULT 0, " +
                    "createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_modelrunners_tenant_name' AND object_id = OBJECT_ID(N'dbo.modelrunners')) CREATE INDEX idx_modelrunners_tenant_name ON dbo.modelrunners (tenantid, name);",

                "IF OBJECT_ID(N'dbo.prompts', N'U') IS NULL CREATE TABLE dbo.prompts (" +
                    "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64), promptkey NVARCHAR(512) NOT NULL, name NVARCHAR(512), content NVARCHAR(MAX), version INT NOT NULL DEFAULT 1, " +
                    "active BIT NOT NULL DEFAULT 1, isprotected BIT NOT NULL DEFAULT 0, " +
                    "createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_prompts_tenant_key' AND object_id = OBJECT_ID(N'dbo.prompts')) CREATE INDEX idx_prompts_tenant_key ON dbo.prompts (tenantid, promptkey);"
            };
        }
    }
}
