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
                list.Add(new SchemaMigration(14, "Add RAG evaluation facts, runs, and results", new List<string>
                {
                    "IF OBJECT_ID(N'dbo.evalfacts', N'U') IS NULL CREATE TABLE dbo.evalfacts (id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, subjectid NVARCHAR(64) NOT NULL, question NVARCHAR(MAX), expectedanswer NVARCHAR(MAX), category NVARCHAR(128), createdutc NVARCHAR(32) NOT NULL);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_evalfacts_tenant_subject' AND object_id = OBJECT_ID(N'dbo.evalfacts')) CREATE INDEX idx_evalfacts_tenant_subject ON dbo.evalfacts (tenantid, subjectid, createdutc);",
                    "IF OBJECT_ID(N'dbo.evalruns', N'U') IS NULL CREATE TABLE dbo.evalruns (id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, subjectid NVARCHAR(64) NOT NULL, status NVARCHAR(32), category NVARCHAR(128), totalfacts INT NOT NULL DEFAULT 0, passcount INT NOT NULL DEFAULT 0, partialcount INT NOT NULL DEFAULT 0, failcount INT NOT NULL DEFAULT 0, judgemodel NVARCHAR(512), error NVARCHAR(MAX), createdutc NVARCHAR(32) NOT NULL, finishedutc NVARCHAR(32));",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_evalruns_tenant_subject' AND object_id = OBJECT_ID(N'dbo.evalruns')) CREATE INDEX idx_evalruns_tenant_subject ON dbo.evalruns (tenantid, subjectid, createdutc);",
                    "IF OBJECT_ID(N'dbo.evalresults', N'U') IS NULL CREATE TABLE dbo.evalresults (id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, runid NVARCHAR(64) NOT NULL, factid NVARCHAR(64), question NVARCHAR(MAX), expectedanswer NVARCHAR(MAX), producedanswer NVARCHAR(MAX), verdict NVARCHAR(32), score FLOAT, reason NVARCHAR(MAX), failuremode NVARCHAR(128), category NVARCHAR(128), createdutc NVARCHAR(32) NOT NULL);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_evalresults_run' AND object_id = OBJECT_ID(N'dbo.evalresults')) CREATE INDEX idx_evalresults_run ON dbo.evalresults (runid);"
                }));
                list.Add(new SchemaMigration(15, "Add ingestion labels and tags to links and jobs", new List<string>
                {
                    "IF COL_LENGTH('dbo.subjectlinks', 'labelsjson') IS NULL ALTER TABLE dbo.subjectlinks ADD labelsjson NVARCHAR(MAX);",
                    "IF COL_LENGTH('dbo.subjectlinks', 'tagsjson') IS NULL ALTER TABLE dbo.subjectlinks ADD tagsjson NVARCHAR(MAX);",
                    "IF COL_LENGTH('dbo.ingestionjobs', 'labelsjson') IS NULL ALTER TABLE dbo.ingestionjobs ADD labelsjson NVARCHAR(MAX);",
                    "IF COL_LENGTH('dbo.ingestionjobs', 'tagsjson') IS NULL ALTER TABLE dbo.ingestionjobs ADD tagsjson NVARCHAR(MAX);"
                }));
                list.Add(new SchemaMigration(16, "Add subject consumer-chat publish flag and link content hash", new List<string>
                {
                    "IF COL_LENGTH('dbo.subjects', 'publishedforchat') IS NULL ALTER TABLE dbo.subjects ADD publishedforchat INT NOT NULL DEFAULT 1;",
                    "IF COL_LENGTH('dbo.subjectlinks', 'contenthash') IS NULL ALTER TABLE dbo.subjectlinks ADD contenthash NVARCHAR(MAX);"
                }));
                list.Add(new SchemaMigration(17, "Add subject chunking configuration", new List<string>
                {
                    "IF COL_LENGTH('dbo.subjects', 'chunkstrategy') IS NULL ALTER TABLE dbo.subjects ADD chunkstrategy NVARCHAR(MAX);",
                    "IF COL_LENGTH('dbo.subjects', 'chunkmaxtokens') IS NULL ALTER TABLE dbo.subjects ADD chunkmaxtokens INT NOT NULL DEFAULT 256;",
                    "IF COL_LENGTH('dbo.subjects', 'chunkoverlaptokens') IS NULL ALTER TABLE dbo.subjects ADD chunkoverlaptokens INT NOT NULL DEFAULT 32;"
                }));
                list.Add(new SchemaMigration(18, "Add subject reranker type", new List<string>
                {
                    "IF COL_LENGTH('dbo.subjects', 'rerankertype') IS NULL ALTER TABLE dbo.subjects ADD rerankertype NVARCHAR(64) NOT NULL DEFAULT 'LlmListwise';"
                }));
                list.Add(new SchemaMigration(19, "Add model runner provider-specific fields", new List<string>
                {
                    "IF COL_LENGTH('dbo.modelrunners', 'deployment') IS NULL ALTER TABLE dbo.modelrunners ADD deployment NVARCHAR(MAX);",
                    "IF COL_LENGTH('dbo.modelrunners', 'apiversion') IS NULL ALTER TABLE dbo.modelrunners ADD apiversion NVARCHAR(MAX);",
                    "IF COL_LENGTH('dbo.modelrunners', 'region') IS NULL ALTER TABLE dbo.modelrunners ADD region NVARCHAR(MAX);",
                    "IF COL_LENGTH('dbo.modelrunners', 'project') IS NULL ALTER TABLE dbo.modelrunners ADD project NVARCHAR(MAX);",
                    "IF COL_LENGTH('dbo.modelrunners', 'accesskeyid') IS NULL ALTER TABLE dbo.modelrunners ADD accesskeyid NVARCHAR(MAX);",
                    "IF COL_LENGTH('dbo.modelrunners', 'sessiontokenencrypted') IS NULL ALTER TABLE dbo.modelrunners ADD sessiontokenencrypted NVARCHAR(MAX);",
                    "IF COL_LENGTH('dbo.modelrunners', 'contextsize') IS NULL ALTER TABLE dbo.modelrunners ADD contextsize INT NOT NULL DEFAULT 0;"
                }));
                list.Add(new SchemaMigration(20, "Add per-subject prompt overrides", new List<string>
                {
                    "IF OBJECT_ID(N'dbo.subjectprompts', N'U') IS NULL CREATE TABLE dbo.subjectprompts (" +
                        "id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, subjectid NVARCHAR(64) NOT NULL, promptkey NVARCHAR(128) NOT NULL, " +
                        "content NVARCHAR(MAX), mergemode NVARCHAR(16) NOT NULL DEFAULT 'Append', createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_subjectprompts_key' AND object_id = OBJECT_ID(N'dbo.subjectprompts')) CREATE UNIQUE INDEX idx_subjectprompts_key ON dbo.subjectprompts (tenantid, subjectid, promptkey);"
                }));
                list.Add(new SchemaMigration(21, "Add link background-deletion status", new List<string>
                {
                    "IF COL_LENGTH('dbo.subjectlinks', 'deletionstatus') IS NULL ALTER TABLE dbo.subjectlinks ADD deletionstatus NVARCHAR(32) NOT NULL DEFAULT 'None';"
                }));
                list.Add(new SchemaMigration(22, "Add model runner health-check and concurrency config", new List<string>
                {
                    "IF COL_LENGTH('dbo.modelrunners', 'maxconcurrentrequests') IS NULL ALTER TABLE dbo.modelrunners ADD maxconcurrentrequests INT NOT NULL DEFAULT 2;",
                    "IF COL_LENGTH('dbo.modelrunners', 'maxqueuedepth') IS NULL ALTER TABLE dbo.modelrunners ADD maxqueuedepth INT NOT NULL DEFAULT 0;",
                    "IF COL_LENGTH('dbo.modelrunners', 'maximumtimeoutms') IS NULL ALTER TABLE dbo.modelrunners ADD maximumtimeoutms INT NOT NULL DEFAULT 60000;",
                    "IF COL_LENGTH('dbo.modelrunners', 'healthcheckenabled') IS NULL ALTER TABLE dbo.modelrunners ADD healthcheckenabled BIT NOT NULL DEFAULT 0;",
                    "IF COL_LENGTH('dbo.modelrunners', 'healthcheckurl') IS NULL ALTER TABLE dbo.modelrunners ADD healthcheckurl NVARCHAR(MAX);",
                    "IF COL_LENGTH('dbo.modelrunners', 'healthcheckmethod') IS NULL ALTER TABLE dbo.modelrunners ADD healthcheckmethod NVARCHAR(16);",
                    "IF COL_LENGTH('dbo.modelrunners', 'healthcheckintervalms') IS NULL ALTER TABLE dbo.modelrunners ADD healthcheckintervalms INT NOT NULL DEFAULT 0;",
                    "IF COL_LENGTH('dbo.modelrunners', 'healthchecktimeoutms') IS NULL ALTER TABLE dbo.modelrunners ADD healthchecktimeoutms INT NOT NULL DEFAULT 0;",
                    "IF COL_LENGTH('dbo.modelrunners', 'healthcheckexpectedstatuscode') IS NULL ALTER TABLE dbo.modelrunners ADD healthcheckexpectedstatuscode INT NOT NULL DEFAULT 200;",
                    "IF COL_LENGTH('dbo.modelrunners', 'healthythreshold') IS NULL ALTER TABLE dbo.modelrunners ADD healthythreshold INT NOT NULL DEFAULT 2;",
                    "IF COL_LENGTH('dbo.modelrunners', 'unhealthythreshold') IS NULL ALTER TABLE dbo.modelrunners ADD unhealthythreshold INT NOT NULL DEFAULT 2;",
                    "IF COL_LENGTH('dbo.modelrunners', 'healthcheckuseauth') IS NULL ALTER TABLE dbo.modelrunners ADD healthcheckuseauth BIT NOT NULL DEFAULT 0;"
                }));
                list.Add(new SchemaMigration(23, "Add tenant deletion status", new List<string>
                {
                    "IF COL_LENGTH('dbo.tenants', 'deletionstatus') IS NULL ALTER TABLE dbo.tenants ADD deletionstatus NVARCHAR(32) NOT NULL DEFAULT 'None';"
                }));
                list.Add(new SchemaMigration(24, "Enable health checks on existing model runners", new List<string>
                {
                    "UPDATE dbo.modelrunners SET healthcheckenabled = 1;"
                }));
                list.Add(new SchemaMigration(25, "Add ingestion job background-deletion status", new List<string>
                {
                    "IF COL_LENGTH('dbo.ingestionjobs', 'deletionstatus') IS NULL ALTER TABLE dbo.ingestionjobs ADD deletionstatus NVARCHAR(32) NOT NULL DEFAULT 'None';"
                }));
                list.Add(new SchemaMigration(26, "Add ingestion tuning singleton and subject concurrency overrides", new List<string>
                {
                    "IF OBJECT_ID(N'dbo.ingestiontuning', N'U') IS NULL CREATE TABLE dbo.ingestiontuning (" +
                        "id NVARCHAR(64) PRIMARY KEY, contentretrieval INT NOT NULL, typedetection INT NOT NULL, cellextraction INT NOT NULL, " +
                        "classification INT NOT NULL, graphmerge INT NOT NULL, summarization INT NOT NULL, chunking INT NOT NULL, " +
                        "embedding INT NOT NULL, indexing INT NOT NULL, maxconcurrenttasks INT NOT NULL, summarizationconcurrency INT NOT NULL, " +
                        "summarizationmincelllength INT NOT NULL, stagetimeoutseconds INT NOT NULL, createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",
                    "IF COL_LENGTH('dbo.subjects', 'concurrencyoverridesjson') IS NULL ALTER TABLE dbo.subjects ADD concurrencyoverridesjson NVARCHAR(MAX);"
                }));
                list.Add(new SchemaMigration(27, "Add classification batching tuning", new List<string>
                {
                    "IF COL_LENGTH('dbo.ingestiontuning', 'classificationbatchsize') IS NULL ALTER TABLE dbo.ingestiontuning ADD classificationbatchsize INT NOT NULL DEFAULT 25;",
                    "IF COL_LENGTH('dbo.ingestiontuning', 'classificationbatchoverlap') IS NULL ALTER TABLE dbo.ingestiontuning ADD classificationbatchoverlap INT NOT NULL DEFAULT 3;",
                    "IF COL_LENGTH('dbo.ingestiontuning', 'classificationbatchconcurrency') IS NULL ALTER TABLE dbo.ingestiontuning ADD classificationbatchconcurrency INT NOT NULL DEFAULT 4;"
                }));
                list.Add(new SchemaMigration(28, "Add ingestion failure categories, warnings, completeness counters, and job attempts", new List<string>
                {
                    "IF COL_LENGTH('dbo.ingestionjobs', 'failurecategory') IS NULL ALTER TABLE dbo.ingestionjobs ADD failurecategory NVARCHAR(256);",
                    "IF COL_LENGTH('dbo.ingestionjobs', 'warningsjson') IS NULL ALTER TABLE dbo.ingestionjobs ADD warningsjson NVARCHAR(MAX);",
                    "IF COL_LENGTH('dbo.ingestionjobs', 'cellsextracted') IS NULL ALTER TABLE dbo.ingestionjobs ADD cellsextracted INT NOT NULL DEFAULT 0;",
                    "IF COL_LENGTH('dbo.ingestionjobs', 'classificationbatches') IS NULL ALTER TABLE dbo.ingestionjobs ADD classificationbatches INT NOT NULL DEFAULT 0;",
                    "IF COL_LENGTH('dbo.ingestionjobs', 'classificationbatchesfailed') IS NULL ALTER TABLE dbo.ingestionjobs ADD classificationbatchesfailed INT NOT NULL DEFAULT 0;",
                    "IF COL_LENGTH('dbo.ingestionjobs', 'cellnodescreated') IS NULL ALTER TABLE dbo.ingestionjobs ADD cellnodescreated INT NOT NULL DEFAULT 0;",
                    "IF COL_LENGTH('dbo.ingestionjobs', 'cellnodesfailed') IS NULL ALTER TABLE dbo.ingestionjobs ADD cellnodesfailed INT NOT NULL DEFAULT 0;",
                    "IF COL_LENGTH('dbo.ingestionjobs', 'summariesattempted') IS NULL ALTER TABLE dbo.ingestionjobs ADD summariesattempted INT NOT NULL DEFAULT 0;",
                    "IF COL_LENGTH('dbo.ingestionjobs', 'summariesfailed') IS NULL ALTER TABLE dbo.ingestionjobs ADD summariesfailed INT NOT NULL DEFAULT 0;",
                    "IF COL_LENGTH('dbo.ingestionjobs', 'chunksproduced') IS NULL ALTER TABLE dbo.ingestionjobs ADD chunksproduced INT NOT NULL DEFAULT 0;",
                    "IF COL_LENGTH('dbo.ingestionjobs', 'chunksembedded') IS NULL ALTER TABLE dbo.ingestionjobs ADD chunksembedded INT NOT NULL DEFAULT 0;",
                    "IF COL_LENGTH('dbo.ingestionjobs', 'chunksindexed') IS NULL ALTER TABLE dbo.ingestionjobs ADD chunksindexed INT NOT NULL DEFAULT 0;",
                    "IF COL_LENGTH('dbo.subjectlinks', 'failurecategory') IS NULL ALTER TABLE dbo.subjectlinks ADD failurecategory NVARCHAR(256);",
                    "IF COL_LENGTH('dbo.subjectlinks', 'warningcount') IS NULL ALTER TABLE dbo.subjectlinks ADD warningcount INT NOT NULL DEFAULT 0;",
                    "IF OBJECT_ID(N'dbo.ingestionjobattempts', N'U') IS NULL CREATE TABLE dbo.ingestionjobattempts (id NVARCHAR(64) PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, jobid NVARCHAR(64) NOT NULL, attemptnumber INT NOT NULL DEFAULT 1, succeeded INT NOT NULL DEFAULT 0, stage NVARCHAR(256), failurecategory NVARCHAR(256), message NVARCHAR(MAX), startedutc NVARCHAR(32) NOT NULL, endedutc NVARCHAR(32) NOT NULL, createdutc NVARCHAR(32) NOT NULL);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_ingestionjobattempts_tenant_job' AND object_id = OBJECT_ID(N'dbo.ingestionjobattempts')) CREATE INDEX idx_ingestionjobattempts_tenant_job ON dbo.ingestionjobattempts (tenantid, jobid, attemptnumber);"
                }));
                list.Add(new SchemaMigration(29, "Add model runner maximum retries", new List<string>
                {
                    "IF COL_LENGTH('dbo.modelrunners', 'maxretries') IS NULL ALTER TABLE dbo.modelrunners ADD maxretries INT NOT NULL DEFAULT 5;"
                }));
                list.Add(new SchemaMigration(30, "Add link current job id", new List<string>
                {
                    "IF COL_LENGTH('dbo.subjectlinks', 'currentjobid') IS NULL ALTER TABLE dbo.subjectlinks ADD currentjobid NVARCHAR(64);"
                }));
                list.Add(new SchemaMigration(31, "Add model runner maximum input tokens and subject chunk headers", new List<string>
                {
                    "IF COL_LENGTH('dbo.modelrunners', 'maxinputtokens') IS NULL ALTER TABLE dbo.modelrunners ADD maxinputtokens INT NOT NULL DEFAULT 0;",
                    "IF COL_LENGTH('dbo.subjects', 'chunkheaders') IS NULL ALTER TABLE dbo.subjects ADD chunkheaders NVARCHAR(256) DEFAULT 'None';"
                }));
                list.Add(new SchemaMigration(32, "Add link source kind, external key, content type, size, and crawl plan", new List<string>
                {
                    "IF COL_LENGTH('dbo.subjectlinks', 'sourcekind') IS NULL ALTER TABLE dbo.subjectlinks ADD sourcekind NVARCHAR(256) NOT NULL DEFAULT 'Url';",
                    "IF COL_LENGTH('dbo.subjectlinks', 'externalkey') IS NULL ALTER TABLE dbo.subjectlinks ADD externalkey NVARCHAR(256);",
                    "IF COL_LENGTH('dbo.subjectlinks', 'contenttype') IS NULL ALTER TABLE dbo.subjectlinks ADD contenttype NVARCHAR(256);",
                    "IF COL_LENGTH('dbo.subjectlinks', 'sizebytes') IS NULL ALTER TABLE dbo.subjectlinks ADD sizebytes BIGINT NOT NULL DEFAULT 0;",
                    "IF COL_LENGTH('dbo.subjectlinks', 'crawlplanid') IS NULL ALTER TABLE dbo.subjectlinks ADD crawlplanid NVARCHAR(64);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_subjectlinks_tenant_crawlplan' AND object_id = OBJECT_ID(N'dbo.subjectlinks')) CREATE INDEX idx_subjectlinks_tenant_crawlplan ON dbo.subjectlinks (tenantid, crawlplanid);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_subjectlinks_tenant_subject_externalkey' AND object_id = OBJECT_ID(N'dbo.subjectlinks')) CREATE UNIQUE INDEX idx_subjectlinks_tenant_subject_externalkey ON dbo.subjectlinks (tenantid, subjectid, externalkey) WHERE externalkey IS NOT NULL;"
                }));
                list.Add(new SchemaMigration(33, "Add crawl plans, settings, secrets, objects, operations, and operation objects", new List<string>
                {
                    "IF OBJECT_ID(N'dbo.crawlplans', N'U') IS NULL CREATE TABLE dbo.crawlplans (id NVARCHAR(64) NOT NULL PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, subjectid NVARCHAR(64) NOT NULL, name NVARCHAR(256) NOT NULL, plantype NVARCHAR(256) NOT NULL, enabled INT NOT NULL DEFAULT 1, status NVARCHAR(256) NOT NULL DEFAULT 'Idle', filterminsizebytes BIGINT NOT NULL DEFAULT 0, filtermaxsizebytes BIGINT NOT NULL DEFAULT 0, filtermaxobjects INT NOT NULL DEFAULT 0, scheduletype NVARCHAR(256) NOT NULL DEFAULT 'Manual', scheduleintervalminutes INT NOT NULL DEFAULT 1440, schedulecron NVARCHAR(256), scheduletimezone NVARCHAR(256) NOT NULL DEFAULT 'UTC', processadditions INT NOT NULL DEFAULT 1, processupdates INT NOT NULL DEFAULT 1, processdeletions INT NOT NULL DEFAULT 0, maxdeletionfraction FLOAT NOT NULL DEFAULT 0.2, retryfailedobjects INT NOT NULL DEFAULT 1, operationretentiondays INT NOT NULL DEFAULT 30, lastoperationid NVARCHAR(64), lastrunutc NVARCHAR(32), lastsuccessutc NVARCHAR(32), nextrunutc NVARCHAR(32), claimtoken NVARCHAR(64), claimexpiresutc NVARCHAR(32), createdutc NVARCHAR(32) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_crawlplans_tenant_subject' AND object_id = OBJECT_ID(N'dbo.crawlplans')) CREATE INDEX idx_crawlplans_tenant_subject ON dbo.crawlplans (tenantid, subjectid);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_crawlplans_nextrun' AND object_id = OBJECT_ID(N'dbo.crawlplans')) CREATE INDEX idx_crawlplans_nextrun ON dbo.crawlplans (nextrunutc);",
                    "IF OBJECT_ID(N'dbo.crawlplansettings', N'U') IS NULL CREATE TABLE dbo.crawlplansettings (tenantid NVARCHAR(64) NOT NULL, planid NVARCHAR(64) NOT NULL, name NVARCHAR(256) NOT NULL, ordinal INT NOT NULL DEFAULT 0, settingvalue NVARCHAR(MAX));",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_crawlplansettings_plan_name_ordinal' AND object_id = OBJECT_ID(N'dbo.crawlplansettings')) CREATE UNIQUE INDEX idx_crawlplansettings_plan_name_ordinal ON dbo.crawlplansettings (tenantid, planid, name, ordinal);",
                    "IF OBJECT_ID(N'dbo.crawlplansecrets', N'U') IS NULL CREATE TABLE dbo.crawlplansecrets (tenantid NVARCHAR(64) NOT NULL, planid NVARCHAR(64) NOT NULL, name NVARCHAR(256) NOT NULL, ciphertext NVARCHAR(MAX) NOT NULL, lastupdateutc NVARCHAR(32) NOT NULL);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_crawlplansecrets_plan_name' AND object_id = OBJECT_ID(N'dbo.crawlplansecrets')) CREATE UNIQUE INDEX idx_crawlplansecrets_plan_name ON dbo.crawlplansecrets (tenantid, planid, name);",
                    "IF OBJECT_ID(N'dbo.crawlobjects', N'U') IS NULL CREATE TABLE dbo.crawlobjects (id NVARCHAR(64) NOT NULL PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, planid NVARCHAR(64) NOT NULL, externalkey NVARCHAR(MAX) NOT NULL, linkid NVARCHAR(64), versiontoken NVARCHAR(MAX), sizebytes BIGINT NOT NULL DEFAULT 0, contenttype NVARCHAR(256), status NVARCHAR(256) NOT NULL DEFAULT 'Active', lasterror NVARCHAR(MAX), lastoperationid NVARCHAR(64), firstseenutc NVARCHAR(32) NOT NULL, lastseenutc NVARCHAR(32) NOT NULL);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_crawlobjects_tenant_plan' AND object_id = OBJECT_ID(N'dbo.crawlobjects')) CREATE INDEX idx_crawlobjects_tenant_plan ON dbo.crawlobjects (tenantid, planid);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_crawlobjects_tenant_link' AND object_id = OBJECT_ID(N'dbo.crawlobjects')) CREATE INDEX idx_crawlobjects_tenant_link ON dbo.crawlobjects (tenantid, linkid);",
                    "IF OBJECT_ID(N'dbo.crawloperations', N'U') IS NULL CREATE TABLE dbo.crawloperations (id NVARCHAR(64) NOT NULL PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, planid NVARCHAR(64) NOT NULL, subjectid NVARCHAR(64) NOT NULL, triggeredby NVARCHAR(256) NOT NULL DEFAULT 'Manual', status NVARCHAR(256) NOT NULL DEFAULT 'Running', enumerated INT NOT NULL DEFAULT 0, added INT NOT NULL DEFAULT 0, updated INT NOT NULL DEFAULT 0, retried INT NOT NULL DEFAULT 0, unchanged INT NOT NULL DEFAULT 0, deleted INT NOT NULL DEFAULT 0, missing INT NOT NULL DEFAULT 0, skipped INT NOT NULL DEFAULT 0, failed INT NOT NULL DEFAULT 0, bytesenumerated BIGINT NOT NULL DEFAULT 0, helddeletions INT NOT NULL DEFAULT 0, error NVARCHAR(MAX), startedutc NVARCHAR(32) NOT NULL, enumeratedutc NVARCHAR(32), dispatchedutc NVARCHAR(32), finishedutc NVARCHAR(32));",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_crawloperations_tenant_plan' AND object_id = OBJECT_ID(N'dbo.crawloperations')) CREATE INDEX idx_crawloperations_tenant_plan ON dbo.crawloperations (tenantid, planid);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_crawloperations_status' AND object_id = OBJECT_ID(N'dbo.crawloperations')) CREATE INDEX idx_crawloperations_status ON dbo.crawloperations (status);",
                    "IF OBJECT_ID(N'dbo.crawloperationobjects', N'U') IS NULL CREATE TABLE dbo.crawloperationobjects (id NVARCHAR(64) NOT NULL PRIMARY KEY, tenantid NVARCHAR(64) NOT NULL, operationid NVARCHAR(64) NOT NULL, externalkey NVARCHAR(MAX) NOT NULL, crawlaction NVARCHAR(256) NOT NULL, succeeded INT, linkid NVARCHAR(64), jobid NVARCHAR(64), detail NVARCHAR(MAX), createdutc NVARCHAR(32) NOT NULL);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_crawloperationobjects_tenant_operation' AND object_id = OBJECT_ID(N'dbo.crawloperationobjects')) CREATE INDEX idx_crawloperationobjects_tenant_operation ON dbo.crawloperationobjects (tenantid, operationid);",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_crawloperationobjects_job' AND object_id = OBJECT_ID(N'dbo.crawloperationobjects')) CREATE INDEX idx_crawloperationobjects_job ON dbo.crawloperationobjects (jobid);"
                }));
                list.Add(new SchemaMigration(34, "Add scheduled link refresh (link interval, next refresh, source ETag and Last-Modified; subject default) and job trigger", new List<string>
                {
                    "IF COL_LENGTH('dbo.subjectlinks', 'refreshintervalminutes') IS NULL ALTER TABLE dbo.subjectlinks ADD refreshintervalminutes INT;",
                    "IF COL_LENGTH('dbo.subjectlinks', 'nextrefreshutc') IS NULL ALTER TABLE dbo.subjectlinks ADD nextrefreshutc NVARCHAR(32);",
                    "IF COL_LENGTH('dbo.subjectlinks', 'lastrefreshutc') IS NULL ALTER TABLE dbo.subjectlinks ADD lastrefreshutc NVARCHAR(32);",
                    "IF COL_LENGTH('dbo.subjectlinks', 'refreshfailures') IS NULL ALTER TABLE dbo.subjectlinks ADD refreshfailures INT NOT NULL DEFAULT 0;",
                    "IF COL_LENGTH('dbo.subjectlinks', 'sourceetag') IS NULL ALTER TABLE dbo.subjectlinks ADD sourceetag NVARCHAR(256);",
                    "IF COL_LENGTH('dbo.subjectlinks', 'sourcelastmodifiedutc') IS NULL ALTER TABLE dbo.subjectlinks ADD sourcelastmodifiedutc NVARCHAR(32);",
                    "IF COL_LENGTH('dbo.subjects', 'defaultrefreshintervalminutes') IS NULL ALTER TABLE dbo.subjects ADD defaultrefreshintervalminutes INT NOT NULL DEFAULT 0;",
                    "IF COL_LENGTH('dbo.ingestionjobs', 'triggeredby') IS NULL ALTER TABLE dbo.ingestionjobs ADD triggeredby NVARCHAR(256) NOT NULL DEFAULT 'Submit';",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_subjectlinks_nextrefresh' AND object_id = OBJECT_ID(N'dbo.subjectlinks')) CREATE INDEX idx_subjectlinks_nextrefresh ON dbo.subjectlinks (nextrefreshutc);"
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
