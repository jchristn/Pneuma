# Partio Removal — Native Processing in Pneuma

**Target release:** `0.3.0` (MINOR bump from the current `0.2.0` in the csprojs).
**Status:** Not started.
**Owner:** _unassigned_

Pneuma delegates chunking, embedding, and summarization to Partio and treats Partio as the system of record for model endpoints. This plan brings all of that in-house so Pneuma owns the processing tier end to end, deletes the Partio server and dashboard from the stack, and — the recurring operational pain — removes the second service whose endpoint health and config drift out of sync with Pneuma's. Completions already run direct to the provider, so this is finishing a job that's half done, not starting a new one.

The work is tractable because the seam already exists. `ISemanticProcessor` (chunk / embed / summarize / process) is a Pneuma abstraction; `IPartioClient : ISemanticProcessor` only adds endpoint administration on top. Ingestion and retrieval depend on those interfaces, not on Partio, so most of this plan swaps implementations behind interfaces the call sites already use.

---

## How to use this document

Every task is a checkbox. Annotate in place:

- `- [ ]` not started · `- [~] (abc 09-20)` in progress · `- [x] (abc 09-21)` done — initials and date in parentheses.
- When a task spawns follow-ups, nest them beneath it.
- Keep the **Product-surface coverage** matrix (§7) honest — it is the definition of "full-product-facing" and the release gate.

Out of scope (tracked elsewhere): the `#5`/`#12` ingestion **step-split** in `CKG_IMPROVEMENTS.md` (making ontology canonicalization and relationship consolidation their own timed stages) is a separate, paused effort. It does not block this plan and should not be folded in.

---

## 1. Locked decisions

The following were decided at planning time and are binding for `0.3.0`. The overriding goal: **Partio ceases to be a component of Pneuma — every function is fully absorbed, manageable end to end across all surfaces, well documented, and well tested.**

- [x] **Scope — full removal.** Endpoint registry, embedding, summarization, and chunking all come in-house; the Partio server and dashboard are deleted from `docker/compose.yaml` and `docker/factory/compose.yaml`; `IPartioClient`/`PartioClient` and the Partio config block are removed. No intermediate stop.
- [x] **Prompt override semantics — operator-selectable per prompt.** Each prompt supports a mode of **default / append / replace**, defaulting to append (preserves today's behavior). Global is always the fallback when a subject has no override. This shapes the schema (§4.5) and the dashboard editor (§6).
- [x] **Tokenizer scope — full Partio parity.** Port SharpToken (`cl100k_base`) **and** `Microsoft.ML.Tokenizers` (BERT/WordPiece with embedded vocab), Partio's `TokenizationProfileResolver`, and the structure-aware table/list chunkers, so token counts are model-exact and chunking behavior matches Partio across every strategy and model family.
- [x] **Chunk parity — exact.** The native chunker must reproduce Partio's chunk boundaries so vectors stay stable across cutover (no forced re-index). A byte-level parity harness against Partio output on a fixed corpus is a required gate (§4.4, §8).
- [x] **Version — `0.3.0`** (MINOR bump). The csprojs are at `0.2.0`; the README badge and `pneuma-*` compose image tags still read `0.1.0`. Reconciling all markers (csprojs, README badge, `DOCKERHUB_README`, `CHANGELOG` heading, compose image tags, SDK versions) is a release task in §9.

---

## 2. What Pneuma uses Partio for today

Four functions, with very different removal costs:

| Function | Path today | Removal cost |
|---|---|---|
| Endpoint management (`cep_`/`eep_`, config, health, concurrency, request history) | Pneuma Model Runners UI proxies Partio | Medium — a DB-backed registry, reusing existing health/gate/retry |
| Embedding | `PartioClient.EmbedAsync` / `ProcessAsync` → Partio → provider | Small — `CompletionClientBase.EmbedAsync` already exists in PolyPrompt |
| Summarization | `PartioClient.SummarizeAsync` → Partio → provider | Small — a completion; prompt already owned (`cell.summarize`) |
| Chunking | `PartioClient.ChunkAsync` → Partio (algorithmic, tokenizer-backed) | Medium — port `Partio.Core/Chunking/` + a tokenizer |

Completions (answering, chat, classification, rerank, rewrite) already bypass Partio entirely — Pneuma builds a `ModelRunner` from the endpoint config it read from Partio and calls the provider via `ModelClientFactory.Create(...)` → `CompletionClientBase`. That same base class exposes `EmbedAsync`, `GenerateAsync`, `ListModelsAsync`, and `GetModelInformationAsync`, which is why embedding, model discovery, and endpoint validation come in-house with no new dependency.

---

## 3. Target architecture

Pneuma keeps `ISemanticProcessor` as the internal contract and gains a native implementation plus a real endpoint registry:

- **`IModelEndpointStore`** — a Pneuma-owned, DB-backed registry of embedding and completion endpoints (provider, base URL, model, API format, encrypted key, health/concurrency config), replacing Partio as the system of record. Backed by the provider-neutral data layer across all four databases.
- **`NativeSemanticProcessor : ISemanticProcessor`** — embedding via `CompletionClientBase.EmbedAsync` + L2 normalization + the existing embedding cache (#14); summarization via a direct completion using the `cell.summarize` prompt; chunking via the ported library.
- **`Pneuma.Chunking`** — a new `src/` library holding the ported chunking engine and strategies, plus a `ITokenizerAdapter` (SharpToken).
- **Prompt management** — the existing global-default + per-subject-override system, completed so every LLM operation's prompt is per-subject with a system default, and exposed as a first-class dashboard surface.

`IPartioClient` and `PartioClient` are deleted at the end. The DI wiring in `Bootstrapper`/`PneumaServer` swaps `PartioClient` for `NativeSemanticProcessor` + `IModelEndpointStore`; `IngestionStages` and `GroundedQueryService` change only where they call endpoint-admin methods (which move to the store).

---

## 4. Workstreams

### 4.1 Endpoint registry (server)
- [ ] Model: `ModelEndpoint` (embedding + completion), PrettyId-prefixed, `TenantId`-scoped (nullable-tenant global fallback, mirroring the `prompts` table), encrypted key via the existing `Aes256Cipher`, plus provider-specific fields (deployment/apiVersion for Azure, region for Bedrock/Vertex, project for Vertex, second encrypted secret for AWS) to support every provider.
- [ ] **Full provider coverage (all PolyPrompt providers).** Extend `ModelRunnerProviderEnum` and `ModelClientFactory.Create` to build every PolyPrompt client — OpenAI, OpenAICompatible/vLLM, Azure OpenAI, Gemini, Ollama, Anthropic, Bedrock, Voyage, Vertex — for **both** completions and embeddings. Anthropic exposes no embeddings API (throws `NotSupportedException`), so it must be selectable only for completion endpoints and rejected with a clear validation error for embedding endpoints.
- [ ] `IModelEndpointStore` + provider implementations under `Sqlite`/`Mysql`/`Postgresql`/`SqlServer` `Implementations/` + `Queries/`, handwritten SQL, composite unique index `(tenant_id, …)`.
- [ ] Versioned, idempotent migration adding the `modelendpoints` table to all four providers.
- [ ] Endpoint validation using `GetModelInformationAsync` / a tiny `EmbedAsync` probe; health via the existing `ModelHealthMonitor` (2xx-only, already fixed); concurrency via `ModelRunnerGate`; outbound retry via `IntegrationClientBase`.
- [ ] Model discovery endpoint using `ListModelsAsync` (populate the dashboard's model picker without hand-typing).
- [ ] Repoint `GroundedQueryService` endpoint resolution and `IngestionStages` completion-endpoint resolution from `IPartioClient` to `IModelEndpointStore`.

### 4.2 Embedding (server)
- [ ] `NativeSemanticProcessor.EmbedAsync` → `CompletionClientBase.EmbedAsync`, batched (keep the 64-batch), L2-normalized, cache-backed (#14 `EmbeddingCache`).
- [ ] Query-time embedding (retrieval) moves off `PartioClient.ProcessAsync` onto the native embedder.
- [ ] Dimensionality guard: record model→dimension and validate against the target RecallDB collection's fixed dimensionality at subject-config time and before batch insert.

### 4.3 Summarization (server)
- [ ] `NativeSemanticProcessor.SummarizeAsync` → direct completion using the resolved completion endpoint and the `cell.summarize` prompt (with per-subject override, §4.5).
- [ ] Preserve the top-down / max-token behavior as prompt + parameters (now Pneuma-managed settings, not Partio config).

### 4.4 Chunking (`src/Pneuma.Chunking` library) — full parity
- [ ] Port **all** of `Partio.Core/Chunking/` into `Pneuma.Chunking`: the engine + `FixedTokenChunker`, `SentenceChunker`, `ParagraphChunker`, `RegexChunker`, `WholeListChunker`, `ListEntryChunker`, `TableChunker`, and `ChunkingHelpers` — renamed into Pneuma's namespace and conformed to `CODE_STYLE.md` (no `var`, no tuples, usings inside namespace, `_PascalCase` privates, XML docs, `.ConfigureAwait(false)`, `CancellationToken` on async).
- [ ] `ITokenizerAdapter` with **both** adapters for full parity: `SharpTokenTokenizerAdapter` (SharpToken, `cl100k_base`) and `BertWordPieceTokenizerAdapter` (`Microsoft.ML.Tokenizers` + embedded vocab), plus Partio's `TokenizationProfileResolver` so the right tokenizer is chosen per model family. Add the `SharpToken` and `Microsoft.ML.Tokenizers` NuGets to the chunking library only.
- [ ] Adapter from Pneuma's `ExtractedCell` (already flattened text) to the chunker input; wire `NativeSemanticProcessor.ChunkAsync` to it, honoring the per-subject `ChunkStrategy`/`ChunkMaxTokens`/`ChunkOverlapTokens` from #6.
- [ ] **Exact-parity harness (release gate):** a test that runs a fixed corpus through both Partio and `Pneuma.Chunking` for every strategy × tokenizer profile and asserts byte-identical chunk boundaries and token counts. Must be green before the default flips (§8).
- [ ] Confirm `Pneuma.Chunking` lands under `src/` (REPOSITORY_REQUIREMENTS §6) and is referenced by `Pneuma.Server`.

### 4.5 Prompt management — per-subject with system defaults (server)
- [ ] Complete override coverage: add per-subject override fields (or a normalized per-subject prompt table) for every LLM prompt, including the ones that lack them today — **`cell.summarize`** and the **community-summary** prompt (currently hardcoded in `CommunityService`, promote to a seeded, managed prompt).
- [ ] Implement the chosen merge semantics (§1): default / append / replace, resolved centrally (extend `MergePromptAsync`) and applied uniformly across `ontology.classify`, `ontology.definition`, `cell.summarize`, `community.summarize`, `user.answer`, `reranking`, `prompt.rewrite`, `SystemPrompt`.
- [ ] Effective-prompt resolution API: return the resolved text plus provenance (default vs override, version + hash — Pneuma already records prompt provenance at ingestion), with **global as the fallback** whenever a subject has no override for a given prompt key.
- [ ] Dashboard-facing list/query contract: support filtering prompts by **scope** — a single subject or global — and, for a subject filter, indicate for each prompt whether the value is a subject override or inherited from the global default (so the UI can show the fallback explicitly).
- [ ] Migration + idempotent first-boot seeding for any new prompt keys / override columns across all four providers.

### 4.6 Remove Partio (server + stack)
- [ ] Swap DI in `Bootstrapper`/`PneumaServer`: `NativeSemanticProcessor` for `ISemanticProcessor`, `IModelEndpointStore` for endpoint admin.
- [ ] Delete `IPartioClient`, `PartioClient`, and `PartioEndpoint`-as-wire-model once no references remain.
- [ ] Remove the Partio integration block from `pneuma.json` (and `docker/pneuma.json`, `docker/factory/pneuma.json`); add native processing/endpoint config where needed.
- [ ] Behind a config flag during rollout so native vs Partio can be toggled per environment (§8).

---

## 5. Data & migration
- [ ] One-time importer: read existing Partio endpoints, write them into `IModelEndpointStore`, and remap each subject's `EmbeddingModel`/`InferenceModel`/`RerankingModel`/`PromptRewriteModel` references to the new endpoint ids.
- [ ] Dimensionality reconciliation for existing RecallDB collections (see §4.2 guard).
- [ ] Document the chunk-parity re-index expectation (§1) and provide the "reingest to normalize" operator note.
- [ ] Verify cascade deletion and provisioning paths no longer touch Partio.

---

## 6. Product-surface coverage (the release gate)

Each row must be genuinely covered, not just the server.

- [ ] **Server** — endpoint registry, native processor, chunking library, prompt system; telemetry and tests below.
- [ ] **Admin dashboard** — "Model Endpoints" management (create/edit/validate/health/model-discovery) replacing the Partio proxy, with a provider dropdown covering all PolyPrompt providers and the provider-specific fields (deployment/apiVersion/region/project/creds) shown conditionally; Prompts editor with a **scope filter (subject vs. global)** where global is the fallback and inherited-vs-overridden is shown per prompt (effective value, default-vs-override, reset-to-default, provenance); per-subject chunking already present (#6). All strings via i18next (`I18N.md`); no hardcoded text.
- [ ] **Subject dashboard** — Prompts editor with the same subject/global scope filter and global-fallback behavior, plus chunking + models, consistent with admin.
- [ ] **User dashboard** — no new surface expected; verify nothing referenced Partio.
- [ ] **SDKs** (`sdk/csharp`, `sdk/js`, `sdk/python`) — model-endpoint management + prompt-management methods for the new/changed REST routes, each with test-harness coverage and README updates; loopback base URLs use `127.0.0.1` (REPOSITORY_REQUIREMENTS §7).
- [ ] **REST_API.md** — document new/changed endpoints (model endpoints, prompt management, model discovery); remove anything Partio-proxy-specific; keep in sync (REPOSITORY_REQUIREMENTS §13).
- [ ] **Postman** (`assets/postman/…`) — add/adjust requests + folder/collection descriptions, variables for base URL/ports/tokens, in sync with REST_API.md.
- [ ] **MCP_API.md** — update if any MCP tool surface changes (e.g., model-endpoint or prompt tools); keep in sync (REPOSITORY_REQUIREMENTS §14).
- [ ] **README.md** — update the architecture (drop Partio from the service list and diagram; describe native processing); reconcile the version badge.
- [ ] **DOCKERHUB_README.md** — mirror the README changes (REPOSITORY_REQUIREMENTS §4).
- [ ] **CHANGELOG.md** — a `0.3.0` entry (Keep a Changelog format) describing the native processing move and the Partio removal as the headline.
- [ ] **docker/compose.yaml** — remove `partio-server` and `partio-dashboard`; fix `depends_on` chains that referenced them; confirm `pneuma-*` services use named+tagged images at the release version (REPOSITORY_REQUIREMENTS §9, §11, §12).
- [ ] **docker/factory/compose.yaml** — same removals; update the factory seed so it provisions native endpoint-registry rows instead of Partio `cep_`/`eep_` defaults; verify `reset.bat`/seed still work.
- [ ] **docker/update.bat** — still valid after the image changes (pull/down/up/ps) (REPOSITORY_REQUIREMENTS §10).
- [ ] **Grafana / observability** — remove or repurpose the Partio integration board; ensure the native processing spans/metrics render (see §7 telemetry).

---

## 7. Requirements compliance checklist

Trace each against `C:\code\agents\requirements`.

- [ ] **CODE_STYLE.md** — new server code and the ported chunker conform (no `var`/tuples/partial classes; usings inside namespace; `_PascalCase` privates; explicit getters/setters with validation; `.ConfigureAwait(false)`; `CancellationToken` on async; XML docs incl. nullability/thread-safety; async variant for any new `IEnumerable`-returning method).
- [ ] **TELEMETRY_REQUIREMENTS.md** — embedding, chunking, summarization, and endpoint-health each emit application meters + `ActivitySource` spans nested under the request/job span, same as the existing pipeline stages; no regression to the per-stage timing already surfaced in ingestion; Grafana boards updated; home-page links intact.
- [ ] **I18N.md** — every new user-visible dashboard string comes from the i18n layer across all three dashboards; locale-aware formatting via shared helpers; no raw strings.
- [ ] **BACKEND_ARCHITECTURE.md** — registrars/hooks, `RequestContext`, RBAC gating, request capture, `127.0.0.1` loopback, provider folders and handwritten SQL, versioned idempotent migrations, first-boot seeding.
- [ ] **BACKEND_TEST_ARCHITECTURE.md** — Touchstone descriptors in `Test.Shared`, run via `Test.Automated`/`Xunit`/`Nunit`; four-provider contract suites for the new tables; tests bind `127.0.0.1`.
- [ ] **REPOSITORY_REQUIREMENTS.md** — items covered in §6 (README, DOCKERHUB_README, CHANGELOG, src/ layout, SDKs, docker named images, healthchecks, depends_on, REST_API + Postman, MCP_API).
- [ ] **AUTHENTICATION.md** — model-endpoint and prompt routes are RBAC-gated (endpoints are tenant-owned; keys write-only, never returned); all decisions audited.

---

## 8. Rollout (strangler)

Ship in reversible phases; each is independently valuable.

1. [ ] **Endpoint registry** live; Model Endpoints UI reads/writes it; Partio still running. Importer migrates existing endpoints.
2. [ ] **Native embedding + summarization** behind `ISemanticProcessor`, selected by a per-environment config flag; run against the same providers and compare outputs to Partio.
3. [ ] **Native chunking** (`Pneuma.Chunking` + both tokenizer adapters); the exact-parity harness (§4.4) must assert byte-identical boundaries against Partio before proceeding.
4. [ ] **Prompt system completion** + dashboard editor.
5. [ ] **Flip default to native**, soak, then **remove Partio** from both compose files, delete the client, and drop the config block.

Each phase gates on green `dotnet run --project src/Test.Automated`, all three dashboard builds, and the file-size guardrail.

---

## 9. Version bump (`0.3.0`)
- [ ] Bump `<Version>` in `src/Pneuma.Core/Pneuma.Core.csproj` and `src/Pneuma.Server/Pneuma.Server.csproj` (and any other packable csprojs).
- [ ] Reconcile the README badge (currently `0.1.0`) and `DOCKERHUB_README`.
- [ ] Set the `pneuma-*` image tags in `docker/compose.yaml` and `docker/factory/compose.yaml` to the release version (currently `v0.1.0`).
- [ ] Move the `CHANGELOG.md` `[Unreleased]` content under a dated `## [0.3.0]` heading with the Partio removal as the lead item.
- [ ] Bump SDK package versions where they track the server version.

---

## 10. Risks
- **Chunk/tokenizer parity** is the highest-risk correctness item. The decision is exact parity, so the risk is concentrated in the §4.4 harness: if native boundaries ever diverge from Partio's, re-ingested vectors shift under an unchanged collection. Mitigated by making byte-identical parity a hard release gate before the default flips.
- **Dimensionality mismatch** between an in-house embedder and a collection's fixed dimensionality causes insert/search failures; the §4.2 guard is mandatory, not optional.
- **Provider-coverage parity** — confirm PolyPrompt covers every provider currently configured (OpenAI/Azure/Gemini/Ollama/Bedrock verified in source; check anything else).
- **Lost Partio surfaces** — its dashboard and per-model request history go away; Pneuma's own request-history and telemetry must be confirmed to cover the operator's needs before deletion.
- **Ownership** — Pneuma now maintains a chunker, a tokenizer dependency, and an endpoint registry; the ported chunker is a fork that can diverge from upstream Partio.

## Definition of done
Partio is no longer a component of Pneuma: the server and dashboard are gone from both compose files, `IPartioClient`/`PartioClient` and the Partio config block are deleted, and no reference to Partio remains anywhere in the tree. Ingestion and retrieval run entirely on native processing with byte-identical chunk parity proven by the §4.4 harness. Every function is manageable end to end across all surfaces (§6), the requirements checklist (§7) passes, and the full test suite, all three dashboard builds, and the file-size guardrail are green. The release is `0.3.0` with the CHANGELOG, README, DOCKERHUB_README, REST_API, MCP_API, and Postman collection all in sync. All work is committed on `main` (no feature branch).
