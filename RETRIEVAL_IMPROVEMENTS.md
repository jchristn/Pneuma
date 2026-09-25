# Retrieval improvements

Isis, the agent-memory platform in `C:\Code\AgentMemory`, went through a benchmark effort that found a set of
retrieval, RAG, and ingest weaknesses and fixed most of them (see `C:\Code\AgentMemory\RETRIEVAL_IMPROVEMENTS.md`,
`benchmarks\RESULTS.md`, and the top `CHANGELOG.md` entry there). Pneuma sits on the same foundations: RecallDB for
chunk text, vectors, and full-text search, PolyPrompt 2.6.0 for embeddings and completions, a token chunker from the
same lineage as TextChunker, and Watson 7.1 for REST and MCP. Many of the Isis findings therefore have a Pneuma twin,
some are already solved in Pneuma, and a few do not apply because Pneuma works differently.

The document walks through how Pneuma retrieves today, gives a verdict with code evidence for every Isis finding,
lists the retrieval problems that are specific to Pneuma, and ranks the proposed work by value and simplicity in one
table. It closes with a plan for benchmarking Pneuma with the Isis harness and a checklist for annotating decisions.
No code was changed to produce it. Line numbers refer to the working tree as of commit `3d64869`.

## How Pneuma retrieves today

Pneuma is a knowledge-graph platform, not a memory store, but its answer path is a conventional hybrid RAG pipeline.
Ingestion (`src/Pneuma.Core/Ingestion/Pipeline/IngestionProcessor.cs`) fetches a submitted URL, extracts semantic
cells with DocumentAtom, classifies them into the ontology with an LLM, and merges a Source node, one Cell node per
cell, and entity nodes into LiteGraph. The hydration phase then summarizes cells, chunks both cell text and summaries
(`ChunkingStage.cs`), embeds the chunks in batches of 64 (`EmbeddingStage.cs`), and stores each chunk as a RecallDB
document whose `litegraphNodeId` tag points at its Cell node (`IndexingStage.cs:70-103`). Chunk text lives only in
RecallDB; the graph holds structure and provenance.

Retrieval is concentrated in `src/Pneuma.Server/Services/GroundedQueryService.cs`. `GatherAsync` (lines 985-1130) runs
a full-text leg (RecallDB `TsRank`) and then a vector leg (cosine similarity) against the subject's collection, fuses
them client-side with weighted Reciprocal Rank Fusion (`RrfK` 60, both weights 1.0 in `RetrievalSettings.cs`), and
resolves every hit to a LiteGraph node. The grounded answer path (`RetrieveSourcesAsync`, lines 344-434) widens the pool
to four times the requested count, selects passages with lexical-cosine MMR, orders them for reconstruction, optionally
expands graph neighbors, optionally reranks with a cross-encoder or an LLM, and asks the subject's inference model for
a cited answer. `POST /v1.0/query`, `POST /v1.0/query/stream`, and the MCP `pneuma_query` tool all use that path.

Two other surfaces retrieve differently, which matters for several findings below. The raw search routes
(`SearchRoutes.cs`) call `GroundedQueryService.SearchAsync` in text, vector, or hybrid mode. The agentic chat used by
every dashboard's Ask screen (`AgenticChatService.cs`, `POST /v1.0/chat/stream`) and the MCP `pneuma_search` tool both
go through `McpGraphTools.SearchAsync` (`src/Pneuma.Server/Mcp/McpGraphTools.cs:71-130`), which is a separate,
full-text-only implementation.

Pneuma's own history of retrieval work is in `CKG_IMPROVEMENTS.md`: RRF, MMR, the cross-encoder option, the embedding
cache, content-hash delta skip, and multi-hop expansion are already done there, and this document builds on that list
rather than repeating it.

## Verdicts on the Isis findings

Each finding from the Isis effort was checked against Pneuma's code. "Applicable" means the same gap exists, "Partially
applicable" means Pneuma handles part of it or has it in a different form, "Already handled" means Pneuma's code covers
it, and "Not applicable" means the mechanism does not exist in Pneuma. The Fix column points at the ranked proposal
letter in the next section.

| # | Isis finding | Verdict | Evidence in Pneuma | Fix |
|---|---|---|---|---|
| 1 | Token-budget overflow for small embedding models; margin and re-chunk on context-length errors | Applicable | Chunking always uses cl100k_base, whatever the embedding model (`NativeSemanticProcessor.cs:86` builds a default `ResolvedTokenizationProfile`, whose `TokenizerKind` is `Cl100kBase`). The budget is the subject's `ChunkMaxTokens` (default 256, clamped only to 16..8192 in `Subject.cs:135-139`) with no model check, no margin, and no retry at a smaller size. The BERT adapter exists but nothing outside tests uses it. | F |
| 2 | Concurrent first-write provisioning races | Partially applicable | Pneuma provisions at tenant creation, not on first write, so the Isis race path does not exist. `RecallDbTenantProvisioner.cs:49-66` and `RecallDbClient.EnsureTenantAsync` (lines 256-270) are still check-then-create with no serialization, and tenants or collections created in the same instant can hit RecallDB's index-name collision (`GetIndexIdentifier`). | W |
| 3 | Concurrent same-key upsert races | Applicable, different keys | Entity resolution in `SubgraphMerger.cs:101-112` is find-by-canonical-name then create, unguarded, while up to 8 jobs run at once (`IngestionSettings._MaxConcurrentTasks`). Two links in one subject that mention the same entity can create two nodes for it. `ClaimNextQueuedAsync` (`Postgresql/Implementations/IngestionJobMethods.cs:108-130`) never checks the affected row count, so it is only safe while one server process polls. Nothing stops two jobs for the same link from running together. | S |
| 4 | Invalid Unicode (lone surrogates) makes a record unstorable | Applicable | No surrogate handling anywhere in `src` (a search for `IsSurrogate`, `FFFD`, or `surrogate` finds nothing). Extracted cell text, link titles, labels, and tags go straight to LiteGraph and RecallDB. | C |
| 5 | Chunker splits emoji surrogate pairs | Applicable, different symptom | `SharpTokenTokenizerAdapter.SliceByTokenRange` decodes an arbitrary token range. cl100k is byte-level BPE, so a slice boundary inside a multi-byte character should decode to U+FFFD instead of failing, which silently corrupts stored chunk text and perturbs the re-count in `ChunkingHelpers.CreateStrictTokenSlice`. Needs a confirming test. | R |
| 6 | Redundant tail chunks from fixed-token chunking with overlap | Partially applicable (verify) | `ChunkingHelpers.ChunkByTokenSpans` (lines 15-61) is the same algorithm as TextChunker, and `ChunkingParitySuite` pins its output to Partio goldens byte for byte, so the Isis defect is likely present. No test checks for chunks wholly contained in their predecessor. | R |
| 7 | HttpClient per request, socket exhaustion | Already handled | Integration clients share one process-wide client (`IntegrationHttp.cs:12`); model clients share one `SocketsHttpHandler` with a 5-minute `PooledConnectionLifetime` (`ModelClientFactory.cs:24-27`); the content fetcher and cross-encoder hold one client each. Two small leftovers: `LiteGraphTenantAdmin.cs:102` builds a client per admin call (rare), and the shared integration client has no `PooledConnectionLifetime`, so it never re-resolves DNS after a container is recreated. | K (note) |
| 8 | Unhandled exceptions return HTML instead of JSON | Partially applicable | Every route registers `RouteHelper.ExceptionAsync` as its handler, so route errors are JSON. There is no server-wide `Routes.Exception` (`PneumaServer.cs:169-175`), and `RouteHelper.ExceptionAsync` (lines 21-36) maps everything except a bad body to 500, including RecallDB or LiteGraph outages (`IntegrationClientException`) and argument errors, and it returns the raw exception message. | K |
| 9 | Keyword search with AND semantics is nearly useless | Partially applicable | `RecallDbClient.cs:204-214` sends no `MatchMode`, so Pneuma gets whatever the RecallDB image defaults to. RecallDB commit `d8ce32c` made any-term the default under the same `v0.2.1` label that `docker/compose.yaml:179` pins. The local image was rebuilt the day of that commit; any deployment that pulled `v0.2.1` earlier still has all-terms matching, which gave Isis keyword nDCG of 0.06 to 0.14. | B |
| 10 | Hybrid as a required text filter | Not applicable | Pneuma never uses RecallDB's hybrid mode; it runs the two legs itself and fuses them as a union (`GroundedQueryService.cs:1022-1112`). | |
| 11 | Chat grounded on truncated 240-character snippets | Partially applicable | The grounded path uses whole chunk content from RecallDB (`BuildSourceContext`, lines 874-888). The agentic and MCP search tool truncates to 1,200 characters (`McpGraphTools.cs:125`), the LLM reranker judges only the first 500 (`GroundedQueryService.cs:611`), and the cross-encoder the first 2,000 (line 642). With the default 256-token chunks only the reranker cut bites; subjects with larger chunks lose tail text in agentic chat. | T |
| 12 | Chat retrieval depth too shallow | Already handled | `QueryRequest.MaxResults` and `ChatRequest.MaxResults` default to 8, the grounded pool is four times that (`GroundedQueryService.cs:349`), and the agentic tool defaults to 20 hits. | |
| 13 | Stricter anti-hallucination prompt | Partially applicable | The seeded `user.answer` prompt (`FirstBootSeeder.cs:286-292`) asks for grounding and says to decline "rather than guessing", but does not forbid general-knowledge fill-in or require a citation per claim. The agentic `assistant.system` prompt (lines 342-373) is similar. | I |
| 14 | Rank fusion with normalized scores and per-leg evidence | Partially applicable | RRF is done (CKG item 1). The score callers see is the best raw score from whichever leg matched (`GroundedQueryService.cs:468`), so a hybrid list mixes cosine similarity (about 0.3 to 0.9) with `ts_rank` (often under 0.1) on one scale. No per-leg score or rank is returned, and the only threshold is `VectorMinimumScore` on the vector leg. Citation scores use the same mixed value (line 376). | G |
| 15 | Recency signal | Partially applicable | No time signal enters ranking, and chunks carry no date tag. Pneuma's corpora are documents, where write order means little; the stale-version problem is better solved by replacing old chunks on re-ingest (E). A source-date signal is only worth it for dated corpora such as news. | V |
| 16 | Chunk title and summary headers in embeddings | Applicable | `EmbeddingStage.cs:79-82` embeds bare chunk text. Link titles (`SubmitLinkRequest.Title`) and cell titles (`ExtractedCell.Title`) are never part of the embedded text. | O |
| 17 | Supersedes links | Applicable, as re-ingest replacement | Re-ingesting a link creates a new job (`SubjectLinkRoutes.cs:213-233`), and chunk keys are `jobId_position` (`IndexingStage.cs:97`), so the previous version's chunks and its Source and Cell nodes stay searchable until someone deletes the old job (`CascadeDeletionService.cs:204-213`). Old and new text of a changed page compete in every search. | E |
| 18 | Follow links from top hits | Already handled | Graph neighbor expansion, single-hop or multi-hop, exists in `RetrieveSourcesAsync` (lines 388-431). It is off by default (`NeighborExpansionEnabled = false`) and adds entity and Source nodes rather than more chunk text, so measure it before turning it on. | |
| 19 | MMR diversity | Already handled | `SelectWithMmr` (lines 1139-1194), on by default with lambda 0.7 (CKG item 11). | |
| 20 | Lookup caching | Applicable | One grounded answer reads the subject from the database up to four times (`GroundedQueryService.cs:155`, `993`, `738`, and via `PromptResolver` for each of rewrite, rerank, and answer at line 679), reads model runners per call, and, for subjects without a collection, lists RecallDB collections through `CollectionResolver`. | P |
| 21 | Cross-encoder reranking with a relevance cutoff | Partially applicable | A cross-encoder exists (`HttpCrossEncoderReranker.cs`, per-subject `RerankerType`, CKG item 13). Its scores are used only to sort and then dropped (`GroundedQueryService.cs:646-659`). "Insufficient support" fires only when retrieval returns nothing at all (line 162), which almost never happens with any-term text matching. | J |
| 22 | Write-time duplicate detection | Partially applicable | Unchanged content on the same link is skipped by content hash (`ContentRetrievalStage.cs:60-66`), and identical text is not re-embedded (`EmbeddingCache`). The same document under two URLs, or near-duplicate cells across links, is stored and retrieved twice. CKG item 3 defers cross-link dedup. | U |
| 23 | MCP protocol compatibility with current clients | Applicable | Pneuma's MCP transport is hand-rolled, not Voltaic. `McpToolCatalog.cs:21` hard-codes protocol `2024-11-05` with no negotiation, `McpRoutes.cs:113-133` answers only `initialize`, `ping`, `notifications/initialized`, `tools/list`, and `tools/call`, and it replies with a JSON-RPC error to any other notification instead of HTTP 202. Claude Code 2.1.x starts with `server/discover` and the stateless `2026-07-28` revision, which is exactly where Isis broke. | L |
| 24 | Stronger embedding model chosen by benchmark (Isis #2) | Partially applicable | Pneuma seeds `nomic-embed-text` (`FirstBootSeeder.cs:61`), already stronger than Isis's all-minilm. It has never been compared with alternatives on Pneuma data. | Y |
| 25 | Update-not-duplicate guidance for agents (Isis #4) | Not applicable | Agents do not write to Pneuma; content arrives by link ingestion. The analogous problem is E. | |
| 26 | Count query on ranked text search (Isis #8) | Not applicable | Fixed upstream in RecallDB (`COUNT(*) OVER ()`), and Pneuma reads no totals from search. | |
| 27 | Single-call RecallDB hybrid (Isis #15) | Applicable | Pneuma has its own RecallDB client, so it can send `Hybrid` options without waiting for an SDK release. The single call also fixes a vector-leg under-fill described under Q. | Q |
| 28 | Split multi-part questions into sub-queries (Isis #16) | Partially applicable | The agentic loop can issue several searches (`ChatMaxToolIterations` 6); the grounded path cannot. Tracked as CKG item 10. | |
| 29 | Query expansion (Isis #17) | Partially applicable | A per-subject prompt-rewrite model exists (`RewriteQuestionAsync`, lines 554-572). No hypothetical-answer expansion. | |
| 30 | Search legs run in parallel (Isis round 1) | Applicable | The text leg finishes before the query is even embedded (`GatherAsync`, lines 1022-1112), and every hit is resolved with a serial LiteGraph read (lines 1039 and 1085). | H |
| 31 | URL-unsafe chunk document keys (Isis `#` bug) | Not applicable | Keys are `jobId_position` (`IndexingStage.cs:97`), all URL-safe characters. | |

Across the 31 rows the tally is 10 Applicable (rows 1, 3, 4, 5, 16, 17, 20, 23, 27, 30), 13 Partially applicable
(2, 6, 8, 9, 11, 13, 14, 15, 21, 22, 24, 28, 29), 4 Already handled (7, 12, 18, 19), and 4 Not applicable (10, 25, 26,
31). The seven Pneuma-specific problems in the next section are counted separately and are all applicable.

## Retrieval problems specific to Pneuma

Reading the code for the table above turned up problems Isis never had. Several matter more than any imported
finding, because they decide what the Ask screen and MCP agents actually see.

**The agentic chat and MCP `pneuma_search` tool never use vectors.** `McpGraphTools.SearchAsync` calls only the
full-text leg (`McpGraphTools.cs:103`), and the tool description (`McpToolCatalog.cs:214`) and the seeded assistant
prompt (`FirstBootSeeder.cs:352`) both call it "full-text search". Every dashboard's Ask screen runs on that tool, so the
most visible answer surface in Pneuma has no semantic retrieval: a paraphrased question that shares no stemmed term with
the source finds nothing. The same method resolves the collection with a null override (line 87), which falls back to
the tenant default collection, so a subject whose chunks live in its own collection is searched in the wrong place.
It also resolves every hit's node with a serial LiteGraph read (line 119).

**Tenant-wide hybrid search is silently lexical.** `GET /v1.0/search` passes no subject (`SearchRoutes.cs:104`), so
`GatherAsync` embeds the query with a null endpoint id (line 1004). `NativeSemanticProcessor.EmbedAsync` then throws
because no runner resolves (`NativeSemanticProcessor.cs:123-124`), `EmbedQueryAsync` swallows it (lines 1308-1311), and
the vector leg is skipped with only a warning in the log. A caller asking for `mode=hybrid` or `mode=vector` without a
subject gets full-text results, or nothing, while the response still says `Hybrid`.

**Sibling chunks collapse into one pool entry, and the wrong text can win.** The pool is keyed by
`litegraphNodeId`, which is the Cell node, not the chunk (`GroundedQueryService.cs:1032`, `IndexingStage.cs:70`). A
long cell has several chunks, and its summary chunks carry the same Cell id (`SummarizationStage.cs:93`). All of them
fold into one entry. Only the first hit in each leg counts toward RRF, and in the vector leg every later hit for the
same Cell overwrites the stored content (lines 1093-1097), so the text sent to the model is the lowest-ranked matching
chunk of that cell rather than the best one. The answer can never see two chunks of the same cell, and a summary chunk
can displace the body text that holds the detail.

**A failed leg degrades results silently.** Both legs catch every exception and log a warning (lines 1061-1064 and
1108-1111). A RecallDB error on one leg, or an embedding model that is down, returns a one-legged result with no marker
in the response, the metrics, or the chat telemetry.

**The vector leg can return fewer hits than asked.** RecallDB raises `hnsw.ef_search` only for its hybrid leg
(`RecallDB SearchMethods.cs:364` and `643-649`), so Pneuma's vector-only requests are capped by pgvector's default of 40
candidates, and the `subjectId` tag filter is applied after that cap. The subject search endpoint asks for 250
(`SearchPoolSize`) and a subject that shares the tenant default collection with other subjects can get far fewer, or
none. pgvector 0.8 iterative scans would fix the filter case, but RecallDB does not enable them.

**Retries and re-ingests leave duplicates behind.** A transient failure retries the whole job (`IngestionProcessor.cs`
lines 141-209), and each attempt creates a fresh Source node and fresh Cell nodes (`GraphMergeStage.cs`, line 29 and
`CreateCellNodeAsync`), so a job that failed in embedding and then succeeded leaves orphaned graph nodes. If a chunk
batch reached RecallDB but the response timed out, the retry sends the same `jobId_position` keys again. Deterministic
errors such as a context-length rejection throw `InvalidOperationException` (`NativeSemanticProcessor.cs:136`), which the
processor treats as transient, so the LLM classification stage runs three times before the job fails.

**Reranking undoes reconstruction order and ranks entity stubs.** `AnswerAsync` reranks after neighbor expansion and
after `OrderForReconstruction` (`GroundedQueryService.cs:161-173`), so entity and Source nodes, which carry names rather
than passages, are scored alongside chunks, and a successful rerank discards the source-grouped, position-ordered layout
that reconstruction built.

## Ranked proposals

Scores follow the Isis convention: value is the expected effect on answer quality or robustness (1 to 10), simplicity
is the effort and risk to build and ship (1 to 10), the score is their sum, and ties go to the higher value. Items with a
simplicity of 8 or more are good candidates for a first round.

| Rank | Id | Fix | Value | Simplicity | Score |
|---|---|---|---|---|---|
| 1 | A | Make `pneuma_search` hybrid and subject-aware by routing it through `GroundedQueryService.SearchAsync` | 9 | 8 | 17 |
| 2 | B | Send `FullText.MatchMode = "Any"` explicitly and verify it at startup | 7 | 10 | 17 |
| 3 | C | Replace unpaired surrogates with U+FFFD at every ingest boundary | 6 | 9 | 15 |
| 4 | D | Key the retrieval pool by chunk, not by Cell node | 8 | 6 | 14 |
| 5 | E | Replace a link's previous version on re-ingest, and make retries idempotent | 8 | 6 | 14 |
| 6 | H | Run the two legs in parallel and resolve graph nodes in one batch | 6 | 8 | 14 |
| 7 | I | Stricter answer and assistant prompts, healed into existing tenants | 5 | 9 | 14 |
| 8 | K | Server-wide exception route with status mapping | 5 | 9 | 14 |
| 9 | F | Model-aware token budget with a margin, re-chunk on context-length errors, hard-fail classification | 8 | 5 | 13 |
| 10 | L | MCP protocol negotiation, `server/discover`, and the `2026-07-28` result fields | 8 | 5 | 13 |
| 11 | J | Relevance cutoff on cross-encoder scores that produces "insufficient support" | 7 | 6 | 13 |
| 12 | G | Normalized fused score, per-leg evidence, and `minScore` in every mode | 6 | 7 | 13 |
| 13 | M | Resolve an embedding model for tenant-wide search, or reject vector modes without one | 6 | 7 | 13 |
| 14 | O | Embed a title header with each chunk (stored content unchanged) | 6 | 7 | 13 |
| 15 | N | Report leg failures as a `degraded` flag and a metric | 4 | 9 | 13 |
| 16 | Y | Choose the default embedding model by benchmark | 6 | 6 | 12 |
| 17 | P | Per-request lookup cache for subject, prompts, and runners | 4 | 8 | 12 |
| 18 | T | Align tool and reranker passage limits with chunk size | 3 | 9 | 12 |
| 19 | Q | Use RecallDB's single-call hybrid (fixes vector-leg under-fill) | 6 | 5 | 11 |
| 20 | S | Serialize entity merges per canonical key, make job claims atomic, one active job per link | 6 | 5 | 11 |
| 21 | R | Surrogate-safe token slicing and contained-tail dedupe in `Pneuma.Chunking` | 5 | 6 | 11 |
| 22 | X | Rerank only chunk passages, before expansion, and keep reconstruction order | 3 | 8 | 11 |
| 23 | U | Cross-link duplicate detection at write time | 5 | 5 | 10 |
| 24 | W | Serialize RecallDB tenant and collection provisioning and adopt on conflict | 3 | 7 | 10 |
| 25 | V | Source-date recency signal for dated corpora | 3 | 6 | 9 |

## Proposal details

### A. Hybrid, subject-aware `pneuma_search`

The Ask screen is where most users meet Pneuma's retrieval, and today it has only half of it. Replace the body of
`McpGraphTools.SearchAsync` with a call to `GroundedQueryService.SearchAsync(tenantId, query, max, subjectId,
RetrievalModeEnum.Hybrid, requestFilter, null, token, resolveNodes: false)`, then map each `RetrievedChunk` to the
existing `{ id, name, nodeType, score, snippet }` shape. That one change brings the vector leg, the subject's own
collection and embedding model, and the subject's default facet filter, and it removes the serial per-hit LiteGraph
read. Keep the optional rerank. Accept an optional `mode` argument (`text`, `vector`, `hybrid`) so agents can still ask
for exact-term matching.

Update the tool description in `McpToolCatalog.cs:214` and the `pneuma_search` line of `DefaultAssistantSystemPrompt`
(`FirstBootSeeder.cs:352`), and add a `HealPromptAsync` entry so existing tenants pick up the new wording. Tests: a
`RetrievalSuite` case where the only matching chunk shares no term with the query (vector-only match) and must be found
by the tool, and a case where the subject's collection differs from the tenant default. Docs: `MCP_API.md` for the tool,
`README.md` where it describes the Ask trace. The risk is latency: each tool call now embeds the query, which is one
extra model round trip per search; the warm-up route already exists to hide cold loads.

### B. Explicit any-term matching

Add `MatchMode = "Any"` to the `FullText` object in `RecallDbClient.SearchAsync` (`RecallDbClient.cs:206-211`). A
RecallDB build that predates `MatchMode` would ignore or reject the field, so add a check to
`ExternalServiceDiagnosticsService` (or the startup probe) that runs a two-term query where only one term matches a
seeded document and warns when it returns nothing. Tests: assert the request body in `FakeRecallDbClient` or through
`RecordingHttpMessageHandler` carries `MatchMode`. Docs: `README.md` "How it works" should state that lexical retrieval
matches any term. Risk: very low.

### C. Unicode sanitizing

Add a small `TextSanitizer` in `Pneuma.Core/Helpers` (Isis has one in `src/Isis.Core/Helpers/TextSanitizer.cs`) that
replaces each unpaired surrogate with U+FFFD, and apply it to extracted cell text and titles at the end of
`CellExtractionStage`, to summaries in `SummarizationStage`, and to link titles, labels, and tags on submit in
`SubjectLinkRoutes`. Without it, one stray code unit from a PDF or a badly encoded page fails the whole job after three
full retries. Tests: a cell with a lone high surrogate ingests and its stored chunk contains U+FFFD; a valid emoji is
left untouched. Docs: a `CHANGELOG.md` entry. Risk: none known.

### D. Chunk-keyed retrieval pool

Carry the RecallDB `DocumentKey` on `VectorSearchHit` (read it in `RecallDbClient.cs:193` the way the text leg does at
line 221) and key `RetrievalPool` by that key instead of by `litegraphNodeId`. RRF then counts each chunk at its own
rank, MMR chooses among real passages, and each chunk keeps its own content. Roll up to the Cell node only where a node
is needed: for neighbor expansion, citations, and the subject search's per-link grouping. Tag summary chunks with a
`chunkKind` of `summary` in `IndexingStage` so the pool can prefer body text when both match. Tests: two chunks of one
cell, the second one relevant, must yield the second chunk's text in `BuildSourceContext`; a body chunk and a summary
chunk of one cell must both be selectable. Docs: `REST_API.md` if `RetrievedChunk` gains a `documentKey`. Risk: moderate,
since `SearchAsync`, `RetrieveSourcesAsync`, and the subject search all read the pool, and the reconstruction order
depends on `PositionByNode`.

### E. Replace the previous version on re-ingest, and idempotent retries

When a job for a link completes, delete the chunks and graph contributions of that link's earlier completed jobs:
`DeleteByTagAsync(tenant, collection, "jobId", oldJobId)` and `DeleteByJobAsync(oldJobId)` for each, run after the new
index write succeeds so there is never a window with no content. Add the cleanup to `IngestionJournal.CompleteAsync` or
as a final hydration step, and reuse `CascadeDeletionService.CleanupJobExternalsAsync` rather than a second copy. For
retries, clear the job's own partial contributions (the same two calls with the current job id) at the start of each
attempt after the first, so a retry never leaves duplicate Source and Cell nodes or re-sends existing chunk keys.

Tests: re-ingest a link whose content changed and assert only the new text is searchable; force a failure in
`EmbeddingStage` on the first attempt and assert one Source node and one set of chunks after success. Docs: `README.md`
ingestion section and `REST_API.md` for the reingest route. Risk: deleting the wrong job's content; the delete must be
scoped by both link id and job id, and job history rows should stay for audit.

### H. Parallel legs and batched node resolution

Start the text search and the query embedding together in `GatherAsync`, run the vector search as soon as the embedding
arrives, and fuse after both finish (Isis measured a clear p50 drop from the same change). Then resolve all pool node ids
in one pass instead of one `ReadNodeAsync` per hit: either a batch read if the LiteGraph client offers one, or bounded
concurrent reads. The grounded path resolves up to 64 hits per question today, one round trip at a time. Tests: a
`DelayingHttpMessageHandler` case showing total time is near the slower leg, not the sum. Docs: `TELEMETRY.md` if new
span names are added. Risk: low; keep the fused order deterministic regardless of which leg returns first.

### I. Stricter prompts

Tighten `DefaultUserAnswerPrompt` and the grounding rules in `DefaultAssistantSystemPrompt`: state only what the
sources say, no general-knowledge fill-in, cite a bracketed source number for every claim, and when the sources do not
answer the question say so and offer any related information they do contain. Seeded prompts are skipped when a key
already exists (`SeedPromptAsync`), so add `HealPromptAsync` entries (the mechanism at `FirstBootSeeder.cs:245-248`) that
replace only unedited defaults. Tests: `SubjectPromptSuite` asserts the seeded text and that an operator-edited prompt is
not overwritten. Docs: none beyond `CHANGELOG.md`. Risk: Isis saw a small model decline slightly less often after a
similar change because of larger contexts, so measure declines with the benchmark.

### K. Server-wide exception route

Set `_Server.Routes.Exception` in `PneumaServer.ConfigureServer` (Watson.Core 7.1.0 exposes
`WebserverRoutes.Exception`; Isis does it in `IsisServer.cs:163` and `217`) and extend `RouteHelper.ExceptionAsync` to map
`RequestBodyException` and `ArgumentException` to 400, `KeyNotFoundException` to 404, `NotSupportedException` to 501,
`IntegrationClientException` and `HttpRequestException` to 502 or 503, and a client-cancelled request to no body.
Log the full exception and return a generic message for 500s rather than `e.Message`. While there, give
`IntegrationHttp.Client` a `SocketsHttpHandler` with a `PooledConnectionLifetime` so it follows container IP changes.
Tests: `ApiSuite` cases that force a fake RecallDB failure and assert a 503 JSON body. Docs: `REST_API.md` error
section. Risk: clients that key on 500 today will see new codes.

### F. Model-aware token budget and recovery

Resolve a tokenization profile per embedding runner: BERT WordPiece for the MiniLM, mpnet, nomic, and mxbai families,
cl100k for OpenAI models, and each model's real input limit (all-minilm 256, nomic-embed-text as configured in Ollama).
Add an optional `MaxInputTokens` to `ModelRunner` for models the table does not know. In `ChunkingStage`, cap
`ChunkMaxTokens` at the resolved budget minus a 4% margin and the model's special tokens, and log when a subject's
setting is lowered. In `EmbeddingStage`, catch a context-length rejection for a batch, re-chunk only the failing texts at
0.75, 0.5, and 0.3 of the budget, and only then fail the job with `IngestionHardFailException` so it is not retried three
times with identical input. PolyPrompt 2.6.0 surfaces the provider error text in `EmbeddingResponse.Error`; match on it
the way Isis's `MemoryChunker` does.

The query side needs the same care: `EmbedQueryAsync` embeds only the first chunk of a long question
(`NativeSemanticProcessor.cs:151` with default options). Tests: a technical, accented text that fits 256 cl100k tokens
but exceeds 256 WordPiece tokens chunks under the BERT budget; a stub provider that rejects inputs over N tokens forces
one re-chunk and then success. Docs: `REST_API.md` for subject chunk settings and the runner field, `README.md` model
section. Risk: the parity suite pins cl100k boundaries, so keep cl100k as the fallback profile and add goldens for the
BERT path.

### L. MCP protocol compatibility

Pneuma is exposed to exactly the failure Isis hit: Isis on Voltaic 0.6 and 0.7 connected to Claude Code 2.1.x and showed
zero tools. Implement version negotiation in `initialize` (echo the client's requested version when supported, including
`2025-06-18` and `2026-07-28`), add `server/discover`, add the result fields the `2026-07-28` revision requires
(`resultType`, `ttlMs`, `cacheScope` on `tools/list` and tool results), accept every `notifications/*` method with HTTP
202 and no body, and answer `GET /mcp` with 405 so clients do not wait for a stream. Voltaic 1.1.0 is the reference
implementation, and replacing the hand-rolled transport with it is the alternative if keeping parity by hand proves
costly. Tests: extend the MCP cases in `ApiSuite` with a `server/discover` call, a `2026-07-28` `initialize`, and a
notification that must return 202. Validate end to end with the agent benchmark (see below). Docs: `MCP_API.md`. Risk:
moderate, and the change needs a real client to verify.

### J. Relevance cutoff

Keep the cross-encoder's scores (`RerankWithCrossEncoderAsync` discards them at lines 646-659), add a per-subject
`RerankMinScore` (null means off), drop passages below it, and return `InsufficientSupport = true` from `AnswerAsync`
when none survive, with the existing refusal text. Report the top rerank score in chat telemetry so an operator can pick
the threshold from real traffic. The LLM listwise path has no calibrated score and should not gate. Tests: with
`FakeCrossEncoderReranker` returning low scores, the answer is the refusal and no model call is made. Docs: `REST_API.md`
subject fields, `README.md` "insufficient support". Risk: a threshold set too high refuses answerable questions; default
it off and tune with the harness's AUROC report.

### G. Scores callers can use

Return the normalized fused score as `score` for hybrid results (RRF sum divided by the maximum achievable, so 1.0 means
first in both legs), and add `vectorScore`, `textScore`, `vectorRank`, and `textRank` to `RetrievedChunk` and the
`pneuma_search` result. Add `minScore` to the search routes and tools, applied to the fused score in hybrid mode and the
raw score in single-leg modes. Record citation relevance from the vector score rather than the mixed value. Tests: a
fusion case where a text-only hit and a vector-only hit report scores on the same 0..1 scale. Docs: `REST_API.md`
search response, `MCP_API.md`. Risk: dashboards that display `score` will show different numbers; the admin search
view should show the evidence fields too.

### M. Tenant-wide semantic search

When no subject is given, either resolve the collection's embedding model (from the subjects that write to the resolved
collection, which must agree) or return a 400 for `mode=vector` and a response notice for `mode=hybrid` saying the
vector leg was skipped. Silent fallback is the one option to rule out. Tests: `GET /v1.0/search?mode=vector` with no
subject returns the notice or error, never an empty 200 without explanation. Docs: `REST_API.md`. Risk: low.

### O. Chunk title headers

Embed `"{link title}: {cell title}"` followed by the chunk text, and keep `Content` unchanged so full-text search,
snippets, and grounding are unaffected. Reserve the header's tokens from the chunk budget (see F) and truncate long
titles at a word boundary. The embedding cache must key on the embedded text, not the stored text. Tests: the embedded
text carries the header and still fits the budget. Docs: `README.md` ingestion section. Risk: Isis lost ground on exact
identifier queries (lexical nDCG 0.833 to 0.740) after the same change; Pneuma's text leg is unaffected, but measure the
lexical question type before and after, and consider a header only on a cell's first chunk.

### N. Degraded flag

Record which legs ran and which failed in `RetrievalPool`, surface a `degraded` boolean and a `legs` object in the
search response and the query response, and count failures with a `pneuma_retrieval_leg_failures_total` metric labelled
by leg. Tests: a failing fake vector leg yields `degraded: true` with text results. Docs: `REST_API.md`, `TELEMETRY.md`.
Risk: none.

### Y. Default embedding model by benchmark

Run the retrieval benchmark with `nomic-embed-text`, `mxbai-embed-large`, `embeddinggemma`, and `all-minilm` on the same
corpora and pick the default on paraphrase nDCG and ingest throughput together. Changing the default affects only new
subjects, because a collection's dimensionality is fixed at creation and existing subjects would need re-ingestion.
Docs: `README.md` model section and `FirstBootSeeder` defaults. Risk: larger models slow ingest on CPU-only hosts.

### P. Per-request lookup cache

Load the subject once per request and pass it down (`AnswerAsync`, `GatherAsync`, `GenerateAnswerDetailedAsync`, and
the rerank overload all re-read it), and resolve the three prompts through one `PromptResolver` call per request. A
short-lived process cache for model runners (a few seconds, invalidated on runner update) removes the rest. Avoid caching
credentials or permissions, which would delay revocation, the trade-off Isis declined. Tests: a counting fake database
asserts one subject read per grounded answer. Risk: low.

### T. Passage limits

Raise the `pneuma_search` snippet cap from 1,200 characters to the chunk's full text up to a limit derived from the
subject's `ChunkMaxTokens` (about 5 characters per token), and raise the LLM reranker's 500-character cut to the same
limit or to a token budget. Tests: a subject with 512-token chunks returns whole chunks from the tool. Risk: larger
tool results cost more context in the agentic loop, which already truncates stored tool output to 8,192 characters
(`AgenticChatService.cs:351`).

### Q. Single-call hybrid

Send one RecallDB search with both `Vector` and `FullText` and `Hybrid = { Strategy = "Rrf", RrfK, CandidatePool }`,
reading `VectorScore`, `TextScore`, `VectorRank`, and `TextRank` per hit. RecallDB then raises `ef_search` to the
candidate pool, fixing the under-fill described earlier, and the two round trips become one. Pneuma's separate
`LexicalWeight` and `SemanticWeight` map onto `FullText.TextWeight` as a share. Keep the client-side fusion for the
single-leg modes. Independently, ask RecallDB to raise `ef_search` for vector-only searches whose `MaxResults` exceeds
40 and to enable `hnsw.iterative_scan` when a tag filter is present. Tests: a golden comparison of fused order against
the current client-side fusion on a fixed fake corpus. Risk: moderate, because D, G, and N all touch the same code; do
Q after D.

### S. Concurrency on shared keys

Serialize `SubgraphMerger` node resolution per `(subjectId, nodeType, canonicalName)` with a keyed async lock, and, for
multi-instance deployments, use a database advisory lock or a unique index in LiteGraph if it offers one. Make
`ClaimNextQueuedAsync` atomic in each provider (`UPDATE ... WHERE id = (SELECT ... FOR UPDATE SKIP LOCKED) RETURNING *` on
PostgreSQL, a checked affected-row count elsewhere). Refuse or queue a re-ingest while a job for the same link is
Queued or Processing. Tests: 8 concurrent jobs that share an entity produce one entity node; two concurrent claims never
return the same job. Docs: `README.md` scaling notes. Risk: lock scope; keep it per canonical key so unrelated jobs do not
wait on each other.

### R. Chunker fixes in `Pneuma.Chunking`

Slice token ranges only at character boundaries that round-trip (decode, re-encode, and step back while the decoded text
ends in U+FFFD or a lone high surrogate), and drop any chunk wholly contained in its predecessor, as Isis now does in
`MemoryChunker`. Both changes alter chunk boundaries, so regenerate the parity goldens deliberately and say so in the
changelog. Tests: an emoji-heavy text chunked at a small budget contains no U+FFFD and every original emoji; no chunk is a
substring of the one before it. Risk: existing collections keep their old chunks until re-ingested.

### X. Rerank placement

Rerank the MMR-selected chunk passages before neighbor expansion, then re-apply `OrderForReconstruction` using the rerank
order as the group score, then append expanded neighbors. Tests: expansion nodes never precede chunks after a rerank.
Risk: low.

### U. Cross-link duplicate detection

Store a normalized content hash per chunk as a tag and, in `IndexingStage`, look up existing chunks with the same hash in
the subject before storing. Skip exact duplicates and record the other link as a second provenance for the existing
chunk, or tag them `duplicateOf` so MMR and citations can collapse them. Near-duplicate detection (a vector search at
write time) is the heavier follow-up. Risk: deleting the first link must not orphan the second link's content, so skip
only when both links remain.

### W. Provisioning serialization

Serialize `RecallDbTenantProvisioner.ProvisionAsync` per tenant and collection creation per tenant, and on a failed
create re-list and adopt a collection with the expected name, as Isis does. The RecallDB index-name collision should be
fixed upstream in `DynamicTableQueries.GetIndexIdentifier`. Risk: low; provisioning is rare.

### V. Source-date recency

For corpora where source date matters, tag chunks with the source's published or fetched date and add a weighted third
RRF signal, default 0, as Isis did with `RecencyWeight`. Isis found that recency costs accuracy on undated or
bulk-imported corpora, which describes most Pneuma subjects, so leave it off by default.

## Benchmarking Pneuma

The Isis harness in `C:\Code\AgentMemory\src\Test.Benchmark` can measure Pneuma with an adapter, and the reuse is worth
it: the datasets, metrics (Hit@1, Recall@k, MRR, nDCG@10, answerable-vs-unanswerable AUROC), the LLM judge, the agent
runner, the load runner, the stub embedding server, and the `compare` regression gate all carry over unchanged. The
harness is black-box over REST and MCP, and all Isis-specific calls go through `IsisClient`, which only
`Runners/BenchmarkContext.cs` constructs. Extract an `IBenchmarkTarget` interface from `IsisClient` (connect, provision a
corpus, ingest documents, search, answer) and add a `PneumaTarget` beside it, selected with `--target pneuma`.

The adapter has four jobs that differ from Isis:

- **Provisioning.** Each corpus becomes a subject with its own collection. Create the collection
  (`PUT /v1.0/collections`) with the embedding model's dimensionality, create or reuse the embedding and inference
  model runners (`/v1.0/model-runners`), then create the subject with those runners and the chunk settings under test.
  Categories become link labels, and category-filtered queries pass a `metadataFilter` with a required label.
- **Ingestion.** Pneuma ingests URLs, not bodies, so the harness hosts a small document server on `127.0.0.1`, built
  like `StubEmbeddingServer`, that serves each benchmark document as `text/plain` with its title as the first line.
  Submit links in bulk (`POST /v1.0/subjects/{id}/links/bulk`) with a `benchDocId` tag, then poll `/v1.0/jobs` until every
  job is terminal. Record failures by stage and error, which directly measures findings C, F, R, and S. For dated
  corpora, submit in date order.
- **Search.** Use `GET /v1.0/subjects/{id}/search?q=&mode=&max=`, which already groups chunks by link, and map each link
  back to its document id through the `benchDocId` tag or the URL. Add a second mode that calls the MCP `pneuma_search`
  tool, because the Ask screen uses that path and finding A lives there.
- **Answers.** Grounded chat is `POST /v1.0/query` with `subjectId`; its `sources` map to links for the "evidence reached
  the prompt" metric. The agentic chat is `POST /v1.0/chat/stream`, which needs an SSE reader that collects the final
  answer and the tool-call trace. Measure both, since they retrieve differently today.

The agent benchmark needs only a different MCP config: point Claude Code at `http://127.0.0.1:8080/mcp` with a bearer
API key. It will immediately show whether finding L blocks current clients. The load benchmark can use the stub
embedding server through an `OpenAICompatible` model runner whose base URL is the stub, which speaks `/v1/embeddings`.

Cost is the main difference from Isis. Every Pneuma document goes through DocumentAtom extraction, an LLM
classification call, and summarization before it is embedded, so SciFact's 5,183 abstracts through a 4B local model take
hours. Start with `isis-live` (24 documents) and `atlas` (170), then a LongMemEval sample of about 20 haystacks. SciFact is
still the best sanity check that chunking and embedding cost nothing against the published score, and it is worth one
overnight run. An ingestion option that skips classification and summarization for retrieval-only benchmarks would make
large corpora practical, but it changes what is measured, so report it separately.

Pneuma's own evaluation harness (`EvalService`, LLM-judged ground-truth facts per subject) complements the black-box
harness rather than replacing it: it runs inside the server over the real answer pipeline but reports no ranking
metrics. Add these Pneuma-specific measures to the adapter's report: ingest failure rate by stage, chunks per document,
the share of chunks wholly contained in their predecessor, the count of stored chunks containing U+FFFD, whether the
vector leg actually ran (from N once it exists), and grounded versus agentic answer accuracy on the same questions.

## Checklist

Mark each item with a decision (do, defer, drop), an owner, and notes. Letters match the ranked table.

- [ ] A. Hybrid, subject-aware `pneuma_search` (`McpGraphTools.cs:71-130`, tool description, assistant prompt heal). Decision: ____ Owner: ____ Notes: ____
- [ ] B. Explicit `MatchMode = "Any"` plus startup check (`RecallDbClient.cs:204-214`). Decision: ____ Owner: ____ Notes: ____
- [ ] C. `TextSanitizer` applied at cell extraction, summarization, and link submit. Decision: ____ Owner: ____ Notes: ____
- [ ] D. Pool keyed by chunk `DocumentKey`; `chunkKind` tag for summaries. Decision: ____ Owner: ____ Notes: ____
- [ ] E. Delete the previous job's content after a successful re-ingest; clear partial contributions before a retry. Decision: ____ Owner: ____ Notes: ____
- [ ] H. Parallel legs; batched or bounded-concurrent node resolution. Decision: ____ Owner: ____ Notes: ____
- [ ] I. Stricter `user.answer` and `assistant.system` prompts with `HealPromptAsync`. Decision: ____ Owner: ____ Notes: ____
- [ ] K. `Routes.Exception`, status mapping, generic 500 messages, `PooledConnectionLifetime` on `IntegrationHttp`. Decision: ____ Owner: ____ Notes: ____
- [ ] F. Per-runner tokenization profile, 4% margin, re-chunk at 0.75/0.5/0.3, hard-fail classification, long-query embedding. Decision: ____ Owner: ____ Notes: ____
- [ ] L. MCP negotiation, `server/discover`, `2026-07-28` fields, 202 for notifications (or adopt Voltaic 1.1.0). Decision: ____ Owner: ____ Notes: ____
- [ ] J. `RerankMinScore` per subject, refusal when nothing survives, top score in telemetry. Decision: ____ Owner: ____ Notes: ____
- [ ] G. Normalized fused score, per-leg evidence fields, `minScore`. Decision: ____ Owner: ____ Notes: ____
- [ ] M. Tenant-wide search resolves an embedding model or says the vector leg was skipped. Decision: ____ Owner: ____ Notes: ____
- [ ] O. Title header in embedded text only; measure lexical questions. Decision: ____ Owner: ____ Notes: ____
- [ ] N. `degraded` flag, `legs` object, leg-failure metric. Decision: ____ Owner: ____ Notes: ____
- [ ] Y. Benchmark candidate embedding models and pick a default for new subjects. Decision: ____ Owner: ____ Notes: ____
- [ ] P. One subject read and one prompt resolution per request; short runner cache. Decision: ____ Owner: ____ Notes: ____
- [ ] T. Chunk-aware snippet and reranker passage limits. Decision: ____ Owner: ____ Notes: ____
- [ ] Q. Single-call RecallDB hybrid; upstream request for vector-only `ef_search` and iterative scans. Decision: ____ Owner: ____ Notes: ____
- [ ] S. Keyed lock for entity merges, atomic job claim, one active job per link. Decision: ____ Owner: ____ Notes: ____
- [ ] R. Surrogate-safe slicing and contained-tail dedupe; regenerate parity goldens. Decision: ____ Owner: ____ Notes: ____
- [ ] X. Rerank before expansion and keep reconstruction order. Decision: ____ Owner: ____ Notes: ____
- [ ] U. Chunk content-hash tag and cross-link duplicate handling. Decision: ____ Owner: ____ Notes: ____
- [ ] W. Serialized provisioning with adopt-on-conflict; upstream RecallDB index-name fix. Decision: ____ Owner: ____ Notes: ____
- [ ] V. Optional source-date recency signal, default off. Decision: ____ Owner: ____ Notes: ____
- [ ] Benchmark adapter: `IBenchmarkTarget`, `PneumaTarget`, document server, SSE reader for agentic chat, Pneuma-specific report fields. Decision: ____ Owner: ____ Notes: ____
- [ ] Baseline run on `isis-live` and `atlas` before any fix lands, so every change above has a before and after. Decision: ____ Owner: ____ Notes: ____
