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

`GET|POST /v1.0/subjects` · `GET|PUT|DELETE /v1.0/subjects/{id}` · `GET /v1.0/subjects/by-slug/{slug}`. A subject has `displayName`, `type` (Person…), `description`, `tagline`, `graphRootNodeId`, plus: `urlSlug` (unique per tenant; auto-generated from the name when omitted - an explicit clash returns **409**), `thinkingEnabled` (show model reasoning in this subject's chats), `systemPrompt` (appended after the global system prompt for its chats), `ontologyClassifyPrompt` / `ontologyDefinitionPrompt` (appended after the global ontology prompts during ingestion), `historyRetentionDays` (chat-history retention, clamped ≥ 1), `classificationTemperature` (sampling temperature for ingestion classification, 0 to 2, default 0), `classificationCacheEnabled` (reuse stored classification results for identical requests, default true), a read-only `ontologyVersionId` (the pinned ontology version, set with [`PUT /v1.0/subjects/{id}/ontology`](#ontologies) and ignored on create and update), `defaultRefreshIntervalMinutes` (how often links that follow the subject default are re-checked for changes: 0 for off, the default, or 60 to 525600; other values are **400**; changing it reschedules those links), and a read-only `deletionStatus` (`None`/`Pending`/`Deleting`/`Failed`).

**A subject owns its models and collection** (moved off link submission): `embeddingModel` (embedding model-endpoint id) and `inferenceModel` (completion model-endpoint id) are **required** before links can be ingested to it or questions answered about it; `collection` (RecallDB collection id, dimensionality must match the embedding model) is **required** to ingest. `rerankingModel` and `promptRewriteModel` (completion model-endpoint ids) are **optional** — when set, the answer pipeline re-ranks retrieved passages / rewrites the question before searching; when null those steps are skipped. `rerankingPrompt` / `promptRewritePrompt` are subject-level overrides appended after the global `reranking` / `prompt.rewrite` prompts (like `systemPrompt`), with sensible defaults applied on create. `tagline` is the subtitle shown beneath the subject's name on its ask page (defaults to the built-in label when omitted). `GET /v1.0/subjects/by-slug/{slug}` resolves a subject by its slug (tenant-scoped).

**Chunking.** `chunkStrategy` (`FixedTokenCount` default, `SentenceBased`, `ParagraphBased`, `Recursive`),
`chunkMaxTokens` (16 to 8192, default 256), and `chunkOverlapTokens` (0 to 4096, default 32) control how cells are split.
Tokens are counted in the subject's embedding model's own tokenizer (WordPiece for BERT-family models such as
nomic-embed-text, cl100k_base otherwise), and chunks are sized to fit the model's input limit less a 1% margin (at least 2
tokens); the runner's `maxInputTokens` overrides the known limit. If the model still rejects a chunk as too long, that
chunk is re-chunked at 75%, 50%, and then 30% of the size; one rejected even at 30% is left out with a warning.
`chunkHeaders` (`None`, `Title`, `TitleAndHeadings`) embeds the document title (the link title, else the first
top-level heading) and, for `TitleAndHeadings`, the section's heading path in front of each chunk; the stored and
returned chunk text is unchanged and the header's tokens come out of the chunk budget. New subjects default to
`TitleAndHeadings`; subjects created before the setting existed keep `None` until changed. Changes apply to new ingests.

**Per-subject ingestion concurrency.** A subject may carry an optional `concurrencyOverrides` object with the same integer keys as the system-wide `IngestionTuning` (see [`/v1.0/settings/ingestion`](#settings-admin)) — `contentRetrieval`, `typeDetection`, `cellExtraction`, `classification`, `graphMerge`, `summarization`, `chunking`, `embedding`, `indexing`, `maxConcurrentTasks`, `summarizationConcurrency`, `summarizationMinCellLength`, `classificationBatchSize`, `classificationBatchOverlap`, `classificationBatchConcurrency`, `stageTimeoutSeconds`. **Each field is nullable**: a set value overrides the system default for this subject's ingestion, while `null`/absent inherits the system default (effective value = override ?? system default). Accepted on create and update and applied live to subsequent ingestion work.

**`DELETE` is asynchronous.** It marks the subject for deletion and returns **`202 Accepted`** immediately; a background worker then runs the full cascade — all of the subject's links (jobs, per-step processing logs, S3 pipeline artifacts + raw blobs, RecallDB chunk documents), the subject's entire LiteGraph subgraph (matched on the `subjectId` tag), and the subject's chat history + feedback — before removing the subject row. The subject shows `deletionStatus = Deleting` until it is gone; an interrupted deletion resumes on the next server start. External-store cleanup is best-effort so an unavailable subordinate service never blocks removal. When `graphRootNodeId` is omitted on create it is derived from `displayName` as a slug (lowercased, non-alphanumeric runs collapsed to single dashes) — e.g. `"The Example Project"` → `the-example-project`.

## Subject Prompts

Each subject can **override any keyed prompt** on top of the global default, choosing how the override merges with the global content. When a subject has no override for a key, the global prompt applies (fallback).

| Method | Path | Description |
|--------|------|-------------|
| GET | `/v1.0/subjects/{id}/prompts` | List the subject's effective prompts → array of `{ key, name, effectiveContent, globalContent, overrideContent｜null, source ("Global"｜"SubjectOverride"), mergeMode ("Append"｜"Replace") }`. `effectiveContent` is what the pipeline actually uses: the `globalContent` alone when `source` is `Global`, or the global merged with the override per `mergeMode` when `source` is `SubjectOverride`. |
| PUT | `/v1.0/subjects/{id}/prompts/{key}` | Set (or clear) the subject's override for `key`. Body `{ content, mergeMode ("Append"｜"Replace") }` → the resulting `SubjectPrompt` DTO. `Append` layers the override after the global prompt; `Replace` uses the override alone. **Empty `content` clears the override** (the subject falls back to the global prompt). |
| DELETE | `/v1.0/subjects/{id}/prompts/{key}` | Remove the subject's override for `key` (falls back to the global prompt). Returns 204. |

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
| POST | `/v1.0/eval/facts/bulk` | Create up to 100 facts. Body `{ facts: [{ subjectId, question, expectedAnswer, category? }] }` → **201** `{ created, objects }`. An empty list or more than 100 is **400**; a subject not in the tenant is **404**. |
| DELETE | `/v1.0/eval/facts/{id}` | Delete a fact. Returns 204 |
| GET | `/v1.0/eval/runs` | List evaluation runs, optionally `?subjectId=` → `{ objects: [...] }` |
| POST | `/v1.0/eval/runs` | **Queue** a run. Body `{ subjectId, category? }` → returns the created run immediately with `status: "Pending"` (**non-blocking**). A background worker claims it, answers each fact through the real grounded pipeline, LLM-judges it (each fact admitted through the model-runner gate so eval yields to interactive traffic), and drives it to a terminal status (`Completed`/`Failed`/`Cancelled`). Status flow: `Pending → Running → {Completed｜Failed｜Cancelled}` |
| GET | `/v1.0/eval/runs/{id}/stream` | Live run progress over server-sent events: `{type:"metadata",runId,total,status}`, `{type:"result",factId,question,verdict,score,category,failureMode}` per judged fact, `{type:"progress",completed,total,pass,partial,fail,status}` each tick, and a final `{type:"complete",run}` (or `{type:"error",message}`) |
| POST | `/v1.0/eval/runs/{id}/cancel` | Cancel a queued or running run → 200 with the run (status becomes `Cancelled`; the worker observes it between facts and stops). Idempotent on an already-terminal run |
| GET | `/v1.0/eval/runs/{id}` | A run with its results: `{ run, results: [{ question, expectedAnswer, producedAnswer, verdict, score, reason, failureMode, category }] }` |
| DELETE | `/v1.0/eval/runs/{id}` | Delete a run and its results. Returns 204 |
| GET | `/v1.0/analytics` | Per-subject chat analytics over a window. Query `subjectId?` + `days?` (default 30, clamped 1–365) → `{ overview: { turnCount, avgGenerationMs, p50/p95/p99GenerationMs, avgTimeToFirstTokenMs, avgPromptTokens, avgCompletionTokens, avgTokensPerSecond, thumbsUp, thumbsDown }, timeseries: [{ bucketUtc, count, avgGenerationMs }], stages: [{ stage, count, avgDurationMs, p95DurationMs }] }` |
| GET | `/v1.0/feedback` | List feedback (newest first), optionally `?subjectId=`. Each item is `{ feedback, turn }` (the rated turn is enriched inline). Paginated `EnumerationResult` |
| POST | `/v1.0/feedback` | Submit feedback on a turn: `{ turnId, rating: "Up"\|"Down"\|"None", comment? }`. Requires a rating and/or comment; unknown `turnId` → 404 |

Chat turns are persisted automatically as chats complete (agentic `POST /v1.0/chat/stream`), and each answering turn's `complete` SSE event now carries a `turnId` for feedback. History is pruned per subject by its `historyRetentionDays`.

## Content Links & Ingestion

| Method | Path | Description |
|--------|------|-------------|
| POST | `/v1.0/subjects/{subjectId}/links` | Submit a link `{ url, title?, labels?: [string], tags?: { key: value }, refreshIntervalMinutes? }` (see "Scheduled link refresh" below; omit it to follow the subject's default) → creates the link and **enqueues an ingestion job**. The embedding/inference models and collection are taken from the **subject** (configure them on the subject first); a subject missing any returns **400**. Any `labels`/`tags` are attached to every chunk this link produces (RecallDB) and to the link's source graph node (LiteGraph) so retrieval can later be scoped to them via a query/chat `metadataFilter` (see **Search & Ask**). The URL must be absolute `http` or `https`; a private IP literal or `localhost` (unless in `Ingestion.FetchSafety.AllowedPrivateHosts`) returns **400** and writes a `FetchBlocked` audit record. A host name that resolves to a private address fails its job with category `Blocked` |
| POST | `/v1.0/subjects/{subjectId}/links/bulk` | Submit many links at once `{ urls: [string], labels?: [string], tags?: { key: value } }` → `{ created, links: [...] }` (one link + job per URL). Models and collection come from the subject; any `labels`/`tags` are applied identically to every URL in the batch. Every URL is checked as for a single submission, and one unsafe URL refuses the whole batch with **400** |
| POST | `/v1.0/subjects/{subjectId}/content` | **Push content** `{ content, contentType, title?, externalKey?, labels?, tags? }` → **201** `ContentSubmitResult` `{ index, statusCode, replaced, link, jobId }`. `contentType` is `text/plain`, `text/markdown`, `text/html`, or `application/json` and replaces type detection. The content is stored in the blob store and ingested like a link's (the link has `sourceKind: Inline` and URL `pneuma-inline://{linkId}`). With an `externalKey` already used in the subject, the content is **replaced** (**200**, `replaced: true`, a new job; the previous version is retired once the new one is indexed). Limits: `Ingestion.MaxInlineContentBytes` (10 MB, **413** over it), `externalKey` at most 256 characters. Text is cleaned (lone surrogates become U+FFFD; control characters other than tab and line breaks are removed). **400** for empty content or an unsupported type, **404** for an unknown subject, **409** when the keyed content is being deleted. Subject models and collection are required, as for links. |
| POST | `/v1.0/subjects/{subjectId}/content/batch` | Push up to 100 items `{ items: [ ...content bodies ] }` → **200** `{ accepted, rejected, results: [ContentSubmitResult] }`. Every item is attempted; an invalid item is reported with its `index`, `statusCode`, and `error` and never stops the rest. More than 100 items is **400**. |
| GET | `/v1.0/ingestion/endpoints` | Available model endpoints for ingestion → `{ embedding: [{ id, name, model, apiFormat, active }], completion: [...] }` (from Pneuma's native model-endpoint store) |
| GET | `/v1.0/subjects/{subjectId}/links` | List a subject's links (with `status`, `lastIngestedUtc`, `lastError`). Paginated `EnumerationResult` |
| GET | `/v1.0/links` / `GET /v1.0/links/{id}` | List / read links |
| DELETE | `/v1.0/links/{id}` | Delete a link and **cascade** through everything it produced: ingestion jobs + per-step processing logs, pipeline document artifacts (source/atoms/chunks/vectors/subgraph + raw blobs), the chunk documents (RecallDB), and the graph nodes/edges it asserted (LiteGraph). Shared entity nodes reused by other links are preserved. Returns 204; external-store cleanup is best-effort so an unavailable subordinate service never blocks removal of the link. |
| POST | `/v1.0/links/{id}/reingest` | **Reingest a link** — queues a **fresh** ingestion job that forces a full re-run. The link's stored content hash is cleared first so the delta-skip can't short-circuit the pipeline, then a brand-new job is enqueued (works even when the link has no prior job). Returns **202 Accepted** with the created job. The previous version stays searchable while the new job runs; once the new job has indexed its content, the earlier jobs' chunks and Source and Cell graph nodes are removed (shared entity nodes are kept) and the link's `currentJobId` moves to the new job. If the new job fails, the previous version stays in place. |
| POST | `/v1.0/links/reingest` | **Bulk reingest** — body `{ ids: [string] }`. Queues a fresh ingestion job per link (each clears the link's content hash to force a full re-run). Returns **202 Accepted** with `{ queued, skipped }` (`skipped` counts ids that could not be resolved to a link). |
| PUT | `/v1.0/links/{id}` | **Set a link's refresh schedule.** Body `{ refreshIntervalMinutes }` (0 turns it off, otherwise 60 to 525600) or `{ useSubjectDefault: true }`. Returns the updated link, whose `nextRefreshUtc` is the next check (null when off). **400** for an invalid interval, pushed content, a link a crawl plan manages, or a link being deleted; **404** for an unknown link. Needs Subject Write. |
| POST | `/v1.0/links/refresh-interval` | **Set the refresh schedule of several links.** Body `{ ids: [string], refreshIntervalMinutes? , useSubjectDefault? }` → `{ updated, skipped: [ids] }` (`skipped` lists ids that are unknown or cannot be refreshed). |
| POST | `/v1.0/links/{id}/refresh` | **Check a link for changes now.** A conditional GET (`If-None-Match` / `If-Modified-Since` from the last check) → `{ linkId, outcome, jobId?, message?, nextRefreshUtc? }`. `outcome` is `Unchanged` (304, or the same validators), `Queued` (changed; a re-ingest job with `trigger` `Refresh` was queued), `Busy` (an ingestion is already pending), or `Failed` (the current version is kept and the check backs off). Same 400/404 rules as above. |
| GET | `/v1.0/links/{id}/log` | **Per-step ingestion log** for a link → `[{ job, events }]` (one entry per ingestion run, newest last). Each `event` is `{ stage, status, message, durationMs, createdUtc }`. |
| GET | `/v1.0/links/{id}/source` | **Pipeline artifact** — the raw crawled source document in its original content type (may be HTML/PDF/binary). `404` if not present yet. |
| GET | `/v1.0/links/{id}/atoms` | **Pipeline artifact** — DocumentAtom semantic cells as `application/json`. `404` if that stage hasn't run yet. |
| GET | `/v1.0/links/{id}/chunks` | **Pipeline artifact** — the link's chunks as `application/json`. `404` if that stage hasn't run yet. |
| GET | `/v1.0/links/{id}/vectors` | **Pipeline artifact** — the link's embeddings as `application/json`. `404` if that stage hasn't run yet. |
| GET | `/v1.0/links/{id}/subgraph` | **Pipeline artifact** — candidate subgraph as `application/json`. `404` if that stage hasn't run yet. |
| GET | `/v1.0/jobs?status=&failureCategory=&hasWarnings=` | List ingestion jobs. Optional filters: `status`; `failureCategory` (one of the categories below; an unknown value is 400); `hasWarnings` (`true` or `false`; anything else is 400). Paginated `EnumerationResult` |
| GET | `/v1.0/jobs/summary` | Time-bucketed ingestion activity, broken down by pipeline stage → `IngestionActivitySummary` `{ totalCount, totals: [{ stage, count }], buckets: [{ bucketStartUtc, bucketEndUtc, totalCount, stages: [{ stage, count }] }] }`. Query: `fromUtc`, `toUtc`, `bucketMinutes` (1–1440, default 15), `subjectId` (optional). Tenant-scoped. |
| GET | `/v1.0/jobs/live` | **Live pipeline snapshot** → `IngestionLiveSnapshot` `{ generatedUtc, running: [], waitingForSlot: [], queued: [] }`. Each entry is an `IngestionLiveJob` `{ jobId, subjectId, sourceUrl, stage｜null, stateSinceUtc }`: `running` = jobs executing a stage now, `waitingForSlot` = jobs whose current stage is waiting for a free per-stage concurrency slot, `queued` = jobs waiting in the pool to start (`stage` is null). Each list is ordered longest-in-state first. `stateSinceUtc` is absolute so a client can tick the elapsed "time in this state" between polls. Query: `subjectId` (optional). Tenant-scoped. |
| GET | `/v1.0/jobs/{id}` | Job detail → `{ job, events, attempts, remediation }`. `attempts` lists every attempt oldest first (`{ id, attemptNumber, succeeded, stage, failureCategory, message, startedUtc, endedUtc }`); `remediation` is the fix to try for the job's failure category, or null |
| GET | `/v1.0/jobs/{id}/log` | Live per-stage log for a job → same shape as the job detail (poll for a "follow logs" view) |
| POST | `/v1.0/jobs/{id}/restart` | Requeue a failed job |
| POST | `/v1.0/jobs/{id}/stop` | Stop (cancel) a queued or in-flight job → job set to `Cancelled`. 409 if already finished |
| DELETE | `/v1.0/jobs/{id}` | Delete an ingestion job (queue/job entry) and **cascade** its per-step processing log, the graph nodes/edges it asserted (LiteGraph), its chunk documents (RecallDB, deleted by `jobId` tag), and its raw blob. The link and its per-link S3 pipeline artifacts are left intact (they belong to the link). Returns 204; external-store cleanup is best-effort. |

### Scheduled link refresh

A URL link can be re-checked for changes on a schedule. A link's `refreshIntervalMinutes` is null (follow the
subject's `defaultRefreshIntervalMinutes`), 0 (off), or 60 to 525600. Each check is a conditional GET using the
`ETag` and `Last-Modified` from the previous check; an unchanged page costs one request and nothing is re-ingested.
A changed page is re-ingested with a job whose `trigger` is `Refresh`, and the previous version stays searchable until
the new one is indexed. A failed check keeps the current version and backs off (15 minutes, doubling, capped at the
interval); `refreshFailures` counts failures in a row. Links a crawl plan manages follow their plan's schedule, and
pushed content is replaced by pushing it again, so neither can be scheduled here. Links carry `refreshIntervalMinutes`,
`nextRefreshUtc`, `lastRefreshUtc`, `refreshFailures`, `sourceETag`, and `sourceLastModifiedUtc`; ingestion jobs
carry `trigger` (`Submit`, `Reingest`, `Refresh`, or `Crawl`). The server's `LinkRefresh` settings section sets
`Enabled` (default true), `IntervalSeconds` (60), and `BatchSize` (50).

**Ingestion labels & tags.** Operator-supplied `labels` (plain strings) and `tags` (key/value) on link submission are stamped onto every chunk the link produces and onto its source graph node. Each label `L` is stored as a distinct chunk tag `label:L` (so a chunk can carry several labels at once), and the detected document type is exposed the same way; tags are stored verbatim (reserved provenance keys — `litegraphNodeId`, `linkId`, `tenantId`, `subjectId`, `jobId`, `sourceUrl`, `documentType`, and any `label:*` — are never overwritten). A query/chat/search `metadataFilter` then scopes retrieval to them: a required label matches with an equals condition on its `label:L` tag; a required tag matches by key/value.

Ingestion stages, each written to the log with a descriptive message and duration, run in two phases. **Categorization:** ContentRetrieval → TypeDetection → CellExtraction → Classification (ontology mapping). **Hydration:** OntologyCanonicalization → GraphMerge (knowledge-graph insertion) → RelationshipConsolidation → Summarization → Chunking → Embedding → Indexing (search index). Content whose hash is unchanged since the last successful ingest completes right after ContentRetrieval. Unknown document types fail at TypeDetection. Each stage records a completion log entry with counts; failures record the stage, the error, and a failure category. The link log (`GET /v1.0/links/{id}/log`) returns one job detail per run in the same shape as `GET /v1.0/jobs/{id}`.

**Failure categories and retries.** A failed job carries `failureCategory`, and so does its link. The category decides whether the job is retried (up to `Ingestion.MaxAttempts`, with exponential backoff; `ModelUnavailable` backs off four times longer). Every attempt is recorded in the job's `attempts`.

| Category | Meaning | Retried |
|---|---|---|
| `Fetch` | The source returned an HTTP error, or the connection failed. A 4xx other than 408 and 429 is permanent. | Yes, except permanent 4xx |
| `Blocked` | The fetch-safety policy refused the URL (private address or disallowed scheme). | No |
| `TooLarge` | The content exceeded a size limit. | No |
| `UnsupportedType` | The content type is unknown or has no extractor. | No |
| `Extraction` | DocumentAtom failed. | Yes |
| `NoContent` | Extraction produced no text. | No |
| `ModelUnavailable` | A model endpoint stayed rate limited or unavailable after its own retries. | Yes, with a longer backoff |
| `ModelRejected` | A model endpoint rejected the request (a 4xx, including a context-length error). | No |
| `Configuration` | A required model, endpoint, or collection is missing. | No |
| `Storage` | RecallDB, LiteGraph, or the blob store failed. | Yes |
| `Timeout` | A stage or request timed out. | Yes |
| `PartialLoss` | Work was dropped and `Ingestion.PartialLossPolicy` is `Fail`. | Yes |
| `WorkerLost` | The worker stopped before the job finished. | Yes |
| `Cancelled` | An operator stopped the job. | No |
| `Internal` | Anything else. | Yes |

**One live version per link.** Each link records `currentJobId`, the job whose output search returns. When a new
job for the link finishes indexing (a re-ingest, a refresh, or a crawl update), the output of the link's earlier jobs
(their RecallDB chunks and their Source and Cell graph nodes) is removed; entity nodes are shared across links and are
kept. A job that completes early because the content is unchanged writes nothing and leaves `currentJobId` alone. A
retry within a job first removes what the failed attempt wrote, so retries never duplicate chunks or nodes. If removal
fails, the job still completes, records a warning, and the next successful ingest of the link removes the leftovers.

**Completeness and warnings.** Every job records what each stage received and produced in `completeness` (`cellsExtracted`, `classificationBatches`, `classificationBatchesFailed`, `cellNodesCreated`, `cellNodesFailed`, `summariesAttempted`, `summariesFailed`, `chunksProduced`, `chunksEmbedded`, `chunksIndexed`, `classificationCacheHits`, `ontologyViolations`, `taxonomyMatches`). Work a job drops but can complete without (a failed classification batch, a failed cell summary, a cell node that could not be created) is recorded in the job's `warnings` list, and the link's `warningCount` holds the count from its latest ingest. With `Ingestion.PartialLossPolicy` set to `Warn` (the default) such a job still ends `Completed`; with `Fail` it fails with category `PartialLoss` and is retried. A chunk that did not receive an embedding is never dropped: the attempt fails and is retried.

## Crawl plans

A crawl plan keeps a subject in sync with a web site, sitemap, GitHub repository, S3, Azure Blob, or Google Cloud Storage bucket, CIFS or NFS share, or server folder; each run is a crawl
operation. See `CRAWLING.md` for the model and the run lifecycle. Secret settings are write-only: they are stored
encrypted, never returned (`secretsSet` names them), redacted from request history, and audited by name when they
change. Permissions: `CrawlPlan` and `CrawlOperation` (Read for GETs, Write for create and replace, Delete for delete,
Execute for test, preview, start, stop, and confirming deletions).

| Method | Path | Description |
|--------|------|-------------|
| GET | `/v1.0/crawl-plan-types` | Supported types, each `{ type, displayName, description, settingsProperty, fields: [{ name, label, help, kind, required, min, max, options, default }] }`. `kind` is `string`, `integer`, `boolean`, `list`, `choice`, or `secret`. |
| POST | `/v1.0/subjects/{subjectId}/crawl-plans` | **Create** `CrawlPlanRequest` `{ name, type, web\|sitemap\|s3\|cifs\|nfs, filter?, schedule?, enabled?, processAdditions?, processUpdates?, processDeletions?, maxDeletionFraction?, retryFailedObjects?, labels?, tags?, operationRetentionDays? }` → **201** `CrawlPlan`. **400** for invalid settings (every problem listed), an invalid cron expression, an interval outside 5 to 525600 minutes, an unknown time zone, a type with no crawler, or a subject without models or a collection; **404** for an unknown subject. |
| GET | `/v1.0/subjects/{subjectId}/crawl-plans` | A subject's plans (paginated). |
| GET | `/v1.0/crawl-plans` | Plans (paginated; `subjectId=` filters). |
| GET | `/v1.0/crawl-plans/{id}` | One plan: settings (secrets null), `filter`, `schedule`, flags, `status` (`Idle`, `Running`, `Stopping`), `lastOperationId`, `lastRunUtc`, `lastSuccessUtc`, `nextRunUtc`, `secretsSet`. |
| PUT | `/v1.0/crawl-plans/{id}` | **Replace** the configuration (same body). A secret left out keeps its stored value; names in `clearSecrets` are removed. Changing `type` is **400**. |
| DELETE | `/v1.0/crawl-plans/{id}` | Delete the plan, its settings, secrets, objects, and operations → `{ deleted, linksDeleted, linksKept }`. `deleteLinks=true` deletes its links in the background; otherwise they are kept and detached. **409** while running. |
| POST | `/v1.0/crawl-plans/test` | Test a **draft** (same body) without saving anything → `ConnectivityResult` `{ success, layers: [{ name, success, message }] }`. `fromPlanId=` fills empty secrets from a stored plan. |
| POST | `/v1.0/crawl-plans/{id}/test` | Test a stored plan's connection step by step. |
| POST | `/v1.0/crawl-plans/{id}/preview` | What a run would do now → `CrawlPreview` `{ enumerated, bytesEnumerated, add, update, retry, unchanged, delete, missing, skip, deletionsHeld, truncated, items (at most 500) }`. Nothing is changed. **502** when the source cannot be listed. |
| POST | `/v1.0/crawl-plans/{id}/start` | Start an operation now → **202** `CrawlOperation`. **409** when the plan is already running (on any server). |
| POST | `/v1.0/crawl-plans/{id}/stop` | Stop the running operation: listing is cancelled; an operation waiting on ingestion has its unstarted jobs cancelled → **202**. **409** when idle. |
| GET | `/v1.0/crawl-plans/{id}/operations` | The plan's operations, newest first (paginated). |
| GET | `/v1.0/crawl-plans/{id}/objects` | Tracked objects `{ externalKey, linkId, versionToken, sizeBytes, contentType, status, lastError, firstSeenUtc, lastSeenUtc }` (paginated; `status=` `Active`, `Missing`, `Failed`, `Excluded`; **400** for another value). |

## Crawl operations

| Method | Path | Description |
|--------|------|-------------|
| GET | `/v1.0/crawl-operations` | Operations, newest first (paginated; `planId=` and `status=` filter; **400** for an unknown status). |
| GET | `/v1.0/crawl-operations/{id}` | One operation: `trigger` (`Schedule`, `Manual`), `status` (`Running`, `Ingesting`, `Succeeded`, `PartiallySucceeded`, `Failed`, `Cancelled`, `Held`), counts `enumerated`, `added`, `updated`, `retried`, `unchanged`, `deleted`, `missing`, `skipped`, `failed`, `bytesEnumerated`, `heldDeletions`, `error`, and timestamps. |
| GET | `/v1.0/crawl-operations/{id}/objects` | What the operation did with each object `{ externalKey, action, succeeded, linkId, jobId, detail }` (paginated; `action=` `Add`, `Update`, `Retry`, `Delete`, `Skip`, `Fail`). Unchanged objects are counted only. |
| POST | `/v1.0/crawl-operations/{id}/confirm-deletions` | Run the deletions a `Held` operation is waiting on → the operation. **409** when it is not `Held`. |

## Subject Wizard

The new subject wizard drafts a subject with a completion model, one step at a time, and creates it once the user has
reviewed and edited the draft. The draft lives with the caller: each drafting route receives the draft so far and returns
a proposal, and nothing is stored until commit. The wizard's own prompts are the seeded `wizard.*` prompts (a task and an
output format per step), overridable per tenant like any other prompt. All wizard routes need **Subject Create**.

| Method | Path | Description |
|--------|------|-------------|
| GET | `/v1.0/subject-wizard/options` | What the caller can do → `{ ontologyModes, defaultOntologyMode, defaultQuestionCount, maxQuestions, coverageMaxQuestions, groundingUrlEnabled, hasCompletionModel }`. `ontologyModes` holds `Prompt` always, `Draft` with Ontology Write, and `Approve` with Ontology Execute as well. |
| POST | `/v1.0/subject-wizard/brief` | Draft the brief. Body `WizardGenerateRequest` `{ draft, modelRunnerId?, guidance? }` where `draft` is `{ description, groundingText?, groundingUrls?: [string], brief?, questions, ontology?, prompts? }` and `description` is required (`groundingUrl`, a single URL, is still accepted). Each reference URL is read through the fetch-safety policy; a page that cannot be read (for example a private address) is a warning, and the step fails with **400** only when none can be read or more than `MaxGroundingUrls` are given. The pages' text, each introduced by its URL, is returned as `groundingExcerpt` to keep as grounding text. → `WizardResult` `{ value: { displayName, type, description, tagline, audience, tone }, model, modelRunnerId, elapsedMs, warnings, groundingExcerpt }`. |
| POST | `/v1.0/subject-wizard/questions` | Draft example questions. `mode` `replace` (default) keeps questions that are `locked` or have `origin` `User` and replaces the rest up to `count` (default 12); `more` keeps all and adds `count` new ones. → `value: [{ question, kind, origin, locked }]`, kept questions first. `kind` is Fact, Relationship, Timeline, Comparison, Reasoning, or Overview. |
| POST | `/v1.0/subject-wizard/ontology` | Draft node and relationship types that answer the draft's questions. Locked types are kept verbatim; node names become PascalCase and relationship names UPPER_SNAKE_CASE; duplicates are dropped; endpoints that name an unknown node type are cleared with a warning; `questions` (1-based question numbers each type serves) are kept in range; a `Subject` node type is always present. **400** without questions. → `value: { nodeTypes: [{ name, description, questions, locked }], edgeTypes: [{ name, description, from, to, questions, locked }], guidance }`. |
| POST | `/v1.0/subject-wizard/prompts` | Draft the subject's additions to the answering (`systemPrompt`), classification (`classifyPrompt`), query rewriting (`rewritePrompt`), and reranking (`rerankingPrompt`) prompts. Keys listed in `draft.prompts.locked` are kept verbatim. |
| POST | `/v1.0/subject-wizard/sources` | Suggest where content for the subject usually lives → `value: [{ kind, title, detail }]` where `kind` is Links, Text, or a crawl plan type. |
| POST | `/v1.0/subject-wizard/{step}/stream` | The same five drafting routes (`brief`, `questions`, `ontology`, `prompts`, `sources`), streamed as server-sent events: `{ type: "progress", phase, attempt, characters, elapsedMs, message? }` while the step runs, then `{ type: "complete", result }` (the `WizardResult` above) or `{ type: "error", statusCode, message }`. `phase` is `reading` (reference pages), `waiting` (request sent), `writing` (`characters` of the reply received so far), `retrying` (the reply was unusable; `attempt` 2), or `checking` (parsing and validating). Each request's `modelRunnerId` picks the completion model for that step, so every step can use a different one. Streamed calls time out after `TimeoutSeconds` without new output, or three times that in all. |
| POST | `/v1.0/subject-wizard/render-ontology` | Clean up `draft.ontology` as above and render it as the classifier will see it → `{ ontology, rendered, warnings }`. No model call. |
| POST | `/v1.0/subject-wizard/commit` | Create the subject from a finished draft. Body `{ draft, inferenceModel?, embeddingModel?, collection?, ontologyMode?, publishedForChat? }`. Models default to the tenant's first active completion and embedding endpoints and the collection to the default collection. The subject gets the brief, the prompt additions (appended to the global prompts), the rendered ontology as its ontology definition prompt, and the draft's questions as starter questions. `ontologyMode` `Approve` (default) also creates a tenant ontology, approves its first version, and pins it to the subject; `Draft` creates the ontology and leaves the version for an approver; `Prompt` creates no ontology. A mode the caller may not use is lowered with a warning. Creation, approval, and pinning are audited. If a later step fails, what was created is removed. → **201** `{ subject, questions, ontologyMode, ontologyId, ontologyVersionId, renderedOntology, warnings }`. |
| GET | `/v1.0/subjects/{id}/questions` | A subject's starter questions in order → `[{ id, question, kind, position, origin }]`. Needs Subject Read (the user dashboard shows up to four as suggestions on the ask page). |
| PUT | `/v1.0/subjects/{id}/questions` | Replace a subject's starter questions. Body `{ questions: [{ question, kind? }] }`; blank questions are skipped; at most 40. Needs Subject Update. |

Generation failures: **400** for a bad draft or no usable completion endpoint, **502** when the model's reply is not the
expected JSON after one retry, **504** when the model does not answer within `Wizard.TimeoutSeconds`. The `Wizard` settings
section sets `DefaultQuestionCount` (12), `MaxQuestions` (40), `MaxOntologyTypes` (60), `MaxGroundingUrls` (5), `MaxGroundingCharacters` (6000, shared between the pages),
`MaxPromptCharacters` (4000), `TimeoutSeconds` (300), and `CoverageMaxQuestions` (12, the most questions the dashboards'
coverage check asks).

## Ontologies

Governed, versioned ontologies: node types, edge types, constraint rules, and a taxonomy that subjects classify into.
See [`ONTOLOGY.md`](ONTOLOGY.md) for the model, scoping, and behavior. Ontologies are tenant-scoped. Permissions:
`Ontology` Read for GETs, Write for create, edit, propose, and import, Delete for deletes, and Execute for approve and
retire. Subject-facing routes use `Subject` Read (view, violations, operations, graph export) and Update (pin, release,
dismiss, start an operation, clear the cache). A failed ontology call returns the usual error body plus `problems`,
every problem found, when there is more than one thing to fix.

| Method | Path | Description |
|--------|------|-------------|
| GET | `/v1.0/ontology-templates` | Built-in templates `[{ name, description, nodeTypeCount, edgeTypeCount, ruleCount }]` (read-only, system content). |
| GET | `/v1.0/ontologies` | The tenant's ontologies (paginated). |
| POST | `/v1.0/ontologies` | Create `{ name, description?, template?, copyFromVersionId? }` → **201** `OntologyDetail` `{ ontology, versions, pinnedSubjects }`; the first version is a draft (empty, from the template, or a copy). **409** for a name already in use. |
| GET | `/v1.0/ontologies/{id}` | `OntologyDetail`: the ontology, its versions (newest first, with counts), and `pinnedSubjects` `[{ subjectId, displayName, ontologyVersionId }]`. |
| PUT | `/v1.0/ontologies/{id}` | Rename or re-describe `{ name, description }`. |
| DELETE | `/v1.0/ontologies/{id}` | Delete the ontology and its versions → **204**. **409** while a subject pins one of its versions. |
| GET | `/v1.0/ontologies/{id}/versions` | Versions, newest first (paginated). |
| POST | `/v1.0/ontologies/{id}/versions` | New draft `{ basedOnVersionId? }` copying that version (default the newest) → **201** `OntologyVersion`. |
| POST | `/v1.0/ontologies/{id}/propose` | Have a model propose a new draft `{ subjectId?, sampleText?, modelRunnerId?, sampleCells? (default 20, capped by `Ontology.MaxProposalSampleCells`), instructions?, language?, basedOnVersionId? }` → **201** the draft. Needs a subject or sample text. Uses the `ontology.propose` and `ontology.propose.format` prompts. **502** when the model's reply cannot be used. |
| GET | `/v1.0/ontology-versions/{id}` | `OntologyVersion` `{ id, ontologyId, versionNumber, status (Draft, Approved, Retired), guidance, undeclaredTypeAction (Allow, Warn, Drop, Quarantine), changeSummary, basedOnVersionId, createdByUserId, approvedByUserId, approvedUtc, retiredUtc, nodeTypes, edgeTypes, rules, concepts, problems }`. |
| PUT | `/v1.0/ontology-versions/{id}` | Replace a draft's contents (same shape) → the version with its `problems`. **409** for an approved or retired version; **400** for contents that cannot be stored (sizes, duplicate rule ids). |
| DELETE | `/v1.0/ontology-versions/{id}` | Delete a draft → **204**. **409** otherwise. |
| POST | `/v1.0/ontology-versions/{id}/approve` | Approve a draft `{ changeSummary? }` → the version. **400** with `problems` while the draft has any. Audited. |
| POST | `/v1.0/ontology-versions/{id}/retire` | Retire an approved version → the version. **409** while a subject pins it. Audited. |
| GET | `/v1.0/ontology-versions/{id}/diff` | Compare with `against=` (default the version it was copied from) → `{ fromVersionId, toVersionId, addedNodeTypes, removedNodeTypes, changedNodeTypes, addedEdgeTypes, removedEdgeTypes, changedEdgeTypes, addedRules, removedRules, addedConcepts, removedConcepts, changedConcepts, guidanceChanged, undeclaredTypeActionChanged, taxonomyChanged }`. |
| GET | `/v1.0/ontology-versions/{id}/definition` | `{ versionId, definition }`: the text the classifier sees for this version. |
| GET | `/v1.0/ontology-versions/{id}/export` | OWL and SKOS: `format=turtle` (default, `text/turtle`) or `jsonld` (`application/ld+json`); `baseIri=` (absolute, default `urn:pneuma:`). |
| POST | `/v1.0/ontology-versions/{id}/taxonomy/import` | Import a SKOS concept scheme into a draft. The body is the raw document (`Content-Type: text/turtle` or `application/ld+json`); `format=turtle\|jsonld`, `mode=merge\|replace` → `{ added, updated, removed, total, warnings, version }`. **400** for a document that cannot be read, **409** for a version that is not a draft, **413** over the size limit. Remote JSON-LD contexts are not fetched. |
| GET | `/v1.0/subjects/{id}/ontology` | `SubjectOntologyView` `{ subjectId, source (Version or Prompt), ontology, version, effectiveDefinition, classificationTemperature, classificationCacheEnabled, cacheEntries, quarantinedCount, conceptCount }`. |
| PUT | `/v1.0/subjects/{id}/ontology` | Pin `{ ontologyVersionId, retag? (default true) }` (an approved version) or unpin (`ontologyVersionId: null`) → the view. Queues a `Retag` operation when the taxonomy changes and `retag` is true. **400** for a version that is not approved. Audited. |
| GET | `/v1.0/subjects/{id}/ontology-violations` | Violations, newest first (paginated; `status=` `Recorded`, `Quarantined`, `Released`, `Dismissed`; `jobId=`; `operationId=`). Each `{ id, jobId, linkId, operationId, ontologyVersionId, ruleId, ruleType, elementKind (Node, Edge), nodeType, nodeName, edgeType, fromNodeType, fromNodeName, toNodeType, toNodeName, content, confidence, action, status, message, resolvedByUserId, resolvedUtc, createdUtc }`. |
| POST | `/v1.0/ontology-violations/{id}/release` | Add a quarantined element to the graph → the violation (`Released`). **409** when it is not quarantined. |
| POST | `/v1.0/ontology-violations/{id}/dismiss` | Discard a quarantined element → the violation (`Dismissed`). **409** when it is not quarantined. |
| GET | `/v1.0/subjects/{id}/ontology-operations` | Operations, newest first (paginated). |
| POST | `/v1.0/subjects/{id}/ontology-operations` | Queue `{ kind (Validate, Retag, DriftCheck), sampleSize? (1 to `Ontology.MaxDriftSampleSize`, default 10) }` → **202** `OntologyOperation`. **400** for `Validate` without a pinned version; **409** when one of that kind is already queued or running. |
| GET | `/v1.0/ontology-operations/{id}` | `{ operation, items }`. The operation carries `kind`, `status` (`Queued`, `Running`, `Succeeded`, `Failed`), `total`, `processed`, `changed`, `added`, `removed`, `driftRate`, `error`, and timestamps; each item `{ ordinal, nodeId, excerpt, changed, detail }`. |
| GET | `/v1.0/subjects/{id}/graph/export` | The subject's graph: `format=json` (default), `jsonld`, `turtle`, or `graphml`; `baseIri=`. Sent as a download; `X-Pneuma-Truncated: true` when the graph exceeded `Ontology.MaxGraphNodes`. |
| DELETE | `/v1.0/subjects/{id}/classification-cache` | Remove the classification cache entries the subject stored → `{ removed }`. |

## Model Runners (admin)

`GET|POST /v1.0/model-runners` · `GET|PUT|DELETE /v1.0/model-runners/{id}`. Pneuma **manages model endpoints natively** in its own `modelrunners` store; LLM access (embeddings, completions, summarization) runs in-process through PolyPrompt. Each item is an embedding or completion endpoint: `{ id, type (Embedding|Completion), provider, name, model, endpoint, apiFormat, deployment, apiVersion, region, project, accessKeyId, active, maxConcurrentRequests, maxRetries, maxInputTokens, maxQueueDepth, contextSize }`.

`provider` selects the PolyPrompt provider and determines which fields apply. Supported providers: **OpenAI, OpenAICompatible, Gemini, Ollama, AzureOpenAI, Anthropic** (completions only), **Bedrock, VoyageAI** (embeddings only), **VertexAI**. Provider-specific fields: `deployment` and `apiVersion` (AzureOpenAI), `region` and `accessKeyId` (Bedrock), `project` and `region` (VertexAI).

Create/update body `{ type (Embedding|Completion), provider, name?, model, endpoint?, apiFormat?, deployment?, apiVersion?, region?, project?, accessKeyId?, apiKey?, secretAccessKey?, sessionToken?, active, maxConcurrentRequests?, maxRetries?, maxInputTokens?, maxQueueDepth?, contextSize? }`. **Secrets are write-only** — `apiKey`, `secretAccessKey`, and `sessionToken` are accepted on create/update and **never returned** on read. `maxConcurrentRequests` caps concurrent requests to the endpoint across ingestion and chat (minimum 1, default 2; requests beyond it wait for a slot; a changed value applies after a restart); `maxInputTokens` is the largest embedding input the endpoint accepts in its own tokens (0, the default, uses the known limit for the model family); `maxRetries` is how many times a transient failure (408, 429, 502, 503, 504, or a 500 whose body names one or says the endpoint is at capacity) is retried, with exponential, jittered backoff that honors `Retry-After` (0 to 10, default 5; client errors are never retried). A call that still fails returns **503** `ModelUnavailable` with `Retry-After`, and one the endpoint rejected returns **502** `ModelRejected`; `maxQueueDepth` is how many requests may wait for a slot once that cap is reached (minimum 0, default 0 rejects over-limit requests immediately with 429, a queued wait past the endpoint timeout returns 504); `contextSize` is the completion model's context window in tokens and drives automatic chat conversation compaction (0 disables). Deletes resolve the endpoint type automatically.

### Model endpoint health

A background monitor probes each model endpoint's **base URL**, deduplicated so a host shared by several endpoints is checked once and the result is shared. Healthy/unhealthy transitions use a two-consecutive-check hysteresis; uptime, a rolling 24-hour history, latency, and the last HTTP status code are accumulated in memory.

| Method | Path | Description |
|--------|------|-------------|
| GET | `/v1.0/model-runners/health` | Health of **every** model endpoint (deduplicated by base URL). Array of `ModelEndpointHealth`. |
| GET | `/v1.0/model-runners/{id}/health` | Health of a single model endpoint. `404` if the endpoint is not found. |
| POST | `/v1.0/model-runners/{id}/health/check` | Run a single health probe against the endpoint **immediately** (no request body). `200` returns the endpoint health object (same `ModelEndpointHealth` shape as `GET /v1.0/model-runners/{id}/health`). `400` if health checks are disabled for the endpoint; `404` if the endpoint is not found. |

`ModelEndpointHealth`: `{ endpointId, endpointName, type, baseUrl, isHealthy, statusCode?, latencyMs?, firstCheckUtc?, lastCheckUtc?, lastHealthyUtc?, lastUnhealthyUtc?, lastStateChangeUtc?, totalUptimeMs, totalDowntimeMs, uptimePercentage, consecutiveSuccesses, consecutiveFailures, lastError?, history: [{ timestampUtc, success }] }`. Before a base URL's first probe, timestamps are null (a "pending" state).

## Prompts

`GET|POST /v1.0/prompts` · `GET|PUT|DELETE /v1.0/prompts/{id}`. Every prompt the platform sends to a model is a keyed
prompt, including `ontology.classify`, `ontology.definition`, `ontology.classify.format`, `taxonomy.hint`,
`ontology.propose`, `ontology.propose.format`, `cell.summarize`, and `user.answer`. Each prompt carries a computed
`isSystemDefault`.

A key resolves as **system default → tenant copy → subject override** ([Subject Prompts](#subject-prompts)). `GET`
lists the prompt in effect for the caller's tenant, one per key: the tenant's copy where one exists, else the system
default (`scope=system` lists only system defaults, `scope=tenant` only the tenant's copies). `PUT` on a system default
by a tenant user creates or updates the tenant's copy and leaves the system default unchanged; a system administrator
edits the system default itself, or saves a tenant copy with `scope=tenant`. `DELETE` of a tenant copy returns the
tenant to the system default; only a system administrator can delete a system default.

## Settings (admin)

Server configuration, restricted to the **system administrator** (`isAdmin`). Non-admins receive 403.

| Method | Path | Description |
|--------|------|-------------|
| GET | `/v1.0/settings` | Read the current settings. Secrets are masked as `********`. Returns `{ success, settings, meta }` |
| PUT | `/v1.0/settings` | Overwrite the settings file with the supplied `AppSettings` body → `{ success, restartRequired, message, meta }` |
| GET | `/v1.0/settings/ingestion` | Read the dashboard-tunable ingestion concurrency (`IngestionTuning`) — the system defaults every ingestion job uses |
| PUT | `/v1.0/settings/ingestion` | Persist the supplied `IngestionTuning` body. **Applied live — no restart required** |

`meta` describes the form: `sections[] { key, label, requiresRestart }` annotates which top-level sections need a server restart, `secretFields[]` lists the masked dot-paths, and `secretMask` is the sentinel. **Submitting a secret field unchanged (still equal to the mask) preserves the stored secret**; supplying a new value replaces it. Most sections require a restart to take effect; `cors` and `requestHistory` are applied live.

**Ingestion concurrency (`/v1.0/settings/ingestion`).** `IngestionTuning` is a flat object of integers. Per-stage parallelism caps: `contentRetrieval`, `typeDetection`, `cellExtraction`, `classification`, `graphMerge`, `summarization`, `chunking`, `embedding`, `indexing`. Job pool & summarization: `maxConcurrentTasks` (how many ingestion jobs run at once), `summarizationConcurrency`, `summarizationMinCellLength` (cells shorter than this many characters are not summarized). Classification batching: `classificationBatchSize` (cells classified per model call — a document with more cells is split into batches so a slow model never classifies a whole document in one call), `classificationBatchOverlap` (context cells included on each side of a batch so relationships spanning a batch boundary are still seen; duplicates are merged by the graph-merge stage), `classificationBatchConcurrency` (batches from one document processed concurrently). Timeouts: `stageTimeoutSeconds` (abort a single stage past this many seconds). `PUT` persists and applies the new values live to subsequent ingestion work. A **subject** may override any of these per-subject via its `concurrencyOverrides` field (see below); the effective value for a subject is its override if set, otherwise the system default here.

## Knowledge Graph (user)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/v1.0/graph/nodes/{id}` | Node contents |
| GET | `/v1.0/graph/nodes/{id}/neighbors` | Adjacent nodes |
| GET | `/v1.0/graph/nodes/{id}/edges` | Relationships (edges) for the node |

## Collections

Vector collections live in the retrieval store (RecallDB), which is the **authority** for tenants and collections; Pneuma simply relays administration to it and keeps no local collection state. Collections are **per-tenant**: each Pneuma tenant maps to a RecallDB tenant of the same id, and these endpoints operate within the caller's tenant. RecallDB assigns collection ids. A collection's `dimensionality` is **fixed at creation** and must match the embedding model used to ingest into it. Collections are create/read/list/delete (no update).

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
| GET | `/v1.0/search?q=<query>&max=20&mode=hybrid&collection=<id>&filter=<json>` | Search (`mode` = `text`, `vector`, or `hybrid`, default hybrid) resolved to a representative set of graph nodes → `{ query, mode, results: [{ node, score, snippet, linkId, documentId, fusedScore, vectorScore, textScore, vectorRank, textRank, chunkKind, position }] }`. `score` is the best raw channel score (cosine or TsRank); results are ordered by `fusedScore`, the RRF score normalized to 0..1 (1 = ranked first by every channel that ran), which is comparable across queries. `collection` is optional — when omitted the configured default (or the tenant's first active) collection is used. `filter` is an optional URL-encoded `RetrievalFilter` JSON (same shape as `metadataFilter` below) that scopes results to matching labels/tags |
| GET | `/v1.0/subjects/{subjectId}/search?q=<query>&maxResults=20&skip=0&mode=hybrid&granularity=document&collection=<id>&filter=<json>` | Search a single subject's ingested documents (RecallDB, filtered by `subjectId`). Returns a paginated `EnumerationResult` in fused order, one hit per source document (or, with `granularity=chunk`, one per retrieved passage); each hit is `{ documentId, score, matchCount, snippet, linkId, linkUrl, linkTitle, nodeId, fusedScore, vectorScore, textScore, vectorRank, textRank, chunkKind, position }`, linked back to the originating content link. Applies the subject's default `retrievalFilterJson` merged with the optional per-request `filter` (URL-encoded `RetrievalFilter` JSON), narrowing by label/tag. |
| GET | `/v1.0/subjects/{id}/retrieval/labels` | The distinct retrieval labels an operator has applied to the subject's links → `{ subjectId, labels: [string] }`. A bounded aggregate over the subject's links (not an enumeration), used to populate the Ask **Scope** filter with real values for a `metadataFilter`'s `requiredLabels`/`excludedLabels`. |
| GET | `/v1.0/subjects/{id}/retrieval/tags` | The distinct retrieval tag keys and their values applied to the subject's links → `{ subjectId, tags: { key: [value, …] } }`. A bounded aggregate (not an enumeration), used to build a `metadataFilter`'s `requiredTags`/`excludedTags` conditions. |
| POST | `/v1.0/query` | Grounded answer. Body `{ question, maxResults?, subjectId?, metadataFilter?, overrides? }` → `{ answer, sources: [node], grounded, insufficientSupport, model, generationMs }`. Each source node carries its originating content link as a `linkId` tag; `insufficientSupport` is true when nothing was retrieved and the answer is the fixed refusal. When `subjectId` is set, retrieval is restricted to that subject's documents. `metadataFilter` is an optional facet filter `{ requiredLabels: [string], excludedLabels: [string], requiredTags: [{ key, condition, value }], excludedTags: [...] }`. Labels are a `List<string>` matched against each chunk's per-label `label:<value>` tags (a chunk must carry every required label; any excluded label removes it) — these are exactly the labels supplied at ingestion (plus the document type); tags are key/value conditions over chunk tags (conditions: `Equals`, `NotEquals`, `Contains`, `StartsWith`, `EndsWith`, `GreaterThan`, `LessThan`, `IsNull`, `IsNotNull`). It is merged with the subject's default `retrievalFilterJson` (union of all four lists), narrowing — never widening — the subject default. |
| POST | `/v1.0/query/stream` | Same as `/v1.0/query` (incl. optional `subjectId`) but streamed over server-sent events (`metadata` / `delta` / `complete` / `error`). |
| POST | `/v1.0/warmup` | Best-effort warm-up of the answering model. Body `{ subjectId? }` → `{ warmed: bool }`. Resolves the subject's (or tenant default) answering runner and issues a minimal completion so the model is loaded before the first real question. The dashboards call this when the Ask page opens / a subject is selected. Never fails the caller. |
| POST | `/v1.0/chat/stream` | Multi-turn agentic chat over the corpus (the assistant may call read tools), streamed over SSE. Body `{ messages: [{ role, content }], maxResults?, subjectId?, metadataFilter? }`. When `subjectId` is set, the search and grounded-answer tools are restricted to that subject. `metadataFilter` (same shape as `/v1.0/query`) scopes the assistant's retrieval to matching labels/tags for this turn, merged with the subject's default filter. SSE event types: `delta`, `tool_call`, `tool_result`, `compacting` (the running history is being summarized to fit the completion model's `contextSize`), `complete`, `error`. The `complete` event carries `{ answer, model, …token telemetry…, toolCalls, citations: [{ linkId, url, title, score }], compacted, compactedSummary }`; when `compacted` is true the client should replace its prior message history with `compactedSummary`. |

**Per-request retrieval overrides (administrators only).** For tuning and benchmarking without a restart, system and tenant administrators may pass `overrides` on `/v1.0/query` and `/v1.0/query/stream` bodies, or query parameters of the same names on the search routes: `rrfK` (1–1000), `lexicalWeight` and `semanticWeight` (0–10), `diversityEnabled`, `diversityLambda` (0–1), `poolMultiplier` (1–25), `neighborExpansionEnabled`, `neighborExpansionMaxHops` (1–5), `neighborExpansionMaxNodes` (0–200). Unset fields keep the configured values; other callers' overrides are ignored.

**Query strings** are URL-decoded, so `q=two%20words` and `q=two+words` both search for "two words", and a URL-encoded `filter` JSON parses.

**Model-runner concurrency gate.** The grounded-answer and chat endpoints (`/v1.0/query`, `/v1.0/query/stream`, `/v1.0/chat/stream`, and the MCP `pneuma_query` tool) are admitted through a process-wide gate that caps concurrent model-runner usage. Requests beyond the cap queue up to a bounded depth; when the queue is full the request is rejected with **HTTP 429 Too Many Requests** (`{ "error": "TooManyRequests", ... }`) before any streaming begins — callers should back off and retry. Configured via the `ModelRunner` settings block (`MaxConcurrentRequests`, default 4; `MaxQueueDepth`, default 16).
