namespace Pneuma.Core.Database.Mysql.Queries
{
    using System.Collections.Generic;
    using Pneuma.Core.Database;

    /// <summary>
    /// MySQL schema definition, expressed as ordered, idempotent migrations.
    /// </summary>
    internal static class MysqlSchema
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
                    "ALTER TABLE ingestionjobevents ADD COLUMN queuedurationms DOUBLE;"
                }));
                list.Add(new SchemaMigration(6, "Add subject slug, prompts, thinking, retention, deletion status", new List<string>
                {
                    "ALTER TABLE subjects ADD COLUMN urlslug VARCHAR(255);",
                    "ALTER TABLE subjects ADD COLUMN thinkingenabled TINYINT NOT NULL DEFAULT 0;",
                    "ALTER TABLE subjects ADD COLUMN systemprompt TEXT;",
                    "ALTER TABLE subjects ADD COLUMN ontologyclassifyprompt TEXT;",
                    "ALTER TABLE subjects ADD COLUMN ontologydefinitionprompt TEXT;",
                    "ALTER TABLE subjects ADD COLUMN historyretentiondays INT NOT NULL DEFAULT 90;",
                    "ALTER TABLE subjects ADD COLUMN deletionstatus VARCHAR(32) NOT NULL DEFAULT 'None';",
                    "CREATE INDEX idx_subjects_slug ON subjects (tenantid, urlslug);"
                }));
                list.Add(new SchemaMigration(7, "Add chat history and feedback tables", new List<string>
                {
                    "CREATE TABLE IF NOT EXISTS chatturns (" +
                        "id VARCHAR(64) PRIMARY KEY, tenantid VARCHAR(64) NOT NULL, subjectid VARCHAR(64), userid VARCHAR(64), " +
                        "question TEXT, answer TEXT, thinking TEXT, model VARCHAR(512), " +
                        "prompttokens INT NOT NULL DEFAULT 0, completiontokens INT NOT NULL DEFAULT 0, totaltokens INT NOT NULL DEFAULT 0, " +
                        "timetofirsttokenms DOUBLE, generationms DOUBLE, thinkingms DOUBLE, " +
                        "contextsize INT NOT NULL DEFAULT 0, citationsjson TEXT, createdutc VARCHAR(32) NOT NULL, " +
                        "KEY idx_chatturns_tenant_subject (tenantid, subjectid, createdutc)" +
                        ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",
                    "CREATE TABLE IF NOT EXISTS chatfeedback (" +
                        "id VARCHAR(64) PRIMARY KEY, tenantid VARCHAR(64) NOT NULL, turnid VARCHAR(64) NOT NULL, subjectid VARCHAR(64), userid VARCHAR(64), " +
                        "rating VARCHAR(16), comment TEXT, createdutc VARCHAR(32) NOT NULL, " +
                        "KEY idx_chatfeedback_tenant_subject (tenantid, subjectid, createdutc), KEY idx_chatfeedback_turn (turnid)" +
                        ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;"
                }));
                list.Add(new SchemaMigration(8, "Add subject id to ingestion job events", new List<string>
                {
                    "ALTER TABLE ingestionjobevents ADD COLUMN subjectid VARCHAR(64);",
                    "UPDATE ingestionjobevents e JOIN ingestionjobs j ON j.id = e.jobid SET e.subjectid = j.subjectid;",
                    "CREATE INDEX idx_jobevents_tenant_subject ON ingestionjobevents (tenantid, subjectid, createdutc);"
                }));
                list.Add(new SchemaMigration(9, "Add subject ask-page tagline", new List<string>
                {
                    "ALTER TABLE subjects ADD COLUMN tagline TEXT;",
                    "UPDATE subjects SET tagline = 'Get an answer grounded in the archive, with the sources that support it.' WHERE tagline IS NULL;"
                }));
                list.Add(new SchemaMigration(10, "Add subject models, collection, and rerank/rewrite prompts", new List<string>
                {
                    "ALTER TABLE subjects ADD COLUMN embeddingmodel VARCHAR(64);",
                    "ALTER TABLE subjects ADD COLUMN inferencemodel VARCHAR(64);",
                    "ALTER TABLE subjects ADD COLUMN rerankingmodel VARCHAR(64);",
                    "ALTER TABLE subjects ADD COLUMN promptrewritemodel VARCHAR(64);",
                    "ALTER TABLE subjects ADD COLUMN collection VARCHAR(64);",
                    "ALTER TABLE subjects ADD COLUMN rerankingprompt TEXT;",
                    "ALTER TABLE subjects ADD COLUMN promptrewriteprompt TEXT;",
                    "UPDATE subjects SET rerankingprompt = 'Rank the candidate passages by how well they help answer the question. Consider only relevance, not length or writing style.' WHERE rerankingprompt IS NULL;",
                    "UPDATE subjects SET promptrewriteprompt = 'Rewrite the question into a single, self-contained search query for this subject''s archive: resolve references, expand abbreviations, and keep it concise.' WHERE promptrewriteprompt IS NULL;"
                }));
                return list;
            }
        }

        private static List<string> InitialStatements()
        {
            return new List<string>
            {
                "CREATE TABLE IF NOT EXISTS accounts (" +
                    "id VARCHAR(64) PRIMARY KEY, name VARCHAR(512) NOT NULL, active TINYINT NOT NULL DEFAULT 1, " +
                    "isprotected TINYINT NOT NULL DEFAULT 0, createdutc VARCHAR(32) NOT NULL, lastupdateutc VARCHAR(32) NOT NULL" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                "CREATE TABLE IF NOT EXISTS tenants (" +
                    "id VARCHAR(64) PRIMARY KEY, accountid VARCHAR(64), parentid VARCHAR(64), name VARCHAR(512) NOT NULL, region VARCHAR(64), " +
                    "active TINYINT NOT NULL DEFAULT 1, isprotected TINYINT NOT NULL DEFAULT 0, " +
                    "createdutc VARCHAR(32) NOT NULL, lastupdateutc VARCHAR(32) NOT NULL" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                "CREATE TABLE IF NOT EXISTS administrators (" +
                    "id VARCHAR(64) PRIMARY KEY, accountid VARCHAR(64), firstname VARCHAR(512), lastname VARCHAR(512), email VARCHAR(320) NOT NULL, " +
                    "passwordsha256 VARCHAR(64), telephone VARCHAR(64), active TINYINT NOT NULL DEFAULT 1, " +
                    "isprotected TINYINT NOT NULL DEFAULT 0, createdutc VARCHAR(32) NOT NULL, lastupdateutc VARCHAR(32) NOT NULL, " +
                    "UNIQUE KEY idx_administrators_email (email)" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                "CREATE TABLE IF NOT EXISTS users (" +
                    "id VARCHAR(64) PRIMARY KEY, tenantid VARCHAR(64) NOT NULL, firstname VARCHAR(512), lastname VARCHAR(512), email VARCHAR(320) NOT NULL, " +
                    "passwordsha256 VARCHAR(64), isadmin TINYINT NOT NULL DEFAULT 0, istenantadmin TINYINT NOT NULL DEFAULT 0, " +
                    "active TINYINT NOT NULL DEFAULT 1, isprotected TINYINT NOT NULL DEFAULT 0, " +
                    "createdutc VARCHAR(32) NOT NULL, lastupdateutc VARCHAR(32) NOT NULL, " +
                    "UNIQUE KEY idx_users_tenant_email (tenantid, email)" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                "CREATE TABLE IF NOT EXISTS credentials (" +
                    "id VARCHAR(64) PRIMARY KEY, tenantid VARCHAR(64) NOT NULL, userid VARCHAR(64) NOT NULL, name VARCHAR(512), accesskey VARCHAR(512) NOT NULL, " +
                    "secretkeyencrypted TEXT, secretkeylast4 VARCHAR(64), authmode VARCHAR(64), lastusedutc VARCHAR(32), expiresutc VARCHAR(32), " +
                    "active TINYINT NOT NULL DEFAULT 1, isprotected TINYINT NOT NULL DEFAULT 0, " +
                    "createdutc VARCHAR(32) NOT NULL, lastupdateutc VARCHAR(32) NOT NULL, " +
                    "UNIQUE KEY idx_credentials_accesskey (accesskey), " +
                    "KEY idx_credentials_tenant_user (tenantid, userid)" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                "CREATE TABLE IF NOT EXISTS authsessions (" +
                    "id VARCHAR(64) PRIMARY KEY, tenantid VARCHAR(64), accountid VARCHAR(64), administratorid VARCHAR(64), userid VARCHAR(64), credentialid VARCHAR(64), " +
                    "principaltype VARCHAR(64), authscheme VARCHAR(64), tokenid VARCHAR(512), sourceip VARCHAR(64), useragent VARCHAR(512), expiresutc VARCHAR(32) NOT NULL, " +
                    "lastusedutc VARCHAR(32), revokedutc VARCHAR(32), revocationreason TEXT, active TINYINT NOT NULL DEFAULT 1, " +
                    "isprotected TINYINT NOT NULL DEFAULT 0, createdutc VARCHAR(32) NOT NULL, lastupdateutc VARCHAR(32) NOT NULL, " +
                    "KEY idx_authsessions_tenant (tenantid)" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                "CREATE TABLE IF NOT EXISTS userroles (" +
                    "id VARCHAR(64) PRIMARY KEY, tenantid VARCHAR(64), name VARCHAR(512) NOT NULL, isbuiltin TINYINT NOT NULL DEFAULT 0, " +
                    "active TINYINT NOT NULL DEFAULT 1, isprotected TINYINT NOT NULL DEFAULT 0, " +
                    "createdutc VARCHAR(32) NOT NULL, lastupdateutc VARCHAR(32) NOT NULL, " +
                    "KEY idx_userroles_tenant_name (tenantid, name)" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                "CREATE TABLE IF NOT EXISTS permissions (" +
                    "id VARCHAR(64) PRIMARY KEY, tenantid VARCHAR(64), name VARCHAR(512), resourcetypes TEXT, operationtypes TEXT, " +
                    "permissiontype VARCHAR(64), active TINYINT NOT NULL DEFAULT 1, isprotected TINYINT NOT NULL DEFAULT 0, " +
                    "createdutc VARCHAR(32) NOT NULL, lastupdateutc VARCHAR(32) NOT NULL" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                "CREATE TABLE IF NOT EXISTS rolepermissionmaps (" +
                    "id VARCHAR(64) PRIMARY KEY, tenantid VARCHAR(64), roleid VARCHAR(64) NOT NULL, permissionid VARCHAR(64) NOT NULL, " +
                    "active TINYINT NOT NULL DEFAULT 1, isprotected TINYINT NOT NULL DEFAULT 0, " +
                    "createdutc VARCHAR(32) NOT NULL, lastupdateutc VARCHAR(32) NOT NULL, " +
                    "KEY idx_rpm_role (roleid)" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                "CREATE TABLE IF NOT EXISTS userroleassignments (" +
                    "id VARCHAR(64) PRIMARY KEY, tenantid VARCHAR(64) NOT NULL, userid VARCHAR(64) NOT NULL, roleid VARCHAR(64), rolename VARCHAR(512), " +
                    "resourcescope VARCHAR(64), resourceid VARCHAR(64), inheritstochildren TINYINT NOT NULL DEFAULT 1, " +
                    "active TINYINT NOT NULL DEFAULT 1, isprotected TINYINT NOT NULL DEFAULT 0, " +
                    "createdutc VARCHAR(32) NOT NULL, lastupdateutc VARCHAR(32) NOT NULL, " +
                    "KEY idx_ura_tenant_user (tenantid, userid)" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                "CREATE TABLE IF NOT EXISTS userrolemaps (" +
                    "id VARCHAR(64) PRIMARY KEY, tenantid VARCHAR(64) NOT NULL, userid VARCHAR(64) NOT NULL, roleid VARCHAR(64) NOT NULL, " +
                    "active TINYINT NOT NULL DEFAULT 1, isprotected TINYINT NOT NULL DEFAULT 0, " +
                    "createdutc VARCHAR(32) NOT NULL, lastupdateutc VARCHAR(32) NOT NULL, " +
                    "KEY idx_urm_tenant_user (tenantid, userid)" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                "CREATE TABLE IF NOT EXISTS credentialscopeassignments (" +
                    "id VARCHAR(64) PRIMARY KEY, tenantid VARCHAR(64) NOT NULL, credentialid VARCHAR(64) NOT NULL, roleid VARCHAR(64), rolename VARCHAR(512), " +
                    "resourcescope VARCHAR(64), resourceid VARCHAR(64), permissions TEXT, resourcetypes TEXT, " +
                    "active TINYINT NOT NULL DEFAULT 1, isprotected TINYINT NOT NULL DEFAULT 0, " +
                    "createdutc VARCHAR(32) NOT NULL, lastupdateutc VARCHAR(32) NOT NULL, " +
                    "KEY idx_csa_tenant_cred (tenantid, credentialid)" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                "CREATE TABLE IF NOT EXISTS audit (" +
                    "id VARCHAR(64) PRIMARY KEY, eventtype VARCHAR(64), tenantid VARCHAR(64), userid VARCHAR(64), credentialid VARCHAR(64), sessionid VARCHAR(64), " +
                    "resourceid VARCHAR(64), principaltype VARCHAR(64), authscheme VARCHAR(64), requestid VARCHAR(64), httpmethod VARCHAR(64), urlpath VARCHAR(512), " +
                    "sourceip VARCHAR(64), authenticationresult VARCHAR(64), authorizationresult VARCHAR(64), requiredresourcetype VARCHAR(64), " +
                    "requiredoperation VARCHAR(64), denialreason TEXT, bypassreason TEXT, statuscode INT, createdutc VARCHAR(32) NOT NULL, " +
                    "KEY idx_audit_tenant_created (tenantid, createdutc)" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                "CREATE TABLE IF NOT EXISTS requesthistory (" +
                    "id VARCHAR(64) PRIMARY KEY, tenantid VARCHAR(64), userid VARCHAR(64), principalname VARCHAR(512), method VARCHAR(64), path VARCHAR(512), url VARCHAR(512), " +
                    "statuscode INT, durationms DOUBLE, sourceip VARCHAR(64), requestheaders TEXT, requestbody TEXT, " +
                    "requestbodybytes BIGINT, requestbodytruncated TINYINT, responseheaders TEXT, responsebody TEXT, " +
                    "responsebodybytes BIGINT, responsebodytruncated TINYINT, createdutc VARCHAR(32) NOT NULL, completedutc VARCHAR(32), " +
                    "KEY idx_reqhist_tenant_created (tenantid, createdutc), " +
                    "KEY idx_reqhist_created (createdutc)" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                "CREATE TABLE IF NOT EXISTS subjects (" +
                    "id VARCHAR(64) PRIMARY KEY, tenantid VARCHAR(64) NOT NULL, displayname VARCHAR(512) NOT NULL, type VARCHAR(64), description TEXT, " +
                    "graphrootnodeid VARCHAR(64), active TINYINT NOT NULL DEFAULT 1, isprotected TINYINT NOT NULL DEFAULT 0, " +
                    "createdutc VARCHAR(32) NOT NULL, lastupdateutc VARCHAR(32) NOT NULL, " +
                    "KEY idx_subjects_tenant (tenantid)" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                "CREATE TABLE IF NOT EXISTS subjectlinks (" +
                    "id VARCHAR(64) PRIMARY KEY, tenantid VARCHAR(64) NOT NULL, subjectid VARCHAR(64) NOT NULL, url VARCHAR(512) NOT NULL, title VARCHAR(512), " +
                    "submittedbyuserid VARCHAR(64), status VARCHAR(64), lastingestedutc VARCHAR(32), lasterror TEXT, " +
                    "active TINYINT NOT NULL DEFAULT 1, isprotected TINYINT NOT NULL DEFAULT 0, " +
                    "createdutc VARCHAR(32) NOT NULL, lastupdateutc VARCHAR(32) NOT NULL, " +
                    "KEY idx_subjectlinks_tenant_subject (tenantid, subjectid)" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                "CREATE TABLE IF NOT EXISTS ingestionjobs (" +
                    "id VARCHAR(64) PRIMARY KEY, tenantid VARCHAR(64) NOT NULL, subjectid VARCHAR(64) NOT NULL, linkid VARCHAR(64) NOT NULL, sourceurl VARCHAR(512), " +
                    "status VARCHAR(64), stage VARCHAR(64), attemptcount INT NOT NULL DEFAULT 0, error TEXT, documenttype VARCHAR(64), blobkey VARCHAR(512), " +
                    "graphnodeids TEXT, verbexdocumentids TEXT, startedutc VARCHAR(32), completedutc VARCHAR(32), " +
                    "createdutc VARCHAR(32) NOT NULL, lastupdateutc VARCHAR(32) NOT NULL, " +
                    "KEY idx_jobs_tenant_status (tenantid, status), " +
                    "KEY idx_jobs_status_created (status, createdutc)" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                "CREATE TABLE IF NOT EXISTS ingestionjobevents (" +
                    "id VARCHAR(64) PRIMARY KEY, tenantid VARCHAR(64) NOT NULL, jobid VARCHAR(64) NOT NULL, stage VARCHAR(64), status VARCHAR(64), message TEXT, " +
                    "durationms DOUBLE, createdutc VARCHAR(32) NOT NULL, " +
                    "KEY idx_jobevents_job (jobid, createdutc)" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                "CREATE TABLE IF NOT EXISTS modelrunners (" +
                    "id VARCHAR(64) PRIMARY KEY, tenantid VARCHAR(64), name VARCHAR(512) NOT NULL, provider VARCHAR(64), baseurl VARCHAR(512), apitype VARCHAR(64), " +
                    "authmaterialencrypted TEXT, capabilities TEXT, runnerusage VARCHAR(64), defaultmodel VARCHAR(512), defaultembeddingmodel VARCHAR(512), " +
                    "active TINYINT NOT NULL DEFAULT 1, isprotected TINYINT NOT NULL DEFAULT 0, " +
                    "createdutc VARCHAR(32) NOT NULL, lastupdateutc VARCHAR(32) NOT NULL, " +
                    "KEY idx_modelrunners_tenant_name (tenantid, name)" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                "CREATE TABLE IF NOT EXISTS prompts (" +
                    "id VARCHAR(64) PRIMARY KEY, tenantid VARCHAR(64), promptkey VARCHAR(512) NOT NULL, name VARCHAR(512), content TEXT, version INT NOT NULL DEFAULT 1, " +
                    "active TINYINT NOT NULL DEFAULT 1, isprotected TINYINT NOT NULL DEFAULT 0, " +
                    "createdutc VARCHAR(32) NOT NULL, lastupdateutc VARCHAR(32) NOT NULL, " +
                    "KEY idx_prompts_tenant_key (tenantid, promptkey)" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;"
            };
        }
    }
}
