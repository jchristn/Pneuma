# Changelog

All notable changes to Pneuma (Pneuma - information brought to life) are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/), and the project adheres to semantic versioning.

## [Unreleased]

### Added
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
