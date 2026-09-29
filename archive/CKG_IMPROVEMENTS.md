# CKG Improvements — Ingestion & Retrieval

A prioritized set of improvements to Pneuma's document→graph→search ingestion pipeline and its grounded retrieval layer. Recommendations are grounded in the current code: the pipeline is a two-phase orchestration (Categorization → Hydration) driven by `IngestionProcessor`/`IngestionStages`, and retrieval is centralized in `GroundedQueryService` (shared by REST and the MCP tool surface). Several capabilities a mature system needs are **already present** — per-subject prompt-rewrite, optional LLM re-ranking, single-hop graph-neighbor expansion, conversation compaction, cited answers with an explicit "insufficient support" refusal, and an LLM-judged eval harness — so the items below deliberately avoid re-litigating those and focus on confirmed gaps.

## Scoring

Each item is scored on two axes, **1–5**:

- **Simplicity** — how easy to implement (5 = small, localized change; 1 = cross-cutting, multi-service effort).
- **Value** — impact on retrieval quality, graph fidelity, cost, or operability (5 = transformative; 1 = marginal).
- **Total** = Simplicity + Value (max 10). Ties are ordered by Value, then Simplicity.

## Prioritized table

Status legend: **✅ Done** · ⬜ Not started.

| # | Item | Simplicity | Value | Total | Status |
|---|------|:---------:|:-----:|:-----:|:------:|
| 1 | Reciprocal-rank fusion for hybrid retrieval | 5 | 4 | **9** | ✅ Done |
| 2 | Automatic retry with backoff on transient ingestion failures | 5 | 3 | **8** | ✅ Done |
| 3 | Content-hash delta detection & document de-duplication on (re)ingest | 3 | 4 | **7** | ✅ Done (delta-skip; cross-link dedup deferred) |
| 4 | Human review/approval gate before a graph goes live | 3 | 4 | **7** | ✅ Done (simplified: per-subject "publish to consumer chat" toggle) |
| 5 | Ontology type-validation guardrail | 4 | 3 | **7** | ✅ Done (canonicalize, not reject — preserves custom types) |
| 6 | Per-subject configurable chunking | 4 | 3 | **7** | ✅ Done (strategy/size/overlap; native `Pneuma.Chunking`) |
| 7 | First-class vector + hybrid search endpoint | 4 | 3 | **7** | ✅ Done (text / vector / hybrid via `mode`) |
| 8 | Multi-hop traversal + community detection (GraphRAG-style) | 3 | 5 | **8** | ✅ Done (on LiteGraph 9 traversal + algorithms) |
| 9 | Fuzzy / embedding-based entity resolution | 2 | 4 | **6** | ⬜ |
| 10 | Query decomposition + retrieval gate / routing | 2 | 4 | **6** | ⬜ |
| 11 | MMR / diversity de-duplication of retrieved passages | 3 | 3 | **6** | ✅ Done (MMR over a widened candidate pool) |
| 12 | Cross-source edge weighting & relationship consolidation | 3 | 3 | **6** | ✅ Done (consolidate + noisy-OR weight) |
| 13 | Dedicated cross-encoder re-ranker option | 3 | 3 | **6** | ✅ Done (per-subject; global `/rerank` endpoint) |
| 14 | Embedding cache | 3 | 3 | **6** | ✅ Done (bounded LRU, global size limit) |
| 15 | True token streaming on the single-shot answer stream | 3 | 3 | **6** | ✅ Done (real token streaming, non-stream fallback) |

> **Implementation note (items 1–4).** Delivered previously. Item 3 ships the content-hash delta-skip (unchanged sources short-circuit re-processing); cross-link document de-duplication remains a follow-up. Item 4 was intentionally reduced from a per-ingestion approval gate to a per-subject `PublishedForChat` attribute (default on) that gates a subject's visibility in the consumer chat experience.
>
> **Implementation note (item 8 — done, built on LiteGraph 9).** The graph store was upgraded to 9.0.0 (compose image pins + the mounted `litegraph.json` migrated to the v9 settings schema). Then #8 was implemented across three layers. **Traversal:** added `GetSubgraphAsync` (wraps v9 `GET .../nodes/{guid}/subgraph?maxDepth=…`), plus `DetectCommunitiesAsync` (wraps `POST .../algorithms` with `AlgorithmType=Louvain`) and `DeleteNodeAsync` to `IGraphRepository` (+ LiteGraph client and fake). Retrieval neighbor-expansion now honours a `NeighborExpansionMaxHops` setting (default 1 = unchanged; >1 uses the bounded server-side subgraph). **Community summaries:** a new `CommunityService` runs Louvain over the tenant graph, groups the subject's entity nodes by community, LLM-summarizes each community above `CommunityMinSize`, and persists each as a `CommunitySummary` graph node (co-located with the graph, cascade-cleaned with the subject; rebuild replaces prior summaries). **Global query mode:** `GroundedQueryService.AnswerGlobalAsync` ranks a subject's community summaries against a thematic question and synthesizes a cited answer. **API:** `POST /v1.0/subjects/{id}/communities/build`, `GET /v1.0/subjects/{id}/communities`, and `POST /v1.0/query/global`. Residual/optional follow-ups (noted, not built): scheduled community recompute (currently operator-triggered via the build endpoint) and auto-routing global-vs-local (overlaps #10); quality still improves with #9. Dashboard buttons for build/global-ask are a thin follow-up.
>
> **Graph-store upgrade (LiteGraph 7.0.0 → 9.0.0) and #8 re-score.** The knowledge-graph store was upgraded to LiteGraph 9.0.0 (compose image pins only; the REST CRUD contract Pneuma uses is unchanged, so no client code changed). v9 is an additive release that adds, server-side and tenant-scoped: **depth-limited subgraph extraction** (`GET .../nodes/{guid}/subgraph?maxDepth=N&maxNodes=&maxEdges=`), **route/path finding** (`POST .../routes`), and a **native graph-algorithm surface** (`POST .../algorithms`) covering **Louvain and label-propagation community detection**, PageRank, degree/closeness/betweenness/eigenvector centrality, connected components, clustering coefficient, and k-core — on demand with optional **write-back** (per-node community/score persisted into node data), plus node embeddings and GraphML/edge-list projection export.
>
> This removes the hardest parts of #8: multi-hop traversal no longer means client-side BFS with fan-out explosion (one bounded `/subgraph` call), and community detection no longer means loading the whole tenant graph into Pneuma's process and implementing Louvain (one `/algorithms` call with write-back — LiteGraph builds the adjacency and computes it). #8 is re-scored **Simplicity 1 → 3 (Total 6 → 8)**, making it the **highest-value remaining item**. Broken down: the *multi-hop traversal* half is now ~Simplicity 4 (wrap `/subgraph` and `/algorithms` in `IGraphRepository` + use in retrieval); the *community-detection GraphRAG* half is what keeps the combined score at 3 — LiteGraph now **detects and tags** communities, but Pneuma must still add the **hierarchical community-summarization stage** (LLM-summarize each community, persist summaries), a **freshness/recompute trigger** (community structure drifts as documents are ingested; LiteGraph gives the compute button, not the schedule), and a **global-vs-local query router** (overlaps #10). What did *not* change: those residual pieces, and the dependency on clean entities (#9). Runtime note: the mounted `litegraph.json` is v7-era; since v9 is additive it should load with new settings defaulted, but confirm a clean first boot after the image bump.
>
> **Implementation note (items 12–14).** **#12** consolidates a re-asserted relationship into a single edge instead of duplicating it, accumulating a corroboration count and a noisy-OR `weight` tag (each corroborating source pushes the weight toward 1.0) — added an `UpdateEdgeAsync` to the graph store to support it. **#13** adds a per-subject reranker type (`LlmListwise` default, or `CrossEncoder`); the cross-encoder calls a globally-configured `/rerank` endpoint (Cohere/Jina/TEI-compatible) and falls back to LLM listwise when unconfigured or on failure. Pneuma's native processing has no rerank capability, so this is a separate HTTP integration. **#14** caches embedding vectors in a bounded, thread-safe LRU (the already-referenced `Caching` package) keyed by (embedding model, content hash), with a **global system size limit** via `IngestionSettings.EmbeddingCacheSize` (0 disables); identical text is served from the cache instead of being re-embedded.
>
> **Implementation note (items 5–7, 11, 15).** Delivered in an earlier pass. **#5** canonicalizes emitted node/edge types (coerce known variants to built-ins, normalize custom ones) rather than rejecting them, so the editable-ontology flexibility is preserved while drift is curbed. **#6** exposes per-subject chunking (strategy/size/overlap); the strategy is validated against the native `Pneuma.Chunking` supported enum (`FixedTokenCount`, `SentenceBased`, `ParagraphBased`) so an invalid strategy is never used. **#7** adds a `mode` parameter (`text` | `vector` | `hybrid`, default hybrid) to `/v1.0/search` and `/v1.0/subjects/{id}/search`, served by the shared retrieval service so search and grounded answering can't drift. **#11** applies Maximal-Marginal-Relevance selection over a candidate pool wider than the final `k`, so near-duplicate passages don't crowd the grounding context. **#15** streams real answer tokens over SSE (with a non-streaming fallback) instead of re-chunking a finished answer.

---

## 1. Reciprocal-rank fusion for hybrid retrieval

**Total 9 (Simplicity 5, Value 4)**

**Today.** `GroundedQueryService.RetrieveSourcesAsync` runs full-text and vector search independently, then merges them with a naive dedup-union: full-text hits are appended first, vector hits second, and duplicates are dropped by graph-node id. There is no score normalization and no rank fusion, so the two channels never actually combine their evidence — a chunk ranked #1 by vector similarity and #2 by lexical match is treated identically to one that appears in a single channel, and ordering is effectively "whichever list we concatenated first."

**Do.** Replace the dedup-union with **Reciprocal-Rank Fusion**: `score(d) = Σ 1/(k + rank_i(d))` across the two ranked lists (k≈60), then sort by fused score. Optionally expose per-subject fusion weights (lexical vs. semantic) alongside the existing retrieval settings. RRF needs no score calibration — it operates purely on ranks — which is why it is both cheap and robust.

**Where.** `GroundedQueryService.RetrieveSourcesAsync`; add a small pure fusion helper. Keep the existing `OrderForReconstruction` document-order pass as a post-fusion step for the answer context. This is a localized change behind an already-centralized retrieval method.

## 2. Automatic retry with backoff on transient ingestion failures

**Total 8 (Simplicity 5, Value 3)**

**Today.** `IngestionJob` already carries an `AttemptCount` field, but nothing increments or acts on it: any stage failure is terminal until an operator manually calls the job restart endpoint. Transient faults in the extraction, chunk/embed, or store services (timeouts, 429s, brief unavailability) permanently fail an otherwise-healthy job.

**Do.** In `IngestionWorkerService` / `IngestionProcessor`, on a **transient** failure (distinguish from `JobCancelledException`, `Unknown`-type, and zero-cell hard fails, which should stay terminal), increment `AttemptCount` and requeue with exponential backoff up to a configurable `MaxAttempts`. Only mark `Failed` once attempts are exhausted. The stage classification already exists in `ProcessAsync`; this reuses it.

**Where.** `IngestionProcessor.ProcessAsync` failure branch; `IngestionWorkerService` claim/requeue loop; a `MaxAttempts` + base-delay setting. The field and error taxonomy are already in place, making this nearly free.

## 3. Content-hash delta detection & document de-duplication on (re)ingest

**Total 7 (Simplicity 3, Value 4)**

**Today.** Reingestion (`restart`) fully reprocesses a source every time; there is no content hashing. Identical or unchanged sources are re-extracted, re-classified, re-chunked, and re-embedded from scratch, and two links pointing at the same bytes both pay full ingestion cost and can create redundant graph/vector entries.

**Do.** After content retrieval, compute a stable hash of the fetched bytes (and optionally of the extracted-cell set). Persist it on the job/link. On (re)ingest, if the hash is unchanged, short-circuit to `Done` (or skip straight to indexing) instead of rebuilding. Across links, detect identical hashes to skip duplicate graph merges. This is the foundation the scheduled-ingestion and cost-reduction items build on.

**Where.** Content-retrieval stage in `IngestionStages`; a hash column on `IngestionJob`/`SubjectLink`; a guard early in `IngestionProcessor`. Cascade-deletion semantics already track per-job contributions, so dedup can be reconciled cleanly.

## 4. Human review/approval gate before a graph goes live

**Total 7 (Simplicity 3, Value 4)**

**Today.** The pipeline is fully auto-approved: Categorization flows straight into Hydration and the merged graph becomes queryable immediately. There is no seam for an operator to inspect what will be asserted before it lands, which matters in any domain where a wrong entity or edge can surface in an executive-facing answer.

**Do.** Make the **candidate subgraph** — already produced as a discrete artifact between the two phases — an optional approval checkpoint. Add a per-subject "require approval" toggle; when on, the job pauses after Categorization in a `PendingReview` state, surfaces the candidate nodes/edges in the dashboard, and only proceeds to Hydration (GraphMerge onward) on approve. Reject discards the candidate. Because Categorization and Hydration are already separate phases with a persisted candidate artifact, the seam exists; this adds a state and a UI.

**Where.** `IngestionProcessor` (split the phase transition on a subject setting), a new job status, the candidate-subgraph artifact (already persisted), and a dashboard review view.

## 5. Ontology type-validation guardrail

**Total 7 (Simplicity 4, Value 3)**

**Today.** The ontology is defined in natural language and the classifier's emitted node/edge type names are accepted verbatim as graph labels. Any type the model invents (a near-synonym, a typo, an off-ontology label) becomes a first-class type, so the ontology silently drifts and downstream tag/label facets fragment.

**Do.** Validate each candidate node/edge `nodeType`/`edgeType` against the ontology's known set in `Ontology`. Coerce close matches to the canonical type (case-insensitive + a small alias map), and drop or quarantine truly unknown types with a journal warning. Optionally feed the closed type list into the classifier prompt's output contract so the model is constrained up front. Keeps the "editable prompt" flexibility while stopping uncontrolled drift.

**Where.** `SubgraphMerger.MergeAsync` (validation pass over the candidate subgraph) and/or the classifier output-contract construction; `Ontology` as the source of truth.

## 6. Per-subject configurable chunking

**Total 7 (Simplicity 4, Value 3)**

**Today.** Chunking is hard-coded to a fixed 256-token window with 32-token overlap for every subject and document type. The downstream chunk/embed service supports other strategies, but none are exposed — so a subject full of short FAQ entries and one full of long technical PDFs are chunked identically, hurting retrieval granularity on both ends.

**Do.** Surface the chunking configuration (strategy, target size, overlap) as **per-subject settings** layered over a global default, mirroring how embedding/inference models and prompt overrides are already per-subject. Pass the resolved config through to the chunk request instead of the hard-coded literal.

**Where.** Subject settings model + edit modal; the chunk-request construction in the chunking client wrapper; thread the value from the job/subject through `IngestionStages`.

## 7. First-class vector + hybrid search endpoint

**Total 7 (Simplicity 4, Value 3)**

**Today.** The standalone search route (`GET /v1.0/search` and the per-subject variant) is **full-text only**. Vector and hybrid search are only exercised inside the grounded-answer path, so external consumers and the dashboards cannot run a semantic or fused search without triggering full answer generation and its LLM cost.

**Do.** Expose vector and hybrid (RRF, from item 1) modes on the search routes via a `mode` parameter, reusing `GroundedQueryService.RetrieveSourcesAsync`. Return ranked hits with scores, provenance tags, and source rollups — no answer synthesis. This makes retrieval independently testable and useful to downstream systems.

**Where.** `SearchRoutes`; delegate to the existing retrieval method rather than duplicating logic.

## 8. Multi-hop traversal + community detection (GraphRAG-style)

**Total 8 (Simplicity 3, Value 5)** — re-scored up from Simplicity 1 / Total 6 after the LiteGraph 7.0.0 → 9.0.0 upgrade.

**Today.** Graph use at query time is limited to **single-hop** neighbor expansion (`IGraphRepository.GetNeighborsAsync`), and Pneuma exposes no traversal/path/subgraph primitives beyond it. There is no community detection or hierarchical summarization, so the system cannot answer global/thematic questions ("what are the main themes across this subject?") — only local ones anchored to retrieved chunks. **However, the graph store (LiteGraph 9.0.0) now provides the heavy machinery server-side**: bounded depth-limited subgraph extraction (`GET .../nodes/{guid}/subgraph?maxDepth=N&maxNodes=&maxEdges=`), path finding (`POST .../routes`), and a native graph-algorithm surface (`POST .../algorithms`) including Louvain and label-propagation community detection with optional per-node write-back. So the missing pieces are now integration and orchestration, not algorithm implementation.

**Do (phased).**
1. **Multi-hop traversal (now ~Simplicity 4).** Add `GetSubgraphAsync` (and, if useful, `FindRoutesAsync`) to `IGraphRepository`, wrapping LiteGraph's `/subgraph` (and `/routes`) endpoints — a bounded server-side call, no client-side BFS. Let retrieval expand beyond one hop along high-weight edges (pairs with item 12's weights for pruning).
2. **Community detection (now a call, not an implementation).** Add `RunAlgorithmAsync` wrapping `POST .../algorithms` with `AlgorithmType=Louvain` (or label propagation) and `WriteBack=true`, so LiteGraph computes communities over the whole tenant graph and tags each node with its community id. Trigger it on a schedule / after ingestion rather than per document.
3. **Hierarchical summarization + global query mode (the residual effort).** Group nodes by community tag, LLM-summarize each community, persist the summaries, keep them fresh as the graph changes, and add a global query mode that answers thematic questions from community summaries — routed to by item 10.

**Where.** `IGraphRepository` + `LiteGraphClient` (thin wrappers over the v9 `/subgraph`, `/routes`, `/algorithms` endpoints) + the graph fake; a scheduled/triggered community-recompute + a new summarization stage with its own storage; `GroundedQueryService` for the global mode. **Re-scored Simplicity 3** (was 1): LiteGraph 9 eliminates the two hardest parts — the traversal primitive and the whole-graph-in-RAM algorithm implementation — leaving the summarization pipeline, recompute freshness, and query routing (which overlaps item 10). Still gated on clean entities (item 9) for quality.

## 9. Fuzzy / embedding-based entity resolution

**Total 6 (Simplicity 2, Value 4)**

**Today.** Entity resolution during merge is an **exact `(nodeType, canonicalName)` string match** within a subject. Any spelling, casing, abbreviation, or phrasing variance ("IBM" vs "I.B.M." vs "International Business Machines") creates a distinct node, so duplicate/variant entities proliferate and fragment the graph and its provenance.

**Do.** Augment exact matching with (a) a normalized/alias lookup and (b) an **embedding-similarity** candidate search over existing same-type node names, with a threshold above which the candidate is treated as the same entity (and optionally recording surface variants as aliases). Keep exact match as the fast path; fall through to fuzzy only on miss.

**Where.** `SubgraphMerger` resolution step; `IGraphRepository.FindNodeByCanonicalAsync` gains a similarity-search sibling. Requires an embedding call per unresolved entity, hence Simplicity 2.

## 10. Query decomposition + retrieval gate / routing

**Total 6 (Simplicity 2, Value 4)**

**Today.** Query understanding is a single-shot rewrite only. Every question takes the same path: there is no decomposition of multi-part questions, no multi-query expansion, and no gate to skip retrieval for conversational turns that need none — so simple chit-chat pays retrieval cost and complex compound questions get one blended retrieval instead of targeted sub-retrievals.

**Do.** Add a lightweight **router/gate** ahead of retrieval: classify the turn as (a) no-retrieval, (b) single retrieval, (c) decompose-into-sub-questions (retrieve per sub-question, then synthesize), or (d) global/thematic (route to item 8's community mode). Start with the gate + simple decomposition; expand later.

**Where.** `GroundedQueryService` (a pre-retrieval routing step) and/or `AgenticChatService`, which already runs a tool-calling loop and is a natural home for decomposition. Reuses the per-subject rewrite model.

## 11. MMR / diversity de-duplication of retrieved passages

**Total 6 (Simplicity 3, Value 3)**

**Today.** Retrieved chunks are ordered and reconstructed but never de-duplicated by semantic similarity. Near-identical passages (common with overlapping chunks and repeated boilerplate across sources) can fill the answer context, crowding out diverse evidence and wasting the token budget.

**Do.** Apply **Maximal Marginal Relevance** (or a similarity-threshold cluster-and-pick) over the fused candidate set before building the answer context, balancing relevance against novelty so the model sees diverse support rather than the same point five times.

**Where.** `GroundedQueryService.RetrieveSourcesAsync`, after fusion (item 1) and before `OrderForReconstruction`. Uses the chunk embeddings already retrieved.

## 12. Cross-source edge weighting & relationship consolidation

**Total 6 (Simplicity 3, Value 3)**

**Today.** Every ingestion job asserts its own edges, deduped only structurally by reference within that job. Confidence is stored per assertion but relationships are **not weighted or consolidated across sources**, so an edge corroborated by twenty documents looks the same as one asserted once, and retrieval/traversal has no signal for which relationships are strongly supported.

**Do.** On merge, when an edge between two resolved nodes already exists, **consolidate** rather than duplicate: accumulate a weight from corroboration count and per-assertion confidence (and optionally an LLM-scored strength). Expose the weight so multi-hop traversal (item 8) and neighbor expansion prefer strong edges.

**Where.** `SubgraphMerger` edge-creation path (upsert-with-weight instead of create); an edge-weight tag; consumed by `GetNeighbors`/traversal ordering.

## 13. Dedicated cross-encoder re-ranker option

**Total 6 (Simplicity 3, Value 3)**

**Today.** Re-ranking exists but is an **LLM listwise reorder** using the subject's answering-class model over truncated passage snippets — effective but comparatively slow and costly, and it competes for the same model runner. There is no option for a purpose-built re-ranker.

**Do.** Add a re-ranker **model type** (cross-encoder / dedicated reranking endpoint) as an alternative to the LLM listwise path, selectable per subject alongside the existing reranking model setting. Cross-encoders give better precision-per-dollar and lower latency for the top-k reorder.

**Where.** `GroundedQueryService.RerankAsync` (strategy branch on reranker type); subject settings; a reranking client wrapper. The re-rank seam and per-subject model plumbing already exist.

## 14. Embedding cache

**Total 6 (Simplicity 3, Value 3)**

**Today.** There is no embedding cache: the same chunk text is re-embedded on every reingest, and identical text across documents is embedded repeatedly. Embedding is a per-batch network + compute cost paid needlessly.

**Do.** Cache embeddings keyed by `(embedding-model, content-hash)`. On embed, look up first and only send cache misses to the embed service. Pairs directly with the content-hashing from item 3 and sharply reduces reingest cost.

**Where.** The embedding stage in `IngestionStages`; a cache store (table or object store) keyed by model + hash.

## 15. True token streaming on the single-shot answer stream

**Total 6 (Simplicity 3, Value 3)**

**Today.** The single-shot answer stream endpoint generates the **full answer first**, then re-chunks it client-side into fake SSE deltas — so "streaming" has the same time-to-first-token as the non-streaming call. (The agentic chat path already streams tokens for real, so the building blocks exist.)

**Do.** Wire the single-shot stream endpoint to the model runner's **real token stream** (as the agentic path does), emitting deltas as generated and reusing the existing `<think>`-separation handling. Materially improves perceived latency.

**Where.** The answer-stream route and `GroundedQueryService` generation call; reuse the token-streaming plumbing already present in the agentic chat service.

---

## Suggested sequencing

- **Quick wins first (items 1, 2, 5, 7, 15):** small, localized changes that improve retrieval quality, resilience, and API completeness with minimal risk.
- **Cost & correctness (items 3, 6, 14, 11, 13):** hashing/dedup, configurable chunking, embedding cache, diversity, and a cheaper re-ranker — mostly independent, several share the content-hash foundation.
- **Graph fidelity (items 4, 9, 12):** approval gate, better entity resolution, and edge consolidation — these compound, and 9+12 make item 8 far more valuable.
- **Strategic (items 8, 10):** GraphRAG traversal/community detection and query routing — highest ceiling, highest effort; sequence last and build on the graph-fidelity work.
