# ASSISTANTHUB_FEATURES.md — AssistantHub → Pneuma capability adoption plan

Full-product implementation plan for seven capabilities identified in the AssistantHub → Pneuma gap
analysis. Each item is broken down across **backend, data layer (all four providers), MCP, REST, SDKs,
documentation, Postman, dashboards (admin / subject / user), and tests**, and ends with an explicit
**usability / design / aesthetics validation** phase. Every step is a checkbox so a developer can annotate
progress in place.

> **Scope confirmed with product owner:** #6 = full multi-thread conversations; #4 = full LLM-judged eval
> harness; #1 = `PerformanceJson` on the turn **and** a queryable per-stage performance-event table.

## How to use this document

- Tick `- [x]` as each step lands. Leave a short note in parentheses when a step is intentionally skipped.
- Status legend for the per-feature header line: `⬜ Not started · 🟨 In progress · ✅ Done · ⛔ Blocked`.
- Do **not** mark a feature "Done" until its acceptance checklist *and* the Phase 8 cross-cutting validation
  pass for that feature.

## Items in scope (numbering follows the original gap analysis)

| # | Capability | Phase | Primary dependency |
|---|---|---|---|
| 1 | Structured per-stage performance telemetry (`PerformanceJson` + perf-event table) | 1 | — (foundation) |
| 2 | Metadata / facet retrieval filters | 2 | — |
| 6 | Conversation threads (full) + persisted tool-call trace | 3 | #1 (tool-trace timing) |
| 5 | Per-subject analytics dashboard | 4 | #1 (perf-event table), feedback |
| 4 | RAG evaluation harness (LLM-judged) | 5 | query pipeline; benefits from #1 |
| 10 | Chat slash commands | 6 | #2 (`/filter`), #6 (`/new`, `/rename`) |
| 11 | Broadened MCP management surface | 7 | all of the above exist to expose |

**Recommended sequence:** 1 → 2 → 6 → 5 → 4 → 10 → 11. #1 is the keystone (it completes the reworked
history modal and feeds analytics). #2 and #10's `/filter` pair up. #11 comes last so every new surface has
something to expose.

---

## Phase 0 — Cross-cutting conventions & compliance (read before starting any feature)

These are **non-negotiable** and mirror `C:\code\agents\requirements` (which wins on any conflict) and the
repo `CLAUDE.md`. Every feature below inherits this checklist; treat it as the definition of done for code
style.

### Backend (C#) — apply to every new file/method
- [ ] No `var`; no tuples (return typed DTOs / set response state); one class or enum per file; no partial classes.
- [ ] `namespace` first, `using` directives **inside** the namespace (System/Microsoft first alphabetically, then others).
- [ ] XML docs on every public class, constructor, property, method, enum; `/// <exception>` on thrown types. No doc comments on private members.
- [ ] Public members `LikeThis`; private `_LikeThis`. Properties with range/null rules use explicit get/set over a backing field; `ArgumentNullException` for required nulls; `Math.Clamp` numeric ranges.
- [ ] Every async method doing cancellable work takes a `CancellationToken`; every `await` in library/server code uses `.ConfigureAwait(false)`.
- [ ] Classic `using (...) { }` blocks; specific exception types with meaningful messages; SyslogLogging (no `Console.WriteLine` in library code).
- [ ] No `System.Text.Json` DOM types for fixed contracts — typed DTOs; JSON columns only for genuinely schemaless data, named `*Json`.
- [ ] Region order where used: `Public-Members`, `Private-Members`, `Constructors-and-Factories`, `Public-Methods`, `Private-Methods`.
- [ ] Keep each `.cs` ≤ 500 lines and each `.jsx/.js` ≤ 400 lines (CI `scripts/check-file-sizes.sh`); split rather than allowlist unless a single cohesive class genuinely warrants it.

### Data layer & multi-tenancy
- [ ] Provider-neutral: add the interface `I{Entity}Methods`, then implementations under each of `Sqlite/`, `Postgresql/`, `Mysql/`, `SqlServer/` (`Implementations/` + `Queries/`), handwritten SQL via `Sanitizer.*` and `RowReader.*`.
- [ ] Structured columns / child tables — never BLOB-as-JSON for known shapes; JSON columns only for schemaless payloads, suffixed `Json`.
- [ ] Every tenant-owned entity carries `TenantId`; all queries scope by tenant; composite unique indexes `(tenant_id, …)`.
- [ ] New tables/columns land as **versioned, idempotent, tracked** migrations in all four `{Provider}Schema.cs` (current head is **v10**; new work starts at **v11**), honoring dialect differences (Postgres `IF NOT EXISTS`, MySQL plain `ADD COLUMN`, SqlServer `IF COL_LENGTH(...) IS NULL`, Sqlite `ADD COLUMN`). First-boot seeding stays idempotent.
- [ ] SQLite writes serialized with the existing `SemaphoreSlim` discipline.
- [ ] New IDs use `Helpers/IdGenerator.cs` PrettyId prefixes centralized in `Constants.cs` (new prefixes proposed per feature below).

### MCP
- [ ] Every new MCP tool is registered in `McpToolCatalog.cs`, dispatched via `McpToolInvoker`, and mapped in `McpToolAuthorization.cs` (tool → ResourceType/Operation) so the MCP transport and the in-process `PneumaToolExecutor` cannot drift.
- [ ] Enumerations use `EnumerationQuery`/`EnumerationResult` (nothing unbounded); reads are RBAC-gated and audited like REST.

### Frontend (all three dashboards where the role warrants)
- [ ] React 19 + Vite, hand-rolled `ApiClient` (no axios), i18next runtime strings, custom confirm modals (never browser `confirm`).
- [ ] Hand-rolled SVG for any chart (no charting library). Light/dark themes; responsive at 1280 / 768 / 390. Full tooltip coverage on new controls.
- [ ] Reuse shared components (`DataTable`, `Modal`/`ConfirmModal`, `CopyButton`/`CopyableId`, `ActivityChart`, `ResourceView`) rather than re-implementing.

### Docs / API surface parity (per feature)
- [ ] `REST_API.md`, `MCP_API.md`, `Pneuma.postman_collection.json`, C# SDK (`sdk/csharp`), JS SDK (`sdk/js/src/index.js`), and `CHANGELOG.md` updated in lockstep with the backend.
- [ ] `PNEUMA_PLAN.md` checkbox(es) updated if the feature maps to a planned phase.

### Tests
- [ ] Extend the Touchstone suites in `Test.Shared/Suites/` (Database/Api/Collections/etc.); new DB columns/tables get round-trip coverage in `DatabaseSuite.cs` across providers; new HTTP/MCP surface gets `ApiSuite.cs` coverage. All suites green via `dotnet run --project src/Test.Automated`.

---

## Phase 1 — #1 Structured per-stage performance telemetry  ✅

> **Status note:** backend, migration v11 (all four providers), data layer, `AgenticChatService`
> instrumentation, retention/cascade pruning, both History detail stage tables, tests (92 pass), and docs are
> done. The MCP `pneuma_get_history_turn` tool is folded into **Phase 7** (MCP broadening) so all history/
> telemetry MCP tools land together.

**Goal:** capture, persist, and surface ordered per-stage timing/token telemetry for every answered turn
(grounded `/v1.0/query` and agentic `/v1.0/chat/stream`), so the reworked history modal shows a stage table
and analytics (#5) can aggregate efficiently.

### Data model & migration (v11)
- [ ] New DTOs (typed, not JSON DOM), one class per file, under `Pneuma.Core/Models/`:
      `TurnPerformance.cs` (schema version + `List<TurnPerformanceStage>`), `TurnPerformanceStage.cs`
      (stage name/kind, endpointId, provider, model, `DurationMs`, success, token in/out, optional
      `ClientTimings`/`ProviderMetrics`), `TurnPerformanceClientTimings.cs`, `TurnPerformanceProviderMetrics.cs`.
- [ ] `ChatTurnRecord.cs`: add `PerformanceJson` (schemaless payload → `*Json` naming) and
      `PerformanceSchemaVersion` (int).
- [ ] New child table `chatturnperfevents` (one row per stage, columns: id `perf_` prefix, tenant_id,
      turn_id, subject_id, stage, kind, endpoint_id, provider, model, duration_ms, queue_ms, request_to_headers_ms,
      headers_to_first_token_ms, first_to_last_token_ms, prompt_tokens, completion_tokens, success, created_utc),
      indexed `(tenant_id, subject_id, created_utc)` and `(turn_id)`.
- [ ] Migration **v11** in all four `{Provider}Schema.cs`: `ALTER TABLE chatturns ADD` the two columns +
      `CREATE TABLE chatturnperfevents` (dialect-correct).
- [ ] Data layer: `IChatTurnPerfEventMethods` + four `Implementations/ChatTurnPerfEventMethods.cs` +
      `Queries/`; extend `ChatTurnMethods` INSERT/UPDATE/Map for the two new columns.
- [ ] `Constants.cs`: add `perf_` PrettyId prefix; wire into `IdGenerator.cs`.

### Backend — emit telemetry
- [ ] `GroundedQueryService.cs`: instrument the existing discrete calls (embed query, retrieve, prompt-rewrite,
      rerank, generate) with a lightweight stopwatch collector; build `TurnPerformance` and hand it to the turn writer.
- [ ] `AgenticChatService.cs`: same for the agentic path (rewrite, per-tool-iteration model calls, generation),
      folding provider metrics where the model client exposes them.
- [ ] Persist: on turn completion, serialize `PerformanceJson`/`PerformanceSchemaVersion` on the `ChatTurnRecord`
      **and** write one `chatturnperfevents` row per stage (best-effort; a telemetry failure must never fail the answer).
- [ ] Surface the full `TurnPerformance` on the SSE completion event and on `GET /v1.0/history/{id}` via `ChatTurnDetail.cs`.

### MCP / REST / SDK / docs / Postman
- [ ] `GET /v1.0/history/{id}` response documented with the new telemetry object in `REST_API.md`.
- [ ] MCP `pneuma_get_history_turn` (add if absent) returns the telemetry; document in `MCP_API.md`.
- [ ] C# SDK models + JS SDK JSDoc gain the telemetry shape; Postman "Get History Turn" example refreshed.
- [ ] `CHANGELOG.md` entry.

### Dashboards
- [ ] admin + subject `HistoryDetailModal.jsx`: light up a **Stage details table** (stage / endpoint / duration /
      queue / headers / first-token / generation / tokens) from `performanceJson`, and drive the existing timing
      bars from real per-stage data instead of the coarse scalars. Keep the graceful "coarse-only" fallback for
      turns recorded before v11.
- [ ] Tooltip every column; copy-to-clipboard on the raw telemetry JSON.

### Tests
- [ ] `DatabaseSuite.cs`: perf-event round-trip across all four providers; `ChatTurnRecord` telemetry columns round-trip.
- [ ] `ApiSuite.cs`: a query/chat turn produces a `PerformanceJson` with the expected ordered stages and a matching perf-event count.

### Acceptance — #1
- [ ] A fresh answered turn shows a populated stage table + accurate timing bars in both history modals.
- [ ] Perf-event rows are queryable per subject/time (verified by a suite query).
- [ ] Telemetry failure is non-fatal (fault-injection test or manual verification).

---

## Phase 2 — #2 Metadata / facet retrieval filters  ✅

> **Status note:** scoped to chunk-**tag** predicates (Pneuma has no separate "labels" concept). Backend is
> complete: `RetrievalFilter`/`RetrievalTagCondition`/`TagConditionEnum`, native push-down through
> `IVectorRepository`/`IInvertedIndex` → RecallDB `TagFilter` (+ fake), subject default
> `retrievalFilterJson` + per-request `metadataFilter` on `/v1.0/query` merged (union), applied on the
> grounded and agentic search paths, effective filter persisted on the turn, migration v12 (all four
> providers), tests (93 pass), docs. Subject forms (admin + creator) get a JSON filter editor with
> validation; the History modal shows the applied filter as chips. **Deferred to a follow-up:** the
> discovery endpoints (distinct labels/tags) and the visual Ask-composer filter builder + MCP
> `metadataFilter` on `pneuma_query`/`pneuma_search` (folded into Phase 7).

**Goal:** let a query constrain retrieval by required/excluded **labels** and **tag conditions** over the tags
Pneuma already stores on chunks/nodes (`rights`, `authority`, `confidence`, `nodeType`, `documentType`,
`sourceUrl`, `subjectId`, `jobId`, …), as a per-request filter merged with an optional per-subject default.

### Data model & migration (v12)
- [ ] DTOs under `Pneuma.Core/Requests/`: `RetrievalFilter.cs` (required/excluded label lists +
      `List<RetrievalTagCondition>`), `RetrievalTagCondition.cs` (key, `TagConditionEnum`, value), enum
      `Enums/TagConditionEnum.cs` (Equals/NotEquals/Contains/StartsWith/EndsWith/GreaterThan/LessThan/IsNull/IsNotNull).
- [ ] `Subject.cs`: add nullable `RetrievalFilterJson` (subject default filter).
- [ ] `ChatTurnRecord.cs`: add `RetrievalFilterJson` (the **effective** filter used, for history/audit).
- [ ] Migration **v12**: `ALTER TABLE subjects ADD retrievalfilterjson`; `ALTER TABLE chatturns ADD retrievalfilterjson`.
- [ ] Extend `SubjectMethods` + `ChatTurnMethods` (4 providers) INSERT/UPDATE/Map.

### Backend
- [ ] `GroundedQueryService.RetrieveSourcesAsync`: translate the merged filter into the RecallDB
      `TagFilter`/search request (extend the existing `subjectId` tag filter), honoring all nine operators and
      label include/exclude; merge subject-default + per-request (union of required, union of excluded).
- [ ] Add filter params to the query/chat request DTOs (`/v1.0/query`, `/v1.0/query/stream`, `/v1.0/chat/stream`) and to `McpGraphTools.SearchAsync` / `PneumaToolExecutor`.
- [ ] Persist the effective filter on the turn; return it on history detail.
- [ ] **Facet discovery endpoints:** `GET /v1.0/subjects/{id}/retrieval/labels` and `.../tags` returning distinct label values and tag keys/values from the subject's collection (proxied from RecallDB), to power the UI.

### MCP / REST / SDK / docs / Postman
- [ ] MCP: add `metadataFilter` to `pneuma_query`/`pneuma_search`; add `pneuma_distinct_labels` / `pneuma_distinct_tags` tools (RBAC-mapped).
- [ ] `REST_API.md` (query bodies + discovery endpoints + subject `retrievalFilter`), `MCP_API.md`, Postman bodies, both SDKs, `CHANGELOG.md`.

### Dashboards
- [ ] **Subject config** (admin `SubjectsView.jsx` formFields + subject `SubjectsView.jsx` modal): a filter builder
      for the subject default (required/excluded label chips + tag-condition rows), fully tooltipped.
- [ ] **Ask views** (admin / subject / user): a compact "Filters" control on the composer opening a
      `MetadataFilterModal` (label checkboxes sourced from the discovery endpoint + tag-condition rows); active
      filter shown as removable chips above the input.
- [ ] **History modal**: render the effective filter that was applied (labels colored, excluded struck through; tag predicates monospace).

### Tests
- [ ] `CollectionsSuite.cs`/`ApiSuite.cs`: retrieval honors required/excluded labels and each tag operator; effective filter persists on the turn; discovery endpoints return distinct values.
- [ ] `DatabaseSuite.cs`: subject + turn `retrievalfilterjson` round-trip (4 providers).

### Acceptance — #2
- [ ] A query with a required tag returns only matching chunks; an excluded label removes matching chunks.
- [ ] Subject default + per-request filters merge as specified; the effective filter is visible in history.

---

## Phase 3 — #6 Conversation threads (full) + persisted tool-call trace  ✅

> **Status note:** backend complete — `chatthreads` + `chattoolcalls` tables (migration v13, all four
> providers), thread CRUD at `/v1.0/threads` (list/create/get-with-turns/rename/delete + cascade),
> `threadId` on turns + history filter, `AgenticChatService` creates/continues a thread per turn (id on the
> `complete` event), auto-title from the first question, and the tool-call trace persisted + returned on
> `GET /v1.0/history/{id}`. All three dashboards thread the `threadId` through chat (a conversation stays in
> one thread; "clear" starts a new one) and render the persisted tool trace in the History detail modal.
> Tests (94 pass), REST_API + CHANGELOG updated. **Deferred to a follow-up:** the visual thread
> switcher/sidebar + rename/delete UI in the Ask views and thread-grouping in the History list, and the MCP
> thread tools (folded into Phase 7).

**Goal:** group turns into named conversations with a thread switcher and "new conversation" UX in all three
Ask views, auto-generated titles, and per-thread history; and persist the agentic tool-call trace (currently
only streamed live) so it appears in the history modal.

### Data model & migration (v13)
- [ ] New table `chatthreads` (id `thr_` prefix, tenant_id, subject_id, user_id, title, created_utc,
      last_activity_utc), indexed `(tenant_id, subject_id, last_activity_utc)`.
- [ ] `ChatTurnRecord.cs`: add `ThreadId` (nullable for back-compat) + optional `SequenceInThread`.
- [ ] New table `chattoolcalls` (id `tcall_` prefix, tenant_id, turn_id, thread_id, subject_id, tool_name,
      arguments_json, output_json, success, denied, truncated, bytes, duration_ms, iteration, sequence,
      provider, model, created_utc), indexed `(turn_id)` and `(tenant_id, subject_id, created_utc)`.
- [ ] Migration **v13**: create both tables + `ALTER TABLE chatturns ADD threadid, sequenceinthread`.
- [ ] Data layer: `IChatThreadMethods` + `IChatToolCallMethods` (+ four impls each + Queries); extend `ChatTurnMethods` for `ThreadId`.
- [ ] `Constants.cs`: `thr_` + `tcall_` prefixes.

### Backend
- [ ] `AgenticChatService.cs`: accept an optional `threadId` (create-on-first-turn when absent), stamp turns
      with it, and **persist each tool call** to `chattoolcalls` (args/output persisted subject to a size cap;
      never fail the turn on a persistence error).
- [ ] Auto-title: after the first turn of a new thread, generate a short title via the subject's inference model (best-effort; falls back to a truncated first question).
- [ ] Routes (`ChatHistoryRoutes.cs` or new `ThreadRoutes.cs`): `GET /v1.0/threads` (paged, subject filter),
      `GET /v1.0/threads/{id}` (thread + its turns), `POST /v1.0/threads` (create), `PATCH /v1.0/threads/{id}`
      (rename), `DELETE /v1.0/threads/{id}` (cascade its turns + tool calls + feedback). `GET /v1.0/history` gains a `threadId` filter.
- [ ] `ChatTurnDetail.cs`: include the persisted tool-call trace.

### MCP / REST / SDK / docs / Postman
- [ ] MCP: `pneuma_enumerate_threads`, `pneuma_get_thread`, `pneuma_delete_thread` (RBAC-mapped; writes gated like subject writes). `pneuma_get_history_turn` returns the tool-call trace.
- [ ] `REST_API.md` (threads CRUD + `threadId` on chat), `MCP_API.md`, Postman, both SDKs, `CHANGELOG.md`.

### Dashboards (all three Ask surfaces)
- [ ] Thread sidebar/switcher: list conversations for the subject (title + last-activity), select to rehydrate,
      "New conversation" button, inline rename, delete-with-confirm. On the **user** dashboard keep it minimal
      (recent conversations for the current subject) consistent with its lighter chrome.
- [ ] Ask views send/receive `threadId`; the composer starts a new thread when none is selected.
- [ ] admin + subject **History** view: add a Thread column/filter and group turns by thread.
- [ ] **HistoryDetailModal**: a "Tool activity" section (tool / status / results / runtime, expandable args/output JSON with copy) fed by the persisted trace.

### Tests
- [ ] `DatabaseSuite.cs`: thread + tool-call round-trip and cascade delete (thread → turns → tool calls → feedback) across providers.
- [ ] `ApiSuite.cs`: a multi-turn chat reuses one thread; thread list/rename/delete; tool-call trace persists and is returned on history detail.

### Acceptance — #6
- [ ] Sending two turns without changing threads keeps them in one titled conversation; "New conversation" starts a fresh one.
- [ ] Deleting a thread removes its turns, tool calls, and feedback; nothing orphaned (verified by suite).
- [ ] An agentic turn's tool calls are visible in the history modal after the fact.

---

## Phase 4 — #5 Per-subject analytics dashboard  ✅

> **Status note:** `AnalyticsService` aggregates turns + v11 perf events + feedback into a windowed
> `AnalyticsReport` (overview with p50/p95/p99, daily time series, per-stage avg/p95), served at
> `GET /v1.0/analytics`. An **Analytics** view lands in the admin and creator dashboards with hand-rolled SVG
> charts (volume-per-day, per-stage latency) + metric tiles, wired into nav. Tests (95 pass), REST_API +
> CHANGELOG updated. MCP analytics tools fold into Phase 7.

**Goal:** per-subject observability rollups (latency percentiles, success/failure, throughput, per-stage and
per-endpoint timing, rerank/rewrite/gate counts, feedback trends) rendered with hand-rolled SVG charts.

### Backend
- [ ] `AnalyticsService.cs` (new) computing rollups over `chatturnperfevents` (#1) + `chatturns` + `chatfeedback`,
      tenant + subject + time-range scoped: overview (counts, success rate, avg/p50/p95/p99 latency, avg
      tokens/tok-per-sec), timeseries (bucketed), per-stage averages, per-endpoint averages, feedback up/down over time.
- [ ] Routes (`AnalyticsRoutes.cs`): `GET /v1.0/analytics/overview`, `.../timeseries`, `.../stages`,
      `.../endpoints`, `.../feedback` — all `subjectId` + `from`/`to` scoped; typed response DTOs (no tuples).
- [ ] Percentile SQL written per-provider (dialect-aware), or computed in-service from a bounded fetch where a provider lacks native percentiles — documented either way.

### MCP / REST / SDK / docs / Postman
- [ ] MCP: `pneuma_analytics_overview` / `_timeseries` / `_stages` / `_endpoints` / `_feedback` (read, RBAC-mapped).
- [ ] `REST_API.md`, `MCP_API.md`, Postman, both SDKs, `CHANGELOG.md`.

### Dashboards (admin + subject)
- [ ] New **Analytics** view: subject selector + time-range control (reuse the extracted range selector from the
      Request/Ingestion activity work); metric tiles; hand-rolled SVG **bar** and **line** charts (extend
      `ActivityChart.jsx` patterns) for latency percentiles, request/success/failure, throughput, per-stage timing, feedback trend.
- [ ] Copy-to-PNG + refresh on each chart, consistent with existing activity charts; auto-refresh via the shared interval control.

### Tests
- [ ] `ApiSuite.cs`: seed several turns/feedback, assert overview counts/percentiles and timeseries buckets are correct and tenant/subject-scoped.

### Acceptance — #5
- [ ] The Analytics view renders real rollups for a subject with traffic and an empty-state for one without.
- [ ] Percentiles and counts match a hand-computed fixture in the suite.

---

## Phase 5 — #4 RAG evaluation harness (LLM-judged)  ✅

> **Status note:** `evalfacts`/`evalruns`/`evalresults` (migration v14, all four providers), `EvalService`
> answers each fact through the real grounded pipeline and LLM-judges it with a seeded `eval.judge` prompt
> (verdict/score/reason/failure-mode), REST at `/v1.0/eval/facts` + `/v1.0/eval/runs`, an Evaluation view in
> the admin + creator dashboards (fact management, start run, results modal), subject-cascade cleanup, tests
> (96 pass), REST_API + CHANGELOG. **Deferred to a follow-up:** runs are **synchronous** (SSE live progress
> + a background/cancellable worker) and MCP eval tools (Phase 7).

**Goal:** ground-truth facts per subject, eval runs over the **real** answer pipeline, per-fact LLM-judge
verdicts, category/failure-mode filtering, SSE live progress, and an Eval dashboard view.

### Data model & migration (v14)
- [ ] Tables: `evalfacts` (id `efact_`, tenant_id, subject_id, question, expected_answer, category, created_utc),
      `evalruns` (id `erun_`, tenant_id, subject_id, status, mode, started_utc, finished_utc, totals/pass/fail counts, judge_model),
      `evalresults` (id `eres_`, tenant_id, run_id, fact_id, produced_answer, verdict, score, reason, failure_mode, trace/turn ids, created_utc).
- [ ] Migration **v14** (4 providers); data layer `IEvalFactMethods` / `IEvalRunMethods` / `IEvalResultMethods` (+ impls + Queries).
- [ ] `Constants.cs`: `efact_` / `erun_` / `eres_` prefixes.

### Backend
- [ ] `EvalService.cs` (new): run a set of facts through `GroundedQueryService` (subject's real models/filters),
      then judge each produced answer against `expected_answer` with a judge prompt (new seeded global prompt
      `eval.judge` in `FirstBootSeeder.cs`), producing verdict + score + reason + failure_mode.
- [ ] Routes (`EvalRoutes.cs`): facts CRUD (`/v1.0/eval/facts`), runs (`POST /v1.0/eval/runs` to start,
      `GET` list/detail, `DELETE`), results (`GET /v1.0/eval/runs/{id}/results` with category/failure-mode filters),
      and **SSE progress** (`GET /v1.0/eval/runs/{id}/stream`) reusing `SseWriter`.
- [ ] Runs execute on a bounded worker (respect the model-runner admission gate); status transitions persisted; a run is resumable/cancellable like ingestion jobs.

### MCP / REST / SDK / docs / Postman
- [ ] MCP: `pneuma_eval_fact_*` (CRUD), `pneuma_eval_run_*` (start/list/get/delete), `pneuma_eval_results` — RBAC-mapped (writes gated).
- [ ] `REST_API.md`, `MCP_API.md`, Postman (facts + start-run + results), both SDKs, `CHANGELOG.md`, and a `PNEUMA_PLAN.md` checkbox.

### Dashboards (admin + subject)
- [ ] **Eval** view: manage ground-truth facts (table + create/edit/bulk), start a run (subject + category filter
      + mode), a **live progress modal** (SSE) with per-fact pass/fail streaming, a results view (verdict/score/
      reason/failure-mode, filterable), and delete/bulk-delete of runs. Link each result to its history turn/trace.

### Tests
- [ ] `DatabaseSuite.cs`: facts/runs/results round-trip + cascade (run → results) across providers.
- [ ] `ApiSuite.cs`: seed facts, start a run against a fake pipeline, assert verdicts persist and SSE emits progress; results filter by category/failure-mode.

### Acceptance — #4
- [ ] A run over N facts yields N results with verdicts and an aggregate pass rate; SSE shows live progress; cancel works.
- [ ] Results link back to the exact turn/trace that produced each answer.

---

## Phase 6 — #10 Chat slash commands  ✅

> **Status note:** all three Ask views intercept `/` commands client-side (never sent to the model):
> `/help` (`/?`), `/clear` / `/new` (start a new conversation — resets the thread), `/context` (context
> usage from the last turn), and `/compact` (informational; compaction is automatic). Unknown commands show
> help. The user dashboard exposes the safe subset. `/filter` is deferred with the Phase 2 Ask-composer
> filter builder.

**Goal:** in-composer slash commands across all three Ask views. Frontend-only (reuses existing endpoints).

- [ ] Command parser in the shared Ask composer logic: `/help` (`/?`), `/clear` (new empty thread — pairs with #6),
      `/new [title]` (start thread), `/rename <title>` (#6), `/compact` (force the existing compaction path),
      `/context` (render a context-usage table from the current stats), `/filter` (open the #2 metadata-filter modal).
- [ ] Autocomplete/hint menu on `/`; unknown command → inline help; commands never sent to the model as a question.
- [ ] Apply consistently in admin, subject, and user Ask views (user dashboard: expose the safe subset — `/help`, `/clear`, `/new`, `/context`).
- [ ] i18n strings for all command labels/help; tooltips on the hint menu.

### Tests / validation
- [ ] Manual: each command behaves as specified in each dashboard; `/context` matches the stats popover; `/filter` opens the builder from #2.

### Acceptance — #10
- [ ] Typing `/` shows the command menu; each command performs its action; unknown commands show help without hitting the model.

---

## Phase 7 — #11 Broadened MCP management surface  ✅

> **Status note:** six new read tools added (`pneuma_get_history_turn`, `pneuma_enumerate_threads`,
> `pneuma_enumerate_feedback`, `pneuma_analytics`, `pneuma_enumerate_eval_runs`, `pneuma_get_eval_run`),
> registered in the catalog, dispatched via the invoker, and RBAC-mapped (Subject/Read) so MCP matches REST.
> Tests + MCP_API.md + CHANGELOG updated (96 pass). This also lands the MCP surface deferred from Phases 1–5.
> The facet `metadataFilter` on `pneuma_query`/`pneuma_search` and eval/thread **write** tools over MCP remain
> a follow-up.

**Goal:** make Pneuma fully operable by an agent — expose the new and existing management/read surfaces over MCP
without drifting from REST authorization.

- [ ] Add read/management tools mirroring REST for: analytics (#5), threads + history turn detail with telemetry
      + tool-trace (#1/#6), feedback (list/get), request-history (list/get/summary), settings/config (redacted),
      model-runner/endpoint health, and eval (#4). Each registered in `McpToolCatalog.cs`, dispatched via
      `McpToolInvoker`, and mapped in `McpToolAuthorization.cs`.
- [ ] Keep secret redaction on any config/credential-bearing tool (default redacted); enumerations bounded via `EnumerationQuery`/`EnumerationResult`.
- [ ] Decide per tool whether it also joins the in-process **assistant** tool surface (`BuildAssistantToolDefinitions` / `PneumaToolExecutor`) — analytics/history reads yes; writes no.
- [ ] `MCP_API.md` full tool matrix updated; Postman MCP examples added; `CHANGELOG.md`.

### Tests
- [ ] `ApiSuite.cs` MCP block: each new tool lists in `tools/list`, enforces RBAC (403 for under-privileged), and returns the same shape as its REST twin.

### Acceptance — #11
- [ ] Every new REST management/read surface has an MCP twin with matching auth; redaction verified; nothing unbounded.

---

## Phase 7.5 — Telemetry & Grafana (full metrics + traces across ingestion and retrieval)  ✅

> **Status note:** ingestion already had per-stage metrics + spans; added retrieval/answer metrics
> (`pneuma_chat_answers_total`, `pneuma_chat_answer_duration_seconds`, `pneuma_chat_stage_duration_seconds`)
> wired from `AgenticChatService`. The Grafana dashboard (both `assets/grafana` and `docker/factory`) gains a
> **Retrieval & Answer** domain row (answer rate, p95, per-stage p95) — it was already sectioned into
> Overview/HTTP/Ingestion/Integrations. **TELEMETRY.md** written (metrics+traces inventory, exposure,
> collection, Grafana access, reading each section + traces, workflows). Tests green (96). **Follow-up:**
> explicit OTLP spans on each answer stage (answer path is currently metered + persisted per-turn; ingestion
> already has spans).

**Goal:** metrics (meters) and distributed traces cover **every** ingestion and retrieval/answer step and are
exposed (`/metrics` + OTLP), collected (Prometheus + Tempo), and displayed in **domain-sectioned** Grafana
dashboards. Ship a `TELEMETRY.md` operator guide.

### Instrumentation (backend)
- [ ] **Metrics — ingestion:** ensure `PneumaMetrics` exposes counters/histograms for every pipeline stage
      (ContentRetrieval, TypeDetection, CellExtraction, Classification, GraphMerge, Summarization, Chunking,
      Embedding, Indexing): stage duration histogram, stage outcome counter (ok/failed/queued), queue-wait
      histogram, and job lifecycle counters (started/completed/failed/cancelled). Add content-bytes and
      cells/chunks/embeddings produced as counters where missing.
- [ ] **Metrics — retrieval/answer:** counters/histograms for each answer stage (prompt-rewrite, retrieval
      [vector/full-text], rerank, neighbor-expansion, final inference), request outcome (ok/insufficient/4xx/429),
      tokens in/out, tokens/sec, and per-integration call latency (RecallDB, Partio, LiteGraph, DocumentAtom).
- [ ] **Traces:** confirm a span wraps each ingestion stage (already partially present via `TelemetryService`)
      and add spans for each retrieval/answer stage and each outbound integration call, with tenant/subject/job
      tags. Every `/v1.0/query`, `/v1.0/chat/stream`, and MCP answer path is a root span with child stage spans.
- [ ] Verify metric names/labels are stable and low-cardinality (no ids in labels; tenant/subject only where bounded).

### Collection / config (docker + docker/factory)
- [ ] Confirm Prometheus scrapes Pneuma `/metrics` and Tempo receives OTLP; update `prometheus.yaml`/`tempo.yaml`
      scrape/receiver config if any new endpoint or job is needed.
- [ ] Provision datasources (Prometheus + Tempo) and dashboards via Grafana provisioning in both `docker/` and
      `docker/factory/`.

### Grafana dashboards — sectioned by domain
- [ ] Rebuild the observability dashboard(s) into **domain sections** (Grafana rows): **Overview** (uptime,
      request rate, error rate, p95), **Ingestion** (per-stage duration/throughput/queue-wait, job outcomes,
      contention), **Retrieval & Answer** (per-stage latency, tokens/sec, rerank/rewrite usage, outcomes),
      **Integrations** (RecallDB/Partio/LiteGraph/DocumentAtom latency + error rate), **Traces** (Tempo
      trace-search panel linked from the metrics). Store the JSON model under `assets/grafana/` and both
      `docker/` + `docker/factory/` provisioning paths.
- [ ] Ensure exemplars / trace-to-metrics linking where supported so an operator can jump from a slow metric to
      the trace.

### Docs
- [ ] Write **`TELEMETRY.md`**: what telemetry Pneuma emits (metrics + traces inventory by domain), how it is
      exposed (`/metrics`, OTLP) and collected (Prometheus/Tempo), how to access Grafana (URL, default creds,
      the sectioned dashboards), and how to use the data (reading each section, finding a slow ingestion stage,
      tracing a slow answer, spotting integration errors). Link it from `README`/`PNEUMA_PLAN.md`.

### Acceptance — Telemetry
- [ ] Every ingestion and retrieval stage shows up as both a metric and a span; the Grafana dashboard has the
      domain sections above populated with live data after a rebuild; `TELEMETRY.md` walks an operator end-to-end.

---

## Phase 8 — Cross-cutting validation: usability, design, aesthetics, compliance (run at the end)

Do not close the project until every box here passes. This is the "does it look and feel right, and does it
comply" gate.

### Build / quality gates
- [ ] `dotnet build src/Pneuma.sln` — 0 errors, no new warnings.
- [ ] `dotnet run --project src/Test.Automated` — all suites pass (all four providers exercised where the shared DB contract suites run).
- [ ] Each dashboard: `npm ci && npm run lint && npm run build` clean.
- [ ] `bash scripts/check-file-sizes.sh` passes (split any file over threshold rather than allowlisting, unless a single cohesive class truly warrants an entry).
- [ ] Full CI green on the branch (backend, live-PostgreSQL, all three dashboards, file-size guardrail).

### Usability & design review (per new/changed surface)
- [ ] **Responsive**: every new view/modal verified at 1280 / 768 / 390; no horizontal page scroll; wide tables/charts scroll within their own container.
- [ ] **Theming**: correct in both light and dark; no hard-coded colors that break a theme; charts legible in both.
- [ ] **Tooltips**: every new control, column header, and metric has a hover tooltip explaining it (match the coverage density of Assistant/Subject settings).
- [ ] **Copy affordances**: IDs, JSON blocks, question/answer, filter values are copyable via `CopyButton`/`CopyableId`.
- [ ] **Confirm modals**: all destructive actions (delete thread, delete eval run, clear conversation) use the custom `ConfirmModal`, never browser `confirm`.
- [ ] **Empty / loading / error states**: every new list and chart has a designed empty state, a loading state, and an error banner with retry.
- [ ] **i18n**: no hard-coded user-facing strings; all new strings go through i18next with sensible English defaults.
- [ ] **Consistency**: new views adopt the existing `PageHeader`, `DataTable` (with auto-refresh + persisted interval), filter-bar, and modal patterns; spacing/typography match neighboring views.
- [ ] **Aesthetics pass**: a deliberate walkthrough of the subject-config form, the Ask thread switcher + filter chips, the history stage table + tool trace, the analytics charts, and the eval run/results — grouped sensibly, visually balanced, and pleasant. Fix anything that reads as cramped or bolted-on.

### Accessibility & interaction
- [ ] Keyboard: modals close on Esc; the slash-command menu is keyboard navigable; focus is trapped in modals and restored on close (re-verify the earlier feedback-textbox focus fix pattern is honored).
- [ ] `aria-label`s on icon-only buttons (refresh, copy, filter, thread actions).

### Documentation & contract parity (final sweep)
- [ ] `REST_API.md`, `MCP_API.md`, `Pneuma.postman_collection.json`, `sdk/csharp`, `sdk/js/src/index.js`, `CHANGELOG.md`, and `PNEUMA_PLAN.md` reflect the final shipped surface (re-diff against the code, not the plan).
- [ ] `docker/` and `docker/factory/` assets updated for any new seeded prompt (e.g. `eval.judge`) or config; confirm the live deployment self-seeds new prompts on restart.

### `C:\code\agents\requirements` compliance sign-off
- [ ] Re-read the requirement documents and confirm each new backend file honors: no `var`, no tuples, one class/enum per file, usings-inside-namespace ordering, full XML docs, `ConfigureAwait(false)`, `CancellationToken` on cancellable async, guard clauses, provider-neutral handwritten SQL across all four providers, tenant scoping + composite unique indexes, versioned/idempotent/tracked migrations, PrettyId prefixes, request-capture/redaction, `127.0.0.1` (never `localhost`), and RBAC + audit on every new route and MCP tool (explicit deny > permit > implicit deny).
- [ ] Where this plan and the requirement documents disagree, the requirement documents win — reconcile and note any deviation.

---

## Progress tracker (fill in as phases complete)

| Phase | Feature | Backend | Data/migration | MCP | REST/SDK/Postman/docs | Dashboards | Tests | Validated |
|---|---|---|---|---|---|---|---|---|
| 1 | #1 Telemetry | ✅ | ✅ | 🟨 (Phase 7) | ✅ | ✅ | ✅ | 🟨 |
| 2 | #2 Facet filters | ✅ | ✅ | 🟨 (Phase 7) | ✅ | ✅ (JSON editor) | ✅ | 🟨 |
| 3 | #6 Threads + tool trace | ✅ | ✅ | 🟨 (Phase 7) | ✅ | ✅ (trace + threading; switcher deferred) | ✅ | 🟨 |
| 4 | #5 Analytics | ✅ | n/a | 🟨 (Phase 7) | ✅ | ✅ | ✅ | 🟨 |
| 5 | #4 Eval harness | ✅ (sync) | ✅ | 🟨 (Phase 7) | ✅ | ✅ | ✅ | 🟨 |
| 6 | #10 Slash commands | n/a | n/a | n/a | ✅ | ✅ (all 3 Ask views) | n/a | 🟨 |
| 7 | #11 MCP surface | ✅ | n/a | ✅ | ✅ | n/a | ✅ | 🟨 |
| 7.5 | Telemetry & Grafana | ✅ (metrics) | n/a | n/a | ✅ (TELEMETRY.md) | ✅ (Grafana row) | ✅ | 🟨 |
