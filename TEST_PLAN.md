# Pneuma Test Plan — coverage assessment and extension plan

Status: **draft for review.** This document assesses the current automated-test infrastructure, identifies
gaps by layer, and proposes a prioritized plan to reach "every layer, every service, every interface, every
API — with positive and negative cases." It does not change any test yet.

## 1. Current state

**Harness.** Touchstone descriptors in `Test.Shared/Suites/*` (no console output), run three ways:
`Test.Automated` (CLI), `Test.Xunit`, `Test.Nunit`. Shared helpers in `Test.Shared/Support/`:
`TestServer` (in-process `PneumaServer` over SQLite with **fake** integrations — `FakePartioClient`,
`FakeRecallDbClient`, `FakeLiteGraphClient`, `FakeDocumentAtomClient`, `FakeContentFetcher`,
`FakeGraphRepositoryFactory`), `TestDatabase` (provider chosen by `PNEUMA_TEST_DB_TYPE`, default SQLite),
and HTTP message handlers for resilience testing (`Recording`/`Sequenced`/`Delaying`).

**Volume.** 110 cases across 8 suites: Security (12), Database (30), Graph (4), GraphTenancy (2),
Ingestion (5), ExternalServices (18), Api (37), Collections (2).

**Strengths.** Good negative-case discipline already exists in spots (wrong password, unknown access key,
tamper token, malformed AES, slug clash, missing displayName, unknown document type, no-collection failure,
transaction rollback, oversized-maxResults clamp). Resilience (retry/timeout/bulkhead/write-no-retry),
provenance immutability, retention pruning, tenant isolation (users, collections), and cascade deletes are
covered. The RBAC decision engine has focused unit tests (deny-wins, implicit-deny, wildcard, write-expansion).

**CI.** `backend` job (SQLite, net8), `postgresql-live` job (live Postgres via `PNEUMA_TEST_DB_TYPE`),
`dashboards` matrix (build + `vitest run`), `file-size-guardrail`. Tests run on **net8 only** in CI though the
projects multi-target net8/net10.

## 2. Gap analysis (by layer)

### 2.1 Provider matrix — **the single biggest gap**
CLAUDE.md and the requirements state the shared DB contract suites must run against **all four** providers
(Sqlite, Postgresql, Mysql, SqlServer). CI runs only **SQLite** (default) and **Postgresql** (live job).
**MySQL and SQL Server are never exercised** — handwritten SQL dialect differences (e.g. `IF NOT EXISTS`
vs `IF COL_LENGTH(...)`, identifier quoting, `LIMIT` vs `TOP`, boolean/JSON column handling) are unverified,
so a provider-specific migration or query bug ships undetected.

### 2.2 API / HTTP layer — large coverage holes
117 registered endpoints; ApiSuite exercises ~30 of them. Whole route families have **zero HTTP-level tests**
(they may have DB-layer coverage, but not the route: validation, status codes, authz-by-role, tenant scoping,
serialization shape):

- **RBAC admin CRUD**: `tenants`, `users`, `credentials`, `roles`, `permissions`, `assignments`,
  `model-runners`, `prompts` — only one route is touched (`NonAdmin_Forbidden`). No create/read/update/delete
  + 400/404/403/cross-tenant tests.
- **audit** (`GET /v1.0/audit`) — no API test.
- **graph** (`GET /v1.0/graph/nodes/{id}`, `/edges`, `/neighbors`) — no API test.
- **search** (`GET /v1.0/search`, `GET /v1.0/subjects/{id}/search`) — no API test.
- **history** (`GET /v1.0/history`, `/{id}`) — DB layer only.
- **feedback** (`GET /v1.0/feedback`, `POST /v1.0/feedback`) — no API test.
- **ingestion endpoints** (`GET /v1.0/ingestion/endpoints`), **jobs** `summary`/`restart`, **link artifacts**
  (`source`/`atoms`/`chunks`/`vectors`/`subgraph`/`log`) — untested.
- **subject update/delete** (`PUT`/`DELETE /v1.0/subjects/{id}` → 202 + background worker) — untested at API.
- **eval facts REST** (`POST`/`GET`/`DELETE /v1.0/eval/facts`) — MCP twin tested, REST not directly.
- **warmup** (`POST /v1.0/warmup`), **model-runner health REST**, **by-slug** — untested at API.
- **CORS preflight / PostRouting hooks / 404 + 405 handling** — untested.

### 2.3 Data layer — interfaces with no round-trip test
DatabaseSuite covers subjects, tenants, users, credentials, chat turns/threads/tool-calls/perf-events/feedback,
eval facts/runs/results, ingestion jobs+events, links. **No DB round-trip/negative tests** for:
`accounts`, `administrators`, `audit`, `authsessions`, `permissions`, `roles`, `rolepermissionmaps`,
`userroleassignments`, `userrolemaps`, `credentialscopeassignments`, `modelrunners`, `requesthistory`.
Also missing: composite-unique-index violation behavior, `TenantId` scoping on every enumerate, and
pagination/order/search parameters on `EnumerationResult` per repository.

### 2.4 Services — untested or thinly-tested
No dedicated tests for: `AgenticChatService` (tool loop, max-iteration cap, compaction trigger, think-parse,
title summarization, tool-trace persistence, effective-filter merge), `GroundedQueryService`
(`RewriteQuestionAsync` subject-grounding + no-placeholder, `ResolveEffectiveFilterAsync` union-merge, rerank,
neighbor expansion, insufficient-support refusal), `ModelRunnerGate` (admission, `ModelRunnerBusyException`,
queue-depth), `ModelHealthMonitor` (base-URL dedup, healthy/unhealthy hysteresis, rolling history),
`RequestHistoryCaptureService` (secret redaction + body truncation + retention prune — only "captured" is
asserted), `IngestionWorkerService`/`EvalWorkerService` (claim-loop, gate yielding), `SubjectDeletionWorker`
(background cascade + resume-on-startup), `CollectionResolver`, `PartioEndpointInitializer`,
`LiteGraphInitializer`, `SettingsRedactor` (unit), `CascadeDeletionService` (direct, beyond the DB/ingestion
paths). `AnalyticsService`, provisioners, and diagnostics are reasonably covered.

### 2.5 Integration interfaces — real-client contract holes
Role interfaces have fakes; ExternalServices covers LiteGraph (clamp/read/round-trip), DocumentAtom
(flatten/skip-binary), resilience, and vector/inverted-index facet honoring, plus a live RecallDB test gated on
`PNEUMA_LIVE_STACK=1`. Gaps: **`IPartioClient`/`ISemanticProcessor`** wire contract (the PascalCase request
serialization that previously caused 400s — no regression test), **`IContentFetcher`** (headless fetch/scroll —
hard, at least a plain-HTTP-fallback test), and **`IServiceProbe`** per-service classification beyond the
aggregate diagnostics test. Live-stack tests exist for RecallDB only; Partio/LiteGraph/DocumentAtom have no
opt-in live contract test.

### 2.6 MCP surface
Good breadth now (tools/list, paging, clamp, subject CRUD, query/search/stream, eval write, threads,
facets, ops). Gaps: **per-tool RBAC negative** (a Read-only credential is 403'd on each write/delete/admin
tool — currently only one generic unauthenticated test), the **admin-only ops tools denied to a non-admin**,
and **enumeration `endOfResults`/`recordsRemaining` correctness across multiple pages** for the newly-paged
tools.

### 2.7 SDKs
`sdk/js/test`, `sdk/python/tests`, and `sdk/csharp/src/Test.Automated` exist but **no CI job runs them** — SDK
drift from the REST contract is undetected. Coverage completeness within each SDK is also unaudited.

### 2.8 Dashboards
All three dashboards have `vitest` wired and a `dashboards` CI job, but there appear to be **no `*.test.jsx`
files** — the harness runs green with zero tests. No coverage of `ApiClient`, i18n fallbacks, `RetrievalFilter`
helpers, `streamSse`, or critical components (ScopeFilter, ThreadSwitcher, DataTable paging, ConversationsView).

### 2.9 Positive/negative balance
Negative cases exist but are concentrated in auth/security/ingestion. Most CRUD and every new route family lack
the negative half: 400 (validation), 404 (missing), 403 (wrong role), 409 (uniqueness), and cross-tenant
isolation at the HTTP layer. Target: **every mutating endpoint has at least one positive and one negative
case; every enumerate has a tenant-isolation case.**

## 3. Proposed plan (phased, prioritized)

### P0 — Close the correctness-critical gaps (do first)
1. **[DONE] Run the DB contract suite on all four providers in CI.** Added `mysql-live` and `sqlserver-live`
   jobs mirroring `postgresql-live`, each setting `PNEUMA_TEST_DB_TYPE`. SQLite remains the default job. The
   runner (`Test.Automated`) also now accepts DB connection details as **CLI arguments**
   (`--type/--host/--port/--user/--pass/--schema/--database`, mapped onto the `PNEUMA_TEST_DB_*` env vars that
   `TestDatabase` reads, so xUnit/NUnit honor the same config); `TestDatabase` supports `--schema` and a
   supplied maintenance/base database, preserving per-case isolation.
2. **[DONE] DB round-trip + negative tests for the untested repositories** (2.3): added `AuthDatabaseSuite`
   (accounts, administrators, authsessions, credentials, credentialscopeassignments), `RbacDatabaseSuite`
   (permissions, roles, rolepermissionmaps, userroleassignments, userrolemaps, audit), and `OpsDatabaseSuite`
   (modelrunners, requesthistory, prompts). Each: create/read/update/delete, read-missing→null, unknown
   natural-key lookup, tenant-scope isolation, and prune where applicable — run across all four providers via
   P0.1. (Remaining nice-to-have: composite-unique-violation assertions.)
3. **HTTP CRUD + authz + negative for the RBAC admin route families** (2.2): tenants, users, credentials,
   roles, permissions, assignments, model-runners, prompts. Each: happy CRUD, 400 on bad body, 404 on missing,
   403 for an under-privileged role, and cross-tenant 404/deny. Extend `TestServer` with a helper to mint a
   scoped non-admin credential.

### P1 — Service-level unit tests (2.4)
4. `GroundedQueryService`: rewrite grounds in subject name and never invents a placeholder (regression for the
   "medication X" fix); effective-filter union-merge (narrows never widens); insufficient-support refusal;
   neighbor-expansion bound; rerank ordering (fake reranker).
5. `AgenticChatService`: max-iteration cap forces a final answer; compaction triggers past the threshold;
   think-parse strips `<think>`; title summarization fires past 500 chars and freezes on manual rename;
   tool-call trace persists; per-request filter merges with the subject default.
6. `ModelRunnerGate` (admission + `ModelRunnerBusyException` + queue depth), `ModelHealthMonitor` (base-URL
   dedup, healthy/unhealthy hysteresis, rolling history), `RequestHistoryCaptureService` (redaction +
   truncation + retention prune), `CollectionResolver`, `SettingsRedactor` (unit), `SubjectDeletionWorker`
   (cascade + resume), worker services (claim-loop + gate yield).

### P2 — Full API + MCP breadth (2.2, 2.6)
7. Remaining endpoints: graph reads, search (both), history, feedback, ingestion endpoints, job summary/restart,
   link artifacts, subject update/delete (202 + worker), eval facts REST, warmup, by-slug, model-runner health
   REST, `/metrics` content, 404/405, CORS preflight. Each with a positive and a negative case.
8. MCP per-tool RBAC negative matrix (a table-driven test: for each write/delete/admin tool, a low-privilege
   credential gets a JSON-RPC "Not permitted" error) and multi-page enumeration correctness
   (`endOfResults`/`recordsRemaining`).

### P3 — Integration contracts, SDKs, dashboards (2.5, 2.7, 2.8)
9. Real-client contract tests behind `PNEUMA_LIVE_STACK=1` for Partio (summarize/chunk/embed, PascalCase
   serialization regression), LiteGraph, and DocumentAtom, mirroring the existing gated RecallDB test; plus a
   plain-HTTP `IContentFetcher` fallback test.
10. **CI job to run the SDK test suites** (js/pytest/csharp) against an in-process or live server; audit each
    SDK for method-per-endpoint parity with the REST surface.
11. Dashboard `vitest` tests for `ApiClient` (request building, error mapping), `streamSse`, i18n fallback
    resolution, `RetrievalFilter`/`isEmptyFilter` helpers, and the high-value components (DataTable paging,
    ScopeFilter, ThreadSwitcher, ConversationsView).

## 4. Infrastructure improvements
- **Harness helpers**: `TestServer.CreateScopedCredentialAsync(role)` for authz-negative tests;
  `McpCall(tool, argsJson)` + `McpResultText` (already added) promoted to a shared helper; a
  `ProviderMatrix` note in `BACKEND_TEST_ARCHITECTURE.md`.
- **Coverage measurement**: add `coverlet` to the test run and publish a coverage summary in CI (target a
  ratchet, not a hard number initially) so regressions in coverage are visible.
- **Run tests on net10 in CI** too (currently net8 only), since the code multi-targets.
- **Live-stack CI lane** (optional, nightly): a compose-up job that flips `PNEUMA_LIVE_STACK=1` and runs the
  gated integration + SDK contract tests against the real services.

## 5. Conventions for new tests (per requirements)
- Touchstone descriptors in `Test.Shared/Suites`, no console output; register in `PneumaSuites`.
- Every mutating endpoint/tool: ≥1 positive and ≥1 negative (400/404/403/409) case.
- Every enumerate: a tenant-isolation case and a paging (`endOfResults`) case.
- DB contract tests live in `DatabaseSuite`-style suites so they run across all four providers automatically.
- Bind test servers to `127.0.0.1`; keep fakes deterministic; no reliance on wall-clock (`Date.now` etc.).

## 6. Rough sizing
- P0: ~1 CI change + ~50–70 DB/HTTP cases. P1: ~30–40 service cases. P2: ~60–80 API/MCP cases.
- P3: ~20–30 integration/SDK cases + a dashboard test baseline.
- End state: on the order of ~350–400 backend cases with the four-provider matrix, plus SDK and dashboard lanes
  wired into CI.
