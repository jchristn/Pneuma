# Pneuma REST API

Base URL (local): `http://127.0.0.1:8080`. All JSON is camelCase; enums are strings. OpenAPI lives at `GET /openapi.json`.

## Authentication

Most routes require a bearer token: `Authorization: Bearer <token>`. Obtain one by logging in with email/password. Machine credentials (access key + secret) may authenticate directly with `x-access-key` + `x-secret-key`, and a system admin key may be sent as `x-api-key`.

| Header | Purpose |
|--------|---------|
| `Authorization: Bearer <token>` | Session token (preferred) |
| `x-token` | Alternate carrier for the session token |
| `x-api-key` | System administrator API key |
| `x-access-key` / `x-secret-key` | Credential (API key) authentication |
| `x-email` / `x-password` / `x-tenant-guid` | Header login (session-creation only) |

Errors use `{ "error": "<code>", "message": "<text>", "context": <optional> }`. Status codes: 200 OK, 201 Created, 204 No Content, 400 Bad Request, 401 Unauthorized, 403 Forbidden, 404 Not Found, 409 Conflict, 429 Too Many Requests, 500 Internal Server Error.

## Pagination (list endpoints)

Every collection `GET` (tenants, users, credentials, roles, permissions, assignments, audit, subjects, links, jobs, model-runners, prompts) returns a paginated **`EnumerationResult`** envelope rather than a bare array. Paging is controlled with query-string parameters:

| Query param | Default | Description |
|-------------|---------|-------------|
| `maxResults` | `100` | Page size, clamped to `1..1000` |
| `skip` | `0` | Records to skip from the start of the ordered set |
| `order` | `desc` | `asc` or `desc` by creation time (`CreatedAscending`/`CreatedDescending`) |
| `search` | — | Case-insensitive substring filter over the entity's primary label (name/email/url/etc.) |

Response envelope:

```json
{
  "success": true,
  "maxResults": 100,
  "skip": 0,
  "totalRecords": 42,
  "recordsRemaining": 0,
  "endOfResults": true,
  "objects": [ /* the page of records */ ]
}
```

`totalRecords` is the count after filtering (before paging), so a client can read a total cheaply with `?maxResults=1`. `recordsRemaining` and `endOfResults` describe whether more pages follow.

## System

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/` | none | Server banner |
| HEAD | `/` | none | Liveness |
| GET | `/v1.0/api/health` | none | Health check → `{ status, serviceName, version, timeUtc }` |
| GET | `/openapi.json` | none | OpenAPI 3 document |

## Tokens

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/v1.0/token` | none | Create a session token. Body `{ email, password, tenantId? }` → `{ token, expiresUtc, principalType, tenantId, userId, displayName, email, isAdmin, isTenantAdmin }` |
| GET | `/v1.0/token` | bearer | Validate the current token; returns principal summary |
| GET | `/v1.0/token/details` | bearer | Decoded authentication context |
| DELETE | `/v1.0/token` | bearer | Revoke the current session (logout), 204 |

## Tenants (admin)

`GET /v1.0/tenants` · `POST /v1.0/tenants` · `GET /v1.0/tenants/{id}` · `PUT /v1.0/tenants/{id}` · `DELETE /v1.0/tenants/{id}`. Requires admin. Creating a tenant cascades to provision its first administrator + API key **and** its subordinate-service resources — a RecallDB tenant (same id) with a default collection, and an **isolated LiteGraph tenant + graph** (its GUIDs are recorded on the tenant as `liteGraphTenantGuid`/`liteGraphGraphGuid`). Each tenant's knowledge graph lives in its own LiteGraph tenant, so graphs never cross tenant boundaries. Provisioning is best-effort — an unavailable subordinate service never blocks tenant creation and is retried on next boot.

## Users

`GET /v1.0/users` (tenant-scoped; admin may pass `?tenantId=`) · `POST /v1.0/users` (body `CreateUserRequest { tenantId?, firstName, lastName, email, password, isAdmin, isTenantAdmin }`) · `GET|PUT|DELETE /v1.0/users/{id}`. Passwords are never returned.

## Credentials

`GET /v1.0/credentials` · `POST /v1.0/credentials` (body `{ name, userId?, expiresUtc? }`; the raw `secretKey` is returned **once**) · `GET /v1.0/credentials/{id}` · `DELETE /v1.0/credentials/{id}`.

## Roles, Permissions, Assignments (RBAC, admin)

- Roles: `GET|POST /v1.0/roles`, `GET|PUT|DELETE /v1.0/roles/{id}` (built-in roles are protected).
- Permissions: `GET|POST /v1.0/permissions`, `GET|PUT|DELETE /v1.0/permissions/{id}`.
- Assignments: `GET /v1.0/assignments?userId=<id>`, `POST /v1.0/assignments`, `DELETE /v1.0/assignments/{id}`.

## Audit

`GET /v1.0/audit` — recent security events (denials, bypasses, session/RBAC changes), tenant-scoped; admin may pass `?tenantId=`. Returns a paginated `EnumerationResult` (see [Pagination](#pagination-list-endpoints)).

## Request History

| Method | Path | Description |
|--------|------|-------------|
| GET | `/v1.0/api/request-history` | Paginated list; filters `method, statusCode, pathContains, fromUtc, toUtc, pageNumber, pageSize` (admin `tenantId, userId`). Bodies omitted. |
| GET | `/v1.0/api/request-history/summary` | Time-bucketed counts; `fromUtc, toUtc, bucketMinutes` |
| GET | `/v1.0/api/request-history/{id}` | Full entry including headers and bodies |
| DELETE | `/v1.0/api/request-history/{id}` | Delete one, 204 |
| DELETE | `/v1.0/api/request-history` | Bulk delete matching a filter → `{ deletedCount }` |

## Subjects

`GET|POST /v1.0/subjects` · `GET|PUT|DELETE /v1.0/subjects/{id}` · `GET /v1.0/subjects/by-slug/{slug}`. A subject has `displayName`, `type` (Person…), `description`, `tagline`, `graphRootNodeId`, plus: `urlSlug` (unique per tenant; auto-generated from the name when omitted — an explicit clash returns **409**), `thinkingEnabled` (show model reasoning in this subject's chats), `systemPrompt` (appended after the global system prompt for its chats), `ontologyClassifyPrompt` / `ontologyDefinitionPrompt` (appended after the global ontology prompts during ingestion), `historyRetentionDays` (chat-history retention, clamped ≥ 1), and a read-only `deletionStatus` (`None`/`Pending`/`Deleting`/`Failed`).

**A subject owns its models and collection** (moved off link submission): `embeddingModel` (Partio embedding endpoint id) and `inferenceModel` (Partio completion endpoint id) are **required** before links can be ingested to it or questions answered about it; `collection` (RecallDB collection id, dimensionality must match the embedding model) is **required** to ingest. `rerankingModel` and `promptRewriteModel` (Partio completion endpoint ids) are **optional** — when set, the answer pipeline re-ranks retrieved passages / rewrites the question before searching; when null those steps are skipped. `rerankingPrompt` / `promptRewritePrompt` are subject-level overrides appended after the global `reranking` / `prompt.rewrite` prompts (like `systemPrompt`), with sensible defaults applied on create. `tagline` is the subtitle shown beneath the subject's name on its ask page (defaults to the built-in label when omitted). `GET /v1.0/subjects/by-slug/{slug}` resolves a subject by its slug (tenant-scoped).

**`DELETE` is asynchronous.** It marks the subject for deletion and returns **`202 Accepted`** immediately; a background worker then runs the full cascade — all of the subject's links (jobs, per-step processing logs, S3 pipeline artifacts + raw blobs, RecallDB chunk documents), the subject's entire LiteGraph subgraph (matched on the `subjectId` tag), and the subject's chat history + feedback — before removing the subject row. The subject shows `deletionStatus = Deleting` until it is gone; an interrupted deletion resumes on the next server start. External-store cleanup is best-effort so an unavailable subordinate service never blocks removal. When `graphRootNodeId` is omitted on create it is derived from `displayName` as a slug (lowercased, non-alphanumeric runs collapsed to single dashes) — e.g. `"The Bomb Squad"` → `the-bomb-squad`.

## Chat History & Feedback

| Method | Path | Description |
|---|---|---|
| GET | `/v1.0/history` | List persisted chat turns (newest first), optionally `?subjectId=`. Paginated `EnumerationResult`. Each turn carries the question, answer, `thinking`, `model`, token counts, timing (`timeToFirstTokenMs`, `generationMs`, `thinkingMs`), `contextSize`, `citationsJson`, and structured per-stage telemetry in `performanceJson` (a serialized `{ schemaVersion, wallTimeMs, stages[] }`; each stage has `name`, `kind`, `provider`, `model`, `durationMs`, `timeToFirstTokenMs`, `promptTokens`, `completionTokens`, `success`) |
| GET | `/v1.0/history/{id}` | A single turn with its feedback and tool-call trace: `{ turn, feedback: [...], toolCalls: [...] }`. The turn's `performanceJson` drives the dashboard History detail stage table and timing bars; `toolCalls` (tool, args, output, success, duration) drives the Tool activity table |
| GET | `/v1.0/history?threadId=` | The history list also accepts `threadId` to return only a thread's turns |
| GET | `/v1.0/threads` | List conversation threads (most-recently-active first), optionally `?subjectId=`. Paginated `EnumerationResult` |
| POST | `/v1.0/threads` | Create a thread. Body `{ subjectId?, title? }` → the created thread |
| GET | `/v1.0/threads/{id}` | A thread with its turns: `{ thread, turns: [...] }` |
| PUT | `/v1.0/threads/{id}` | Rename a thread. Body `{ title }` |
| DELETE | `/v1.0/threads/{id}` | Delete a thread and cascade its turns + tool calls. Returns 204 |
| GET | `/v1.0/eval/facts` | List a subject's ground-truth facts (`?subjectId=` required) → `{ objects: [...] }` |
| POST | `/v1.0/eval/facts` | Create a fact. Body `{ subjectId, question, expectedAnswer, category? }` |
| DELETE | `/v1.0/eval/facts/{id}` | Delete a fact. Returns 204 |
| GET | `/v1.0/eval/runs` | List evaluation runs, optionally `?subjectId=` → `{ objects: [...] }` |
| POST | `/v1.0/eval/runs` | Start a run. Body `{ subjectId, category? }` — answers each fact through the pipeline and LLM-judges it, returning the completed run `{ status, totalFacts, passCount, partialCount, failCount, ... }` (synchronous) |
| GET | `/v1.0/eval/runs/{id}` | A run with its results: `{ run, results: [{ question, expectedAnswer, producedAnswer, verdict, score, reason, failureMode, category }] }` |
| DELETE | `/v1.0/eval/runs/{id}` | Delete a run and its results. Returns 204 |
| GET | `/v1.0/analytics` | Per-subject chat analytics over a window. Query `subjectId?` + `days?` (default 30, clamped 1–365) → `{ overview: { turnCount, avgGenerationMs, p50/p95/p99GenerationMs, avgTimeToFirstTokenMs, avgPromptTokens, avgCompletionTokens, avgTokensPerSecond, thumbsUp, thumbsDown }, timeseries: [{ bucketUtc, count, avgGenerationMs }], stages: [{ stage, count, avgDurationMs, p95DurationMs }] }` |
| GET | `/v1.0/feedback` | List feedback (newest first), optionally `?subjectId=`. Each item is `{ feedback, turn }` (the rated turn is enriched inline). Paginated `EnumerationResult` |
| POST | `/v1.0/feedback` | Submit feedback on a turn: `{ turnId, rating: "Up"\|"Down"\|"None", comment? }`. Requires a rating and/or comment; unknown `turnId` → 404 |

Chat turns are persisted automatically as chats complete (agentic `POST /v1.0/chat/stream`), and each answering turn's `complete` SSE event now carries a `turnId` for feedback. History is pruned per subject by its `historyRetentionDays`.

## Content Links & Ingestion

| Method | Path | Description |
|--------|------|-------------|
| POST | `/v1.0/subjects/{subjectId}/links` | Submit a link `{ url, title? }` → creates the link and **enqueues an ingestion job**. The embedding/inference models and collection are taken from the **subject** (configure them on the subject first); a subject missing any returns **400** |
| POST | `/v1.0/subjects/{subjectId}/links/bulk` | Submit many links at once `{ urls: [string] }` → `{ created, links: [...] }` (one link + job per URL). Models and collection come from the subject |
| GET | `/v1.0/ingestion/endpoints` | Available model endpoints for ingestion → `{ embedding: [{ id, name, model, apiFormat, active }], completion: [...] }` (sourced from Partio) |
| GET | `/v1.0/subjects/{subjectId}/links` | List a subject's links (with `status`, `lastIngestedUtc`, `lastError`). Paginated `EnumerationResult` |
| GET | `/v1.0/links` / `GET /v1.0/links/{id}` | List / read links |
| DELETE | `/v1.0/links/{id}` | Delete a link and **cascade** through everything it produced: ingestion jobs + per-step processing logs, pipeline document artifacts (source/atoms/chunks/vectors/subgraph + raw blobs), the chunk documents (RecallDB), and the graph nodes/edges it asserted (LiteGraph). Shared entity nodes reused by other links are preserved. Returns 204; external-store cleanup is best-effort so an unavailable subordinate service never blocks removal of the link. |
| GET | `/v1.0/links/{id}/log` | **Per-step ingestion log** for a link → `[{ job, events }]` (one entry per ingestion run, newest last). Each `event` is `{ stage, status, message, durationMs, createdUtc }`. |
| GET | `/v1.0/links/{id}/source` | **Pipeline artifact** — the raw crawled source document in its original content type (may be HTML/PDF/binary). `404` if not present yet. |
| GET | `/v1.0/links/{id}/atoms` | **Pipeline artifact** — DocumentAtom semantic cells as `application/json`. `404` if that stage hasn't run yet. |
| GET | `/v1.0/links/{id}/chunks` | **Pipeline artifact** — Partio chunks as `application/json`. `404` if that stage hasn't run yet. |
| GET | `/v1.0/links/{id}/vectors` | **Pipeline artifact** — Partio embeddings as `application/json`. `404` if that stage hasn't run yet. |
| GET | `/v1.0/links/{id}/subgraph` | **Pipeline artifact** — candidate subgraph as `application/json`. `404` if that stage hasn't run yet. |
| GET | `/v1.0/jobs?status=` | List ingestion jobs (optional status filter). Paginated `EnumerationResult` |
| GET | `/v1.0/jobs/summary` | Time-bucketed ingestion activity, broken down by pipeline stage → `IngestionActivitySummary` `{ totalCount, totals: [{ stage, count }], buckets: [{ bucketStartUtc, bucketEndUtc, totalCount, stages: [{ stage, count }] }] }`. Query: `fromUtc`, `toUtc`, `bucketMinutes` (1–1440, default 15), `subjectId` (optional). Tenant-scoped. |
| GET | `/v1.0/jobs/{id}` | Job detail with per-stage events → `{ job, events }` |
| GET | `/v1.0/jobs/{id}/log` | Live per-stage log for a job → `{ job, events }` (poll for a "follow logs" view) |
| POST | `/v1.0/jobs/{id}/restart` | Requeue a failed job |
| POST | `/v1.0/jobs/{id}/stop` | Stop (cancel) a queued or in-flight job → job set to `Cancelled`. 409 if already finished |
| DELETE | `/v1.0/jobs/{id}` | Delete an ingestion job (queue/job entry) and **cascade** its per-step processing log, the graph nodes/edges it asserted (LiteGraph), its chunk documents (RecallDB, deleted by `jobId` tag), and its raw blob. The link and its per-link S3 pipeline artifacts are left intact (they belong to the link). Returns 204; external-store cleanup is best-effort. |

Ingestion stages, each written to the log with a descriptive message and duration: **TypeDetection → CellExtraction → Classification (ontology mapping) → GraphMerge (knowledge-graph insertion) → Embedding (chunking + embedding generation) → Indexing (search index)**. Unknown document types fail at TypeDetection. Each stage records a completion log entry with counts (e.g. cells extracted, chunks produced, embeddings generated, nodes/edges inserted, documents indexed); failures record the stage and error.

## Model Runners (admin)

`GET|POST /v1.0/model-runners` · `GET|PUT|DELETE /v1.0/model-runners/{id}`. This is a **pass-through proxy to Partio's model endpoints** — Pneuma stores no local model state. Each item is a Partio embedding or completion endpoint: `{ id, type (Embedding|Completion), name, model, endpoint, apiFormat, active, maxConcurrentRequests, contextSize }`. Create/update body `{ type (Embedding|Completion), name?, model, endpoint, apiFormat?, apiKey?, active, maxConcurrentRequests?, contextSize? }` (the `apiKey` is forwarded to Partio and never returned; `maxConcurrentRequests` caps concurrent connections Partio opens to the endpoint — minimum 1, default 2; `contextSize` is the completion model's context window in tokens — Partio has no discrete field so it round-trips via the endpoint's `contextSize` tag, and it drives automatic chat conversation compaction; 0 disables). Deletes resolve the endpoint type automatically.

### Model endpoint health

A background monitor probes each model endpoint's **base URL**, deduplicated so a host shared by several endpoints is checked once and the result is shared. Healthy/unhealthy transitions use a two-consecutive-check hysteresis; uptime, a rolling 24-hour history, latency, and the last HTTP status code are accumulated in memory.

| Method | Path | Description |
|--------|------|-------------|
| GET | `/v1.0/model-runners/health` | Health of **every** model endpoint (deduplicated by base URL). Array of `ModelEndpointHealth`. |
| GET | `/v1.0/model-runners/{id}/health` | Health of a single model endpoint. `404` if the endpoint is not found. |

`ModelEndpointHealth`: `{ endpointId, endpointName, type, baseUrl, isHealthy, statusCode?, latencyMs?, firstCheckUtc?, lastCheckUtc?, lastHealthyUtc?, lastUnhealthyUtc?, lastStateChangeUtc?, totalUptimeMs, totalDowntimeMs, uptimePercentage, consecutiveSuccesses, consecutiveFailures, lastError?, history: [{ timestampUtc, success }] }`. Before a base URL's first probe, timestamps are null (a "pending" state).

## Prompts (admin)

`GET|POST /v1.0/prompts` · `GET|PUT|DELETE /v1.0/prompts/{id}`. Keyed prompts: `ontology.classify`, `cell.summarize`, `user.answer`.

## Settings (admin)

Server configuration, restricted to the **system administrator** (`isAdmin`). Non-admins receive 403.

| Method | Path | Description |
|--------|------|-------------|
| GET | `/v1.0/settings` | Read the current settings. Secrets are masked as `********`. Returns `{ success, settings, meta }` |
| PUT | `/v1.0/settings` | Overwrite the settings file with the supplied `AppSettings` body → `{ success, restartRequired, message, meta }` |

`meta` describes the form: `sections[] { key, label, requiresRestart }` annotates which top-level sections need a server restart, `secretFields[]` lists the masked dot-paths, and `secretMask` is the sentinel. **Submitting a secret field unchanged (still equal to the mask) preserves the stored secret**; supplying a new value replaces it. Most sections require a restart to take effect; `cors` and `requestHistory` are applied live.

## Knowledge Graph (user)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/v1.0/graph/nodes/{id}` | Node contents |
| GET | `/v1.0/graph/nodes/{id}/neighbors` | Adjacent nodes |
| GET | `/v1.0/graph/nodes/{id}/edges` | Relationships (edges) for the node |

## Collections

Vector collections live in the retrieval store (RecallDB), which is the **authority** for tenants and collections; Pneuma simply relays administration to it (the same way model runners are proxied to Partio) and keeps no local collection state. Collections are **per-tenant**: each Pneuma tenant maps to a RecallDB tenant of the same id, and these endpoints operate within the caller's tenant. RecallDB assigns collection ids. A collection's `dimensionality` is **fixed at creation** and must match the embedding model used to ingest into it. Collections are create/read/list/delete (no update).

Provisioning is automatic: at first-boot and whenever a **tenant is created** (`POST /v1.0/tenants`), Pneuma provisions the tenant on RecallDB and creates a **default collection** for it, so ingestion has a target out of the box.

| Method | Path | Description |
|--------|------|-------------|
| GET | `/v1.0/collections` | List vector collections → paginated `EnumerationResult` of `{ id, name, description, dimensionality, active }` |
| GET | `/v1.0/collections/{id}` | Read one collection |
| PUT | `/v1.0/collections` | Create a collection `{ name, description?, dimensionality }` (dimensionality defaults to 768) → `201` with the created collection |
| DELETE | `/v1.0/collections/{id}` | Delete a collection **and all of its documents** → `204` |

## Search & Ask (user)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/v1.0/search?q=<query>&max=20&collection=<id>` | Full-text search (RecallDB) resolved to a representative set of graph nodes → `{ query, results: [{ node, score, snippet }] }`. `collection` is optional — when omitted the configured default (or the tenant's first active) collection is used |
| GET | `/v1.0/subjects/{subjectId}/search?q=<query>&maxResults=20&skip=0&collection=<id>` | Search a single subject's ingested documents (RecallDB, filtered by `subjectId`). Returns a paginated `EnumerationResult` ranked by score; each hit is `{ documentId, score, snippet, linkId, linkUrl, linkTitle, nodeId }`, linked back to the originating content link. |
| POST | `/v1.0/query` | Grounded answer. Body `{ question, maxResults?, subjectId?, metadataFilter? }` → `{ answer, sources: [node], grounded }`. When `subjectId` is set, retrieval is restricted to that subject's documents. `metadataFilter` is an optional facet filter `{ required: [{ key, condition, value }], excluded: [...] }` over chunk tags (conditions: `Equals`, `NotEquals`, `Contains`, `StartsWith`, `EndsWith`, `GreaterThan`, `LessThan`, `IsNull`, `IsNotNull`); it is merged with the subject's default `retrievalFilterJson` (union of required and excluded), narrowing — never widening — the subject default. |
| POST | `/v1.0/query/stream` | Same as `/v1.0/query` (incl. optional `subjectId`) but streamed over server-sent events (`metadata` / `delta` / `complete` / `error`). |
| POST | `/v1.0/chat/stream` | Multi-turn agentic chat over the corpus (the assistant may call read tools), streamed over SSE. Body `{ messages: [{ role, content }], maxResults?, subjectId? }`. When `subjectId` is set, the search and grounded-answer tools are restricted to that subject. SSE event types: `delta`, `tool_call`, `tool_result`, `compacting` (the running history is being summarized to fit the completion model's `contextSize`), `complete`, `error`. The `complete` event carries `{ answer, model, …token telemetry…, toolCalls, citations: [{ linkId, url, title, score }], compacted, compactedSummary }`; when `compacted` is true the client should replace its prior message history with `compactedSummary`. |

**Model-runner concurrency gate.** The grounded-answer and chat endpoints (`/v1.0/query`, `/v1.0/query/stream`, `/v1.0/chat/stream`, and the MCP `pneuma_query` tool) are admitted through a process-wide gate that caps concurrent model-runner usage. Requests beyond the cap queue up to a bounded depth; when the queue is full the request is rejected with **HTTP 429 Too Many Requests** (`{ "error": "TooManyRequests", ... }`) before any streaming begins — callers should back off and retry. Configured via the `ModelRunner` settings block (`MaxConcurrentRequests`, default 4; `MaxQueueDepth`, default 16).
