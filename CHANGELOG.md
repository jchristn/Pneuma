# Changelog

All notable changes to Pneuma (Pneuma - information brought to life) are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/). Pneuma is in its `0.x` alpha series: anything may change
between releases, and the project will adopt semantic versioning at its stable 1.0 release.

## [Unreleased]

### Added
- **Metadata / facet retrieval filters.** A subject can carry a default retrieval facet filter
  (`retrievalFilterJson`, schema v12) and `/v1.0/query` accepts a per-request `metadataFilter`; both are
  chunk-tag predicates (`required`/`excluded` with conditions Equals/NotEquals/Contains/StartsWith/EndsWith/
  GreaterThan/LessThan/IsNull/IsNotNull) pushed down natively to RecallDB's tag filter. The per-request filter
  is merged with the subject default (union — narrows, never widens). Applied on both the grounded and agentic
  search paths; the effective filter is recorded on the chat turn and shown in the History detail modal, and a
  subject's default filter is editable in the admin/creator subject forms.
- **Per-stage chat telemetry.** Every answered agentic turn now records structured, provider-agnostic
  performance telemetry (schema v11): a serialized `TurnPerformance` on the turn's `performanceJson` (ordered
  stages — prompt rewrite, compaction, each tool call, final inference — with duration, time-to-first-token,
  tokens, provider/model) **and** one queryable `chatturnperfevents` row per stage for analytics. Surfaced on
  `GET /v1.0/history/{id}`; the admin and creator History detail modals render a stage-details table and drive
  their timing bars from real per-stage data. Perf events are pruned with their subject's retention window and
  removed on subject cascade.

### Changed
- **Cells are the graph's unit of source content; chunks live only in RecallDB.** Ingestion no longer
  creates a `Chunk` node per chunk. Instead the graph-merge stage materializes a `Cell` node per extracted
  semantic cell (carrying its text, linked to its `Source` via `HAS_CELL`), and each chunk is stored only as
  a RecallDB document whose `litegraphNodeId` points back at its originating cell node (falling back to the
  source). Retrieval hits still resolve to a graph node for structure and neighbor expansion, but the graph
  is no longer inflated with one node per chunk. Legacy `Chunk`/`HAS_CHUNK` types are retained so any
  pre-existing chunk nodes still resolve; cascade deletion removes cell nodes by the job's `assertedByJob`
  tag as before.
- **Retrieval store migrated from Verbex to RecallDB.** Verbex is removed entirely. RecallDB (Postgres +
  pgvector, `:8600`) is now the retrieval store for both vector and full-text search, hidden behind the
  same `IVectorRepository` / `IInvertedIndex` interfaces plus a new `ICollectionStore`. Vectors are no
  longer stored on LiteGraph nodes — each chunk is stored in RecallDB as one document carrying its content,
  embedding, and provenance tags (`litegraphNodeId` round-trips on hits so retrieval still resolves to the
  chunk's graph node). Cascade deletion removes a job's documents by its `jobId` tag. Pneuma operates under
  a dedicated RecallDB tenant (`pneuma`), ensured at startup.

### Added
- **Subject-scoped chat controls (thinking, prompts, slug, retention).** Subjects (schema v6) gained
  `urlSlug`, `thinkingEnabled`, `systemPrompt`, `ontologyClassifyPrompt`, `ontologyDefinitionPrompt`,
  `historyRetentionDays` (clamped ≥ 1), and a `deletionStatus`, all editable via REST (`PUT
  /v1.0/subjects/{id}`) and the admin/creator dashboards. Model **thinking** (`<think>…</think>`) is now
  stripped from chat answers server-side and streamed separately; per subject it is either hidden or shown
  in a collapsed section with a *Thinking time* statistic. A subject's **system prompt** is appended after
  the global one for its chats, and its **ontology prompts** are appended after the global
  `ontology.classify` / `ontology.definition` during ingestion (global base + subject appended). Each
  subject has a unique **URL slug** (`GET /v1.0/subjects/by-slug/{slug}`; explicit clashes → 409); the user
  dashboard now shows a card per subject and opens `/{slug}` as that subject's chat.
- **Chat history + feedback.** Every completed chat turn is persisted (question, answer, thinking, model,
  tokens, timing, citations) with per-subject retention pruning (schema v7). Users can rate any answer
  (👍/👎 + optional comment) inline. New endpoints: `GET /v1.0/history`, `GET /v1.0/history/{id}`,
  `GET /v1.0/feedback`, `POST /v1.0/feedback`. Admin and creator dashboards gained **History** and
  **Feedback** views with full-detail modals.
- **Background subject deletion.** Deleting a subject now returns `202` immediately and runs the heavy
  cascade (links, jobs, events, artifacts, graph, index, history, feedback) in a background worker; the
  subject is marked *Deleting* (greyed in the UI) until removed, and interrupted deletions resume on
  startup. The dashboards show a dismissible "deleting in the background — you may close this window" notice.
- **Ingestion Queue / Jobs subject filter.** Both admin pages gained a subject dropdown (`?subjectId=` on
  `GET /v1.0/jobs`).
- **Richer table-atom ingestion.** DocumentAtom tables are now flattened to one valid markdown cell per
  data row (header + separator + row) so column context travels with every value; all atom types are
  ingested except binary.
- **MCP + SDK coverage for subjects/history/feedback.** New MCP tools `pneuma_create_subject` /
  `pneuma_update_subject` (mirroring the REST create/update, including slug uniqueness). The C# SDK
  (`sdk/csharp`) gained the new subject fields, `GetSubjectBySlugAsync`, and history/feedback models +
  `ListHistoryAsync` / `GetHistoryTurnAsync` / `ListFeedbackAsync` / `SubmitFeedbackAsync`. Positive and
  negative tests were added for the DB layer, the MCP tools, and the SDK smoke harness.

### Fixed
- **Fresh-install migration for the queue-duration column.** The `ingestionjobevents.queuedurationms`
  column (and the new subject columns) were being created in both the baseline schema and a migration,
  which failed on a fresh SQLite/MySQL database with a duplicate-column error. Baseline `CREATE` statements
  are now the original v1 shape and the versioned migrations are the sole source of later columns.

### Added (earlier)
- **Per-tenant LiteGraph isolation.** Each Pneuma tenant now gets its own isolated LiteGraph tenant and
  graph, created and hydrated when the tenant is provisioned; the LiteGraph tenant/graph GUIDs are recorded
  on the Pneuma tenant record (`LiteGraphTenantGuid`/`LiteGraphGraphGuid`, schema v4). All graph operations
  (ingestion writes, search/query reads, cascade deletes, MCP graph tools) route through a per-tenant
  `IGraphRepositoryFactory`, so one tenant can never read or write another's graph — with a fallback to the
  configured default graph for tenants not yet provisioned. Postgres now installs pgvector (via the postgres
  image build) with the `recalldb` database + extensions created in init.
- **Per-tenant collections + automatic provisioning.** Each Pneuma tenant maps to a RecallDB tenant of the
  same id; collection administration and retrieval (ingestion store, vector/full-text search, cascade
  delete) are all tenant-scoped. RecallDB is the authority for tenants and collections — Pneuma relays and
  assigns no ids of its own. Creating a Pneuma tenant (and first-boot seeding) now provisions the tenant on
  RecallDB and creates a **default collection** for it, via a best-effort, idempotent, extensible
  `TenantProvisioningService` (`ITenantProvisioner`). Configurable default collection name/dimensionality.
- **User-managed vector collections.** Operators define collections in the Pneuma dashboard
  (`GET/PUT/DELETE /v1.0/collections`, proxied to RecallDB); a collection's `dimensionality` is fixed at
  creation. Ingestion now **requires** a collection: link submission takes a `collectionId`, persisted on
  the ingestion job and used as the store/search target. The admin dashboard gains a Collections page and
  a collection selector on link submission (single + bulk); the subject dashboard gains the selector.
- **Collection-scoped search.** `GET /v1.0/search` and `GET /v1.0/subjects/{id}/search` accept an optional
  `collection` query parameter; grounded query/chat and MCP search resolve a default collection when none
  is specified.

## [0.1.0] - 2026-08-18

### Added
- **Discrete Summarization, Chunking, and Embedding ingestion steps.** These now call Partio's separate
  `/v1.0/summarize`, `/v1.0/chunk`, and `/v1.0/embed` endpoints (Partio 0.4.0) and run as three distinct,
  independently-timed pipeline stages, each with its own duration recorded in the job log (the Follow Logs
  modal shows per-step and total runtime). Embedding is done in bounded batches.
- **One Verbex document per source.** Ingestion indexes a source's full text as a single lexical document
  (tagged with `linkId`/`tenantId`/`subjectId`/`jobId`/`sourceUrl`/`documentType`) rather than one document
  per chunk, so a search returns one hit per ingested source. Chunk-level graph nodes and vectors are still
  created for semantic retrieval.
- **Subject search grouped per source.** `GET /v1.0/subjects/{id}/search` rolls hits up to one result per
  source link (best score + `matchCount`), and the admin Search table shows Score, Matches, Title, URL,
  Link ID, and the top passage.
- **Answering model falls back to the Partio completion endpoint** when no explicit Pneuma model runner is
  configured, so grounded query / chat works out of the box with the configured completion model.
- **Parallelized cascade deletion.** Verbex document deletes and LiteGraph node/edge deletes run with bounded
  concurrency, so deleting a link/subject with many indexed documents is dramatically faster.
- **Clearer ingestion event log.** The Categorization phase is recorded as a single `Completed` event (with
  prompt provenance folded in), and Hydration emits a matching `Completed` event, so no phase lingers as
  `Processing`.
- Admin dashboard: health histogram capped to 10 bars in the table / 50 in the modal; the Follow Logs modal
  shows total runtime across all stages at the top.
- Docker compose pins Partio to `v0.4.0` (adds `/v1.0/chunk` and `/v1.0/embed`).
- **Integrated MCP server.** The Pneuma host now serves a Model Context Protocol endpoint at `POST /mcp`
  (JSON-RPC 2.0) in-process, so agents drive the platform through the *same* `AuthenticateRequest` hook,
  `AuthorizationService`, and audit trail as the REST API — the same bearer/API-key credentials apply and
  every decision is audited identically. Collections are never returned unbounded: `pneuma_enumerate_*` tools
  page through the `EnumerationQuery`/`EnumerationResult` envelope with small summary projections (the
  first page carries `totalRecords`), and full objects are fetched one at a time with `pneuma_get_*`. Ships
  `pneuma_capabilities`, `pneuma_enumerate_subjects`/`pneuma_get_subject`, and `pneuma_enumerate_jobs`/`pneuma_get_job`,
  plus discovery via `tools/list`. Documented in `MCP_API.md`.
- **Streaming grounded chat.** A new `POST /v1.0/query/stream` streams the grounded answer over
  server-sent events (`metadata` / `delta` / `complete` / `error`, with an `insufficientSupport` refusal).
  All three dashboards (admin, subject, user) now have a live "Ask" surface that renders the answer as it
  arrives, backed by a dependency-free `sse.js` client.
- **Provider-neutral integration interfaces.** Ingestion and retrieval now depend on role interfaces —
  `IGraphRepository`, `IVectorRepository`, `IAtomizer`, `ISemanticProcessor`, `IInvertedIndex`,
  `IServiceProbe` — rather than vendor client names, so a backend can be swapped without touching
  orchestration.
- **Resilient outbound HTTP.** All integration clients (DocumentAtom, Partio, Verbex, LiteGraph) extend a
  shared `IntegrationClientBase` with transient-failure retry, per-attempt timeout, a per-service
  concurrency bulkhead, uniform structured failures, and per-call telemetry. Writes never retry, so a
  non-idempotent create is never duplicated. Configurable via `Integrations.Resilience`.
- **Startup connectivity diagnostics.** On boot Pneuma probes every external dependency concurrently and logs
  a secret-safe reachable / erroring / unreachable report; an optional `Diagnostics.FailFastOnStartupProbe`
  aborts startup on an unreachable required service.
- **Automated two-stage ingestion.** Ingestion runs as an explicit `Categorize` phase (fetch → atomize →
  classify into a candidate plan) followed by a `Hydrate` phase (commit to the graph, embeddings, and
  index), visible as phase events in "Follow Logs". Each run also records **prompt provenance** — the
  version and content-hash of the prompts that shaped it — so a past run stays reproducible even after
  prompts are edited.
- **Chunks are first-class graph nodes.** Each semantic chunk becomes a `Chunk` node linked to its source,
  carrying its text and (when embedded) its vector, so semantic search resolves to chunk-level content.
- **Session refresh.** `POST /v1.0/token/refresh` rotates the session — issuing a fresh token and revoking
  the prior one, so a leaked earlier token stops working — and records it in the audit stream.
- **Grounded question answering** blends lexical (inverted-index) and semantic (graph vector) retrieval
  with optional graph-neighbor expansion, exposed over REST (`/v1.0/query` + SSE stream) and the MCP
  `pneuma_query` tool from one shared service.
- **Continuous integration.** A `.github/workflows/ci.yaml` builds the solution, runs the automated/xUnit/
  NUnit suites (each Touchstone case now reported individually) and a live-PostgreSQL job, and builds and
  tests the three dashboards (vitest). Root `build.bat`/`test.bat` mirror it locally.
- **Search page (Content).** A new admin **Search** view lets an operator pick an subject and full-text
  search that subject's ingested documents (Verbex), with results paginated and ranked by score and each
  hit linked back to the content link it came from. Backed by
  `GET /v1.0/subjects/{subjectId}/search?q=&maxResults=&skip=` (Verbex search filtered by `subjectId`,
  resolving each document's job back to its link).
- **Fuller web-page capture for ingestion.** The headless-browser (Playwright/Chromium) content fetcher
  now renders at a real 1920×1080 desktop viewport, auto-scrolls (bounded) to trigger lazy/infinite-scroll
  content, waits for the network to settle, and force-opens collapsible `<details>` regions before
  capturing the DOM — so DocumentAtom's cell extraction sees the complete page instead of only the
  above-the-fold/initial content. All steps are best-effort and time-boxed.
- **Subject delete now cascades** through every subordinate object — all of the subject's links (each
  with its full link cascade: jobs, processing logs, S3 pipeline artifacts + raw blobs, and Verbex
  index documents) and the subject's entire LiteGraph subgraph (matched on the `subjectId` tag). The
  link, job, and subject cascades are now centralized in a single `CascadeDeletionService`.

### Fixed
- **Administrator bypasses are now audited.** System-admin and tenant-admin authorization bypasses were
  recorded only in memory; they are now persisted as `AuthorizationBypass` audit records, matching the
  documented "all denials and bypasses are audited" contract.
- **Server-grade databases are no longer globally serialized.** The SQLite single-writer semaphore was
  also throttling PostgreSQL, MySQL, and SQL Server (which open pooled connections per call); it is now
  scoped to SQLite only, removing an unnecessary bottleneck on the deployed database.
- **Model endpoint edits no longer reset on restart.** Partio reconciles its `id="default"` embedding
  endpoint back to `partio.json`'s `DefaultEmbeddingEndpoints` on every boot, which silently reverted
  dashboard edits (new URL/model) to the seeded values. `DefaultEmbeddingEndpoints` and
  `DefaultInferenceEndpoints` are now empty in `partio.json` (disabling the reconcile), and Pneuma instead
  seeds a default embedding + completion endpoint on startup **only when none exist** (create-only, never
  overwriting), so operator edits persist across server and stack restarts.

### Added
- **Model endpoint health monitoring:** a background monitor probes each model endpoint's base URL,
  **deduplicated by base URL** (a host shared by multiple endpoints is checked once and the result
  fanned out), with two-check healthy/unhealthy hysteresis and an in-memory rolling 24h history,
  uptime %, latency, and last HTTP status code. Exposed as `GET /v1.0/model-runners/health` (all
  endpoints) and `GET /v1.0/model-runners/{id}/health`. The admin **Model Runners** page shows a
  per-row health pill + history sparkline (polled every 15s) that opens a health-details modal;
  the health cell does not trigger the row's edit modal. REST_API.md and the Postman collection updated.
- Initial repository scaffold: solution, core library, Watson 7.1 server, four-provider data layer,
  three dashboards (admin, subject, user), SDKs (C#, JS, Python), and Docker Compose stack.
- Knowledge-graph ontology for subject archives (subject-neutral) backed by LiteGraph.
- Ingestion pipeline: DocumentAtom type detection + cell extraction, PolyPrompt ontology
  classification, LiteGraph subgraph merge, Partio chunk/embed/summarize, Verbex indexing.
- Full RBAC (tenants, users, credentials, roles, permissions, assignments, sessions, audit).
- Observability via Radiant traces, Prometheus metrics, Grafana dashboards, and Tempo.
- Admin-editable prompts for every model interaction, and a **natural-language ontology** (the
  `ontology.definition` prompt) that drives classification and can be rewritten without code changes;
  the graph merge accepts whatever node/edge types the admin's ontology defines.
- Test suite (Touchstone, runnable via console/xUnit/NUnit): 33 cases spanning crypto, RBAC,
  token codec, four-provider database contracts, graph merge/entity-resolution, the ingestion state
  machine (fakes), and end-to-end HTTP auth/RBAC/request-history/metrics against an in-process server —
  including list pagination, subject slug derivation, settings read/write masking, and job stop/log.
- **Paginated list APIs:** every collection `GET` now returns an `EnumerationResult<T>` envelope
  (`success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `objects`) and
  honors `maxResults`, `skip`, `order` (asc/desc), and `search` query parameters. REST_API.md, the
  Postman collection, all three SDKs (C#/JS/Python), and the dashboards consume the new shape.
- **Server settings API:** `GET /v1.0/settings` (secrets masked) and `PUT /v1.0/settings` (overwrites
  the settings file; a masked secret submitted unchanged preserves the stored value), with metadata
  annotating which sections require a server restart. Backed by a styled admin settings form.
- **Subject graph root node id** is derived from the display name as a slug (e.g. "The Bomb Squad" →
  `the-bomb-squad`) when not supplied.
- **Ingestion job control:** `POST /v1.0/jobs/{id}/stop` cancels a queued or in-flight job (mid-flight
  cancellation is honored between pipeline stages) and `GET /v1.0/jobs/{id}/log` returns the live
  per-stage log for a follow-logs view.
- **Per-link model selection:** submitting a link now requires an embedding and completion endpoint id
  (`GET /v1.0/ingestion/endpoints` lists the available Partio-backed models); `POST /v1.0/subjects/{subjectId}/links/bulk`
  submits many URLs at once. The chosen ids are stored on the ingestion job (new columns; schema migration v2)
  and passed through to Partio. Surfaced in the admin/subject dashboards (required dropdowns + "Add Multiple").
- **Headless-browser crawling:** the ingestion content fetcher renders pages with headless Chromium
  (Playwright) so JavaScript-heavy sites are captured, falling back to a plain HTTP fetch; the server image
  installs Chromium at build time (`Ingestion.UseHeadlessBrowser`, `BrowserNavigationTimeoutMs`).
- **Custom themed 404 page** in all three dashboards (catch-all routes and unknown sections).
- **Job detail / follow-logs modals** gained a copy-to-clipboard control that copies the processing log as JSON.

- **Object storage (Less3 / S3):** added single-node `less3` + `less3-ui` (v4.0.0) to the stack. Pipeline
  artifacts are persisted to five S3 buckets — source (on crawl), atoms (DocumentAtom), chunks and
  embeddings (Partio), and subgraph JSON (model) — each keyed by the originating link id. S3 endpoint,
  credentials, region, and bucket names live in server settings; buckets are auto-created at startup.
  New `GET /v1.0/links/{id}/{source|atoms|chunks|vectors|subgraph}` endpoints plus Links action-menu
  items (View Source Document / View Atoms / View Chunks / View Vectors / View Subgraph).
- **LiteGraph startup initialization:** the server ensures the LiteGraph tenant, user, credential, and
  "Pneuma" graph exist on boot (idempotent), so the knowledge graph is ready without manual setup.

### Changed
- Audit list endpoint dropped its bespoke `?max=` parameter in favor of the standard pagination params.
- **Identifiers** are now 32 characters total (`{prefix}{ksortable}_{random}`).
- **All subordinate data services use the shared PostgreSQL** instance (each with its own database:
  `less3`, `litegraph`, `partio`, `verbex`) instead of SQLite; the databases are created on first boot.
- Dashboard "server URL" env vars now point at browser-reachable host ports for the DocumentAtom,
  Verbex, Partio, LiteGraph, and Less3 UIs.
- `Ingestion.StageTimeoutSeconds` default raised to 1800s to accommodate slow local LLM inference.

### Fixed
- Integration clients (DocumentAtom, LiteGraph, Partio, Verbex) bind all interfaces (`*`) in the Docker
  settings so they are reachable across the compose network (they shipped bound to container loopback).
- Partio and Verbex request bodies are serialized in PascalCase to match those services' case-sensitive
  deserializers (previously camelCase produced `"… is required"` 400s).
- Partio embedding/completion endpoints point at the `ollama` service (was container `localhost`).
- Compose image tags pinned to known-good versions (Tempo `2.6.1`, Prometheus `v3.5.4`, Grafana `13.0.2`,
  Ollama `0.31.2`, Partio images by digest) after `grafana/tempo:latest` broke on a v3.0.0 dev build.
