// Specific help text for each server-settings field. Keyed by full dot-path first (the server serializes
// settings in camelCase, so keys match that), then by leaf key, then a generic fallback. Extracted from
// SettingsView so the view stays within the file-size guardrail.

const TIP_BY_PATH = {
  createdUtc: 'When this settings file was first created. Informational and read-only.',

  'rest.hostname': "Network interface the HTTP server binds to. '*' listens on all interfaces; set a specific IP to restrict. Restart required.",
  'rest.port': 'TCP port the HTTP server listens on. Restart required.',
  'rest.ssl': 'Whether the server terminates HTTPS itself (vs. running behind a TLS-terminating proxy). Restart required.',

  'cors.allowOrigins': "Browser origins allowed to call the API (CORS Access-Control-Allow-Origin). '*' allows any origin. Applied live.",
  'cors.allowMethods': 'HTTP methods advertised as allowed in CORS preflight responses.',
  'cors.allowHeaders': 'Request headers a browser may send cross-origin (CORS Access-Control-Allow-Headers).',
  'cors.exposeHeaders': 'Response headers the browser is allowed to read cross-origin.',
  'cors.maxAgeSeconds': 'How long (seconds) a browser may cache the CORS preflight result before re-checking.',

  'logging.consoleLogging': 'Write log output to the console / stdout.',
  'logging.fileLogging': 'Write log output to a rotating file in the log directory.',
  'logging.logDirectory': 'Directory where log files are written when file logging is on. Restart required.',
  'logging.logFilename': 'Base filename for the server log. Restart required.',
  'logging.minimumSeverity': 'Lowest severity that gets logged (lower number = more verbose; 0 logs everything).',
  'logging.logHttpRequests': 'Emit a log line for every inbound HTTP request. Verbose; separate from Request History capture.',

  'database.type': 'Database provider: Sqlite, Postgresql, Mysql, or SqlServer. Restart required.',
  'database.filename': 'SQLite database file path. Used only when the provider is Sqlite. Restart required.',
  'database.hostname': 'Database server host (Postgresql/Mysql/SqlServer). Restart required.',
  'database.port': 'Database server port. Restart required.',
  'database.databaseName': 'Name of the database/schema to connect to. Restart required.',
  'database.username': 'Database login username. Restart required.',
  'database.password': 'Database login password. Restart required.',
  'database.maxConnections': 'Maximum size of the connection pool to the database. Restart required.',
  'database.commandTimeoutSeconds': 'Per-query command timeout (seconds) before a database call is aborted.',

  'auth.issuer': 'Issuer name embedded in the session tokens this server mints.',
  'auth.tokenSigningKey': 'Secret key used to sign and verify dashboard session tokens. Changing it invalidates all existing sessions.',
  'auth.tokenLifetimeMinutes': 'How long a dashboard login session stays valid before re-authentication is required.',
  'auth.adminApiKeys': 'Bootstrap API keys that authenticate as the system administrator (bearer). Treat as secrets.',

  'requestHistory.enabled': 'Capture inbound API requests for the Request History view. Secrets are redacted and bodies truncated.',
  'requestHistory.maxRequestBodyBytes': 'Maximum request-body bytes stored per captured request; larger bodies are truncated.',
  'requestHistory.maxResponseBodyBytes': 'Maximum response-body bytes stored per captured request; larger bodies are truncated.',
  'requestHistory.retentionDays': 'How many days captured request history is kept before automatic pruning.',
  'requestHistory.pruneIntervalMinutes': 'How often (minutes) the background job prunes request history past its retention.',

  'ingestion.maxConcurrentTasks': 'How many ingestion jobs process in parallel (the job pool). The per-stage caps below still bound the expensive steps.',
  'ingestion.pollIntervalMs': 'How often (ms) the worker checks for the next queued job when the queue is empty.',
  'ingestion.maxAttempts': 'Attempts per job before it is marked failed. Transient errors (timeouts) retry; deterministic ones fail immediately.',
  'ingestion.retryBackoffBaseMs': 'Base delay (ms) before retrying a transient failure; grows exponentially with each attempt.',
  'ingestion.retryBackoffMaxMs': 'Ceiling (ms) on the exponential retry backoff between attempts.',
  'ingestion.embeddingCacheSize': "Maximum entries in the in-memory embedding cache so identical text isn't re-embedded. 0 disables it.",
  'ingestion.stageTimeoutSeconds': 'Abort a single pipeline stage if it runs longer than this many seconds (it is then retried).',
  'ingestion.summarizationMinCellLength': 'Cells shorter than this many characters are not summarized (skips trivial fragments). 0 summarizes every cell.',
  'ingestion.summarizationConcurrency': 'Cells summarized in parallel within one job — bounds the model calls one document issues at once.',
  'ingestion.classificationBatchSize': 'Cells classified per model call; a larger document is split into batches so no single call is oversized.',
  'ingestion.classificationBatchOverlap': 'Context cells included on each side of a classification batch so relationships spanning a batch boundary are still detected.',
  'ingestion.classificationBatchConcurrency': 'Classification batches from one document processed in parallel.',
  'ingestion.useHeadlessBrowser': 'Render pages in a headless browser before extraction so JavaScript-heavy sites ingest correctly.',
  'ingestion.browserNavigationTimeoutMs': 'Headless-browser page-navigation timeout (ms) before it falls back to a plain fetch or fails.',
  'ingestion.userAgent': 'User-Agent header sent when fetching source URLs.',

  'ingestion.stageConcurrency.contentRetrieval': 'Max source fetches running at once across all jobs.',
  'ingestion.stageConcurrency.typeDetection': 'Max document type-detection calls running at once across all jobs.',
  'ingestion.stageConcurrency.cellExtraction': 'Max cell-extraction (DocumentAtom) calls running at once across all jobs.',
  'ingestion.stageConcurrency.classification': 'Max jobs running the ontology-classification stage at once — bounds load on the completion model.',
  'ingestion.stageConcurrency.graphMerge': 'Max graph-merge / relationship-consolidation operations (LiteGraph writes) running at once.',
  'ingestion.stageConcurrency.summarization': 'Max jobs running the summarization stage at once — bounds completion-model load.',
  'ingestion.stageConcurrency.chunking': 'Max chunking operations running at once across all jobs.',
  'ingestion.stageConcurrency.embedding': 'Max jobs running the embedding stage at once — bounds embedding-model load.',
  'ingestion.stageConcurrency.indexing': 'Max index/upsert operations to the retrieval store running at once.',

  'integrations.documentAtom.endpoint': 'Base URL of the DocumentAtom service (type detection + cell extraction). Restart required.',
  'integrations.recallDb.endpoint': 'Base URL of the RecallDB retrieval store (vectors + full-text). Restart required.',
  'integrations.recallDb.bearerToken': 'Bearer token the server uses to authenticate to RecallDB. Restart required.',
  'integrations.recallDb.tenantId': 'RecallDB tenant id that Pneuma provisions and stores chunk documents under.',
  'integrations.recallDb.tenantName': 'Display name used when provisioning the RecallDB tenant.',
  'integrations.recallDb.defaultCollectionName': 'Name of the default RecallDB collection created per tenant.',
  'integrations.recallDb.defaultCollectionDimensionality': 'Vector dimensionality of the default collection. Must match the embedding model’s output size.',
  'integrations.liteGraph.endpoint': 'Base URL of the LiteGraph knowledge-graph store. Restart required.',
  'integrations.liteGraph.bearerToken': 'Bearer token the server uses to authenticate to LiteGraph. Restart required.',
  'integrations.liteGraph.tenantGuid': 'Default LiteGraph tenant GUID used as a fallback when a Pneuma tenant has no provisioned graph.',
  'integrations.blob.provider': 'Where raw fetched source bytes are stored: Disk or an S3-compatible store. Restart required.',
  'integrations.blob.directory': 'Filesystem directory for raw source blobs when the blob provider is Disk.',
  'integrations.resilience.timeoutMilliseconds': 'Per-request timeout (ms) for calls to subordinate services (DocumentAtom / RecallDB / LiteGraph).',
  'integrations.resilience.maxConcurrentRequests': 'Max concurrent outbound requests to any one subordinate service (a bulkhead against a slow dependency).',
  'integrations.resilience.retryCount': 'How many times a failed call to a subordinate service is retried.',
  'integrations.resilience.retryDelayMilliseconds': 'Delay (ms) between retries of a subordinate-service call.',

  'retrieval.useInvertedIndex': 'Use RecallDB full-text (lexical) search alongside vector search. Off = vector-only retrieval.',
  'retrieval.neighborExpansionEnabled': 'After finding relevant chunks, pull in their connected graph neighbors for richer, better-cited answers.',
  'retrieval.chatMaxToolIterations': 'Max tool-calling rounds the chat assistant may take before it must answer. Higher = more digging, more latency.',
  'retrieval.neighborExpansionMaxNodes': 'Upper bound on extra neighbor nodes added during expansion. Higher = more context but larger prompts.',
  'retrieval.neighborExpansionMaxHops': 'How many graph hops out from a hit that neighbor expansion may traverse.',
  'retrieval.communityMinSize': 'Minimum node count for a detected graph community to be summarized and used in answers.',
  'retrieval.communitySummaryMaxMembers': 'Maximum member nodes included when summarizing a community.',
  'retrieval.communityDetectionMaxIterations': 'Iteration cap for the community-detection algorithm.',
  'retrieval.vectorMinimumScore': 'Discard vector hits below this cosine similarity (0–1). Raise to keep only strong matches.',
  'retrieval.rrfK': 'Reciprocal-rank-fusion constant that blends lexical and vector rankings. Higher = flatter blend.',
  'retrieval.lexicalWeight': 'Weight given to full-text (lexical) scores when fusing them with vector scores.',
  'retrieval.semanticWeight': 'Weight given to vector (semantic) scores when fusing them with lexical scores.',
  'retrieval.diversityEnabled': "Re-rank results for diversity (MMR) so near-duplicate chunks don't crowd out the answer.",
  'retrieval.diversityLambda': 'Diversity/relevance trade-off for MMR. 0 = maximize diversity, 1 = pure relevance.',

  'modelRunner.maxConcurrentRequests': 'Server-wide cap on concurrent completion/answer requests before they queue.',
  'modelRunner.maxQueueDepth': 'How many answer requests may wait once at capacity before callers get HTTP 429.',

  's3.enabled': 'Store per-stage pipeline artifacts (source, atoms, chunks, embeddings, subgraph) in S3.',
  's3.endpoint': 'S3-compatible endpoint URL used for artifact storage.',
  's3.region': 'S3 region for artifact storage.',
  's3.accessKey': 'S3 access key id for artifact storage.',
  's3.secretKey': 'S3 secret access key for artifact storage.',
  's3.forcePathStyle': 'Use path-style bucket URLs (endpoint/bucket) instead of virtual-hosted — required by most S3-compatible servers.',
  's3.buckets.source': 'Bucket holding the raw fetched source bytes of each link.',
  's3.buckets.atoms': 'Bucket holding the extracted semantic cells (atoms) per link.',
  's3.buckets.chunks': 'Bucket holding the chunk texts produced per link.',
  's3.buckets.embeddings': 'Bucket holding the chunk embedding vectors produced per link.',
  's3.buckets.subgraph': 'Bucket holding the candidate subgraph proposed per link.',

  'telemetry.enabled': 'Emit OpenTelemetry traces and metrics for API requests and the ingestion pipeline.',
  'telemetry.serviceName': 'service.name reported on emitted telemetry.',
  'telemetry.otlpEndpoint': 'OTLP collector endpoint that traces are exported to.',
  'telemetry.otlpProtocol': "OTLP export protocol: 'grpc' (port 4317) or 'httpprotobuf' (port 4318, /v1/traces appended).",
  'telemetry.prometheusEnabled': 'Expose a Prometheus metrics endpoint for scraping.',

  'diagnostics.runStartupProbes': 'On startup, probe subordinate services (DocumentAtom / RecallDB / LiteGraph) and report their health.',
  'diagnostics.failFastOnStartupProbe': 'Abort startup if a subordinate-service probe fails, rather than starting in a degraded state.',

  'seed.accountName': 'Account name created on an empty database during first-boot seeding. Restart required.',
  'seed.tenantName': 'Default tenant name created during first-boot seeding. Restart required.',
  'seed.adminEmail': 'Email of the administrator created during first-boot seeding. Restart required.',
  'seed.adminPassword': 'Password for the seeded administrator (used only on first boot of an empty database). Restart required.',
  'seed.adminFirstName': 'First name of the seeded administrator.',
  'seed.adminLastName': 'Last name of the seeded administrator.',
  'seed.ollamaBaseUrl': 'Default Ollama base URL used when seeding the sample model endpoints.',
  'seed.openAiBaseUrl': 'Default OpenAI base URL used when seeding the sample model endpoints.',
  'seed.geminiBaseUrl': 'Default Gemini base URL used when seeding the sample model endpoints.'
};
// Fallback by leaf key, for any field not enumerated by full path above (e.g. a newly added setting).
const TIP_BY_KEY = {
  hostname: 'Network host this component binds to or connects to.',
  port: 'TCP port for this component.',
  endpoint: 'Base URL of the service the server calls.',
  bearerToken: 'Auth token sent to this dependency. Stored server-side; shown masked.',
  enabled: 'Turn this subsystem on or off.',
  provider: 'Which backend implementation to use for this feature.'
};

// Resolve the most specific help text for a settings field.
export function settingsTip(path, key) {
  return TIP_BY_PATH[path] || TIP_BY_KEY[key] || `Server configuration value (${path}). Some changes take effect immediately; others require a restart.`;
}
