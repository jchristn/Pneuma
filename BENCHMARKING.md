# Benchmarking Plan

This plan builds a benchmark suite for Pneuma that matches the one in `C:\code\agentmemory\benchmarks` (the Isis
agent-memory platform) in rigor. The goal is to measure Pneuma's retrieval and grounded answering against industry
standards, find where it is weak, and use those numbers to decide what to improve next.

Isis and Pneuma have a lot in common: C# on Watson, RecallDB for vector and full-text storage, Ollama embeddings
through a provider-neutral layer, grounded chat, and an MCP surface. Most of the Isis harness carries over, but Pneuma
differs from Isis in six ways that matter for measurement. This plan covers what to reuse, what to change, the
product changes Pneuma needs before it can be measured properly, and the order to do the work in.

Status legend: ⬜ not started · 🟨 in progress · ✅ done. The harness, datasets, and Phase 0 product changes are
implemented; see §12 for where the implementation departs from this plan, and `benchmarks/README.md` and
`benchmarks/RESULTS.md` for how to run it and what it measured.

---

## 1. What the Isis suite does, and what Pneuma needs from it

The Isis harness (`agentmemory/src/Test.Benchmark`, about 5,100 lines) is a **black-box CLI**. It talks to the system
under test only over REST and MCP, never references its assemblies, and runs against an isolated container stack on
non-default ports. Its commands:

| Isis command | What it measures | Pneuma equivalent |
|---|---|---|
| `retrieval` | Hit@1, Recall@1/5/10, All@5/10, MRR@10, nDCG@10 per search mode and query type; latency; server-side stage breakdown from Prometheus; AUROC of the top score on answerable vs unanswerable questions | **Core of this plan.** Reuse as-is, plus a reference arm (§4) and confidence intervals (§6) |
| `chat` | Answer accuracy from an LLM judge, abstention, citation precision and recall, whether the evidence reached the prompt | `answer` (`/v1.0/query`) and `chat` (`/v1.0/chat/stream`) |
| `agent` | Claude Code run headless, with and without the MCP server, on tasks that depend on the corpus | Same, against Pneuma's `POST /mcp` |
| `load` | Closed-loop throughput and p50/p95/p99 per concurrency level, optionally with a stub embedding server | Same, plus an ingest-throughput scenario |
| `compare` | Diffs two retrieval reports and exits non-zero on a regression, for CI | Same, plus bootstrap confidence intervals |
| `prepare` | Converts BEIR and LongMemEval into one neutral dataset format | BEIR plus MultiHop-RAG (§3) |
| `stub` | A deterministic embedding server | Embedding stub plus an **inference stub** (§5.3) |

Four practices account for most of the value of the Isis suite, and the Pneuma suite keeps all four:

1. **Every report records its provenance**: the git commit (with `-dirty`), the machine, the endpoints and the full
   configuration. Reports go to a git-ignored `results/` directory, and the numbers that matter are copied into a
   committed `RESULTS.md`.
2. **Results are broken down by question type.** Overall nDCG hid Isis's real problems (superseded facts,
   paraphrase, multi-memory questions, abstention). The per-type tables found them.
3. **Work happens in rounds.** Round 0 is the baseline. Each later round is a set of fixes drawn from a
   value-and-simplicity-scored table (`RETRIEVAL_IMPROVEMENTS.md`), re-measured on the same datasets, with ablation
   sweeps for any new parameter. Isis chose its recency weight by sweeping 0, 0.03, 0.05, 0.1 and 0.2 on all four
   datasets.
4. **Defects count as findings.** Thirteen of the Isis suite's findings were bugs (ingest races, token-budget
   overflows, a broken keyword leg, chat grounding on 240-character snippets), not tuning problems. Plan for that.

## 2. How Pneuma differs, and what that means for the harness

These are confirmed from the Pneuma source.

| # | Difference | Effect on benchmarking | Handling |
|---|---|---|---|
| D1 | **Ingestion only takes URLs.** `POST /v1.0/subjects/{id}/links` accepts a URL that `HttpContentFetcher` downloads. There's no upload route and no raw-text route. | A dataset can't be pushed directly. | The bench stack runs a static **corpus server** (nginx) serving `benchmarks/data/corpus/`. The harness writes each document as a file and submits its URL. As a side effect, DocumentAtom's type detection and extraction get tested too. |
| D2 | **Ingestion is LLM-heavy.** Each document goes through DocumentAtom, ontology classification, a summary per cell, and a graph merge. | SciFact's 5,183 abstracts would take more than 10k LLM calls on a local model, which is hours. Isis ingests at about 70 embeddings/s with no LLM involved. | Two ingest profiles, recorded in every report: `full` (real inference model) for small and medium datasets, and `lean` (inference stub, §5.3) for large public sets. `lean` numbers measure only the text and vector legs. |
| D3 | **LLM summaries are indexed alongside content.** `ChunkingStage` chunks both the extracted cells and their summaries, and both carry the same `litegraphNodeId`. | This could help paraphrase questions, or it could crowd out content chunks and hurt lexical ones. Nobody knows which. | It's a measured ablation (§7), which needs product change P7 (§8). |
| D4 | **Hits come at three granularities.** `/v1.0/search` returns one hit per cell, de-duplicated by graph node, with no `linkId`. `/v1.0/subjects/{id}/search` returns one hit per document, with `linkId`. `/v1.0/query` returns graph nodes with no score and no `linkId`. | Relevance labels are per document. Passage-level evidence needs text matching. | Score documents on subject search. Score evidence by matching gold evidence spans against retrieved text (§6.2). Map `/v1.0/query` sources to documents through `assertedByJob` → job → `linkId` until P1 lands. |
| D5 | **Retrieval settings are mostly global.** RRF k and weights, MMR, λ, neighbor expansion and pool size live in `RetrievalSettings` in `pneuma.json`. Only rewrite, rerank and chunking are per subject. | An ablation would need a server restart per setting. | Per-request retrieval overrides (P4), limited to admins. Until then, the harness restarts the bench server with a generated `pneuma.json` for each sweep point. |
| D6 | **Pneuma has a graph, and Isis doesn't.** Neighbor expansion (off by default), multi-hop subgraphs, and community summaries with `/v1.0/query/global`. | Isis's datasets have no multi-hop entity questions or thematic questions, so they can't show whether the graph adds anything. | New question types, `multihop` and `global` (§3), and the MultiHop-RAG public set. |

Pneuma already ships several things that Isis still lists as "later": RRF fusion, MMR, an LLM or cross-encoder
reranker, query rewrite and an embedding cache. None of them has been measured. Round 0 should find out which ones
actually help.

## 3. Datasets

The harness uses **Isis's neutral JSON format** (`BenchmarkDataset` → corpora → documents and labelled queries), with
new fields that are all optional. Because the format is shared, **Isis's `atlas.json` loads unchanged**, which gives a
direct Isis-vs-Pneuma comparison on the same corpus and questions.

New fields:

- Document: `format` (`md`, `html`, `txt` or `pdf`, which picks the file extension the corpus server uses), plus
  `labels` and `tags`, which are passed through to the link.
- Query: `evidence` (verbatim gold spans for passage-level scoring), `hops` (how many documents the answer needs),
  and `filter` (labels and tags, testing `metadataFilter`).
- Query type vocabulary: Isis's types (`paraphrase`, `lexical`, `multi`, `detail`, `category`, `negative`,
  `confusable`, `superseded`) plus two for Pneuma: `multihop` (the answer comes from following an entity relationship
  across documents that share no vocabulary with the question) and `global` (a thematic question over the whole
  subject).

| Dataset | Committed? | Size | Profile | What it tests | External reference |
|---|---|---|---|---|---|
| `pneuma-live.json` | yes | about 30 docs (Pneuma's own `*.md`: README, REST_API, MCP_API, CKG_IMPROVEMENTS, TELEMETRY, CLAUDE.md, …), about 120 questions | full | The counterpart of isis-live. Real technical documents with every Isis question type plus `filter`. It also backs the `agent` tasks. | none (internal) |
| `meridian.json` (synthetic) | yes | about 150 docs in mixed md, html and pdf, about 300 questions | full | The counterpart of Atlas, written for Pneuma's domain. It's an entity-rich subject (people, organizations, works and events for a fictional institution) spread across formats, so type detection, extraction, entity resolution (duplicate spellings on purpose) and relationships all get exercised. Question types: all of the above, plus `multihop` and `global`. Written the way Atlas was, with questions produced by a separate pass that never saw the corpus prompts. | none (internal) |
| `atlas.json` (from Isis) | copied from Isis | 170 docs, 260 questions | full | A head-to-head with Isis on identical data. Pneuma has no recency signal, so expect it to trail Isis on `superseded`. That's useful to know. | Isis RESULTS.md, round 2 |
| **SciFact** (BEIR) | downloaded | 5,183 docs, 300 queries | lean | **The sanity anchor.** Published numbers exist for BM25 and for the embedding models. If Pneuma's vector leg falls below the model's published score, the ingest path is losing information. | BEIR / MTEB leaderboard |
| **NFCorpus** (BEIR) | downloaded | 3,633 docs, 323 test queries | lean | A second BEIR anchor with graded relevance, which exercises graded nDCG. | BEIR / MTEB leaderboard |
| **MultiHop-RAG** | downloaded | 609 news articles, 2,556 queries (use a stratified sample of about 300) | full | **The industry-standard multi-document RAG benchmark.** It has inference, comparison, temporal and null (unanswerable) queries with gold evidence and answers. It's small enough for the full profile, and it's the fairest outside test of the graph and multi-document path. | MultiHop-RAG paper (Tang & Yang, 2024): retrieval Hits@4/@10, MRR@10, MAP@10; answer accuracy |

Notes:

- **Verify every published reference number before quoting it**, including the exact model and version, from the
  BEIR and MTEB leaderboards and the MultiHop-RAG paper. Record the source in RESULTS.md. Compare only like with like:
  same model, same split, same metric definition. nDCG uses linear gain, as pytrec_eval does.
- Public data is downloaded at run time and never committed. Check licenses upstream before publishing anything
  derived from it.
- `global` questions have no gold ranking. Score them with the GraphRAG paper's method: a pairwise LLM judgement of
  comprehensiveness, diversity and directness between `/v1.0/query/global` and the local `/v1.0/query` answer, in
  both presentation orders to cancel position bias. Report these numbers separately and label them as weaker
  evidence than the rest.
- The two `sample-data-sets/*.txt` files are lists of live URLs. They can't be used for benchmarking because pages
  change. If they're wanted as a smoke corpus, snapshot the pages into `benchmarks/data/corpus/`.

## 4. What Pneuma is compared against

"Industry standard" has three meanings here. The harness reports all three.

1. **Published numbers on public datasets** (SciFact, NFCorpus, MultiHop-RAG). These answer "is Pneuma in the
   expected range at all?"
2. **A reference arm computed by the harness.** The same documents and queries go through a plain RAG baseline
   built into the harness, with no Pneuma code involved:
   - **BM25** over the raw document text (k1 = 0.9, b = 0.4, the Pyserini/Anserini defaults; about 150 lines of C#).
     Validate it by reproducing the published BEIR BM25 score on SciFact to within about 0.01.
   - **Dense**: brute-force cosine similarity using **the same embedding endpoint Pneuma uses**, over fixed
     256-token chunks with the document's max score as its score.
   - **Hybrid**: RRF (k = 60) of the two.

   The gap between Pneuma and this arm is **what Pneuma's pipeline adds or costs**: DocumentAtom extraction, cell
   chunking, summary chunks, fusion, MMR, the graph, rerank. It's the most useful comparison for deciding where to
   work, because the model is the same on both sides and only Pneuma's pipeline differs. Isis had no reference arm,
   and SciFact was its only sanity check. This is where the Pneuma suite goes beyond parity.
3. **Isis on atlas.json.** A same-hardware, same-data comparison with a sibling system that uses the same retrieval
   store.

Optional later work: run LightRAG or Microsoft GraphRAG on MultiHop-RAG and `meridian` as outside graph-RAG
comparators. It's expensive, so only do it if the reference arm shows the graph path is worth investing in.

## 5. Harness design

### 5.1 Layout

```
benchmarks/
  README.md                how to stand up, run and read the suite
  RESULTS.md               committed numbers, round by round
  docker/compose.yaml      the isolated bench stack (§5.2)
  docker/pneuma.bench.json the bench server config (generated per sweep point, §2 D5)
  datasets/                committed datasets (pneuma-live, meridian, atlas)
  agent/tasks-pneuma.json  agent tasks
  data/                    git-ignored: downloads, converted sets, the served corpus
  results/                 git-ignored: <utc>-<kind>-<name>.json and .md
  run-baseline.{sh,bat}
  start-bench-server.{sh,bat}
src/Test.Benchmark/        black-box CLI; not referenced by, and not referencing, Pneuma assemblies
RETRIEVAL_IMPROVEMENTS.md  the scored fix backlog, linked from RESULTS.md
```

`src/Test.Benchmark` joins `Pneuma.sln` so it builds, but it has **no project reference** to `Pneuma.Core` or
`Pneuma.Server`. It follows this repo's C# rules (§CLAUDE.md), which are stricter than the Isis harness in places: no
`var`, classic `using (...) { }` blocks rather than using declarations, and typed DTOs rather than `JsonObject`.
Console output is fine here because it's a CLI, not library code. Port the Isis files directly where they fit
(`RetrievalMetrics`, `LatencyStats`, `PrometheusSnapshot`, `ReportWriter`, `ResultComparer`, `BeirConverter`,
`DatasetStore`, `StubEmbeddingServer`, `AgentRunner`), then restyle them.

### 5.2 Isolated bench stack

`benchmarks/docker/compose.yaml` (project name `pneuma-bench`) runs on non-default ports, bound to `127.0.0.1`. It
never touches `docker/` or a live deployment, and `down -v` throws away all benchmark data.

| Service | Bench port | Notes |
|---|---|---|
| postgres (pgvector) | 25432 | Backs Pneuma, RecallDB and LiteGraph |
| recalldb-server | 28600 | **Same image as `docker/compose.yaml`** |
| litegraph | 28701 | v9.0.0 |
| documentatom | 28000 | v3.0.0 |
| corpus (nginx) | 28090 | Serves `benchmarks/data/corpus/` read-only (D1) |
| stub | 28434 | Embedding and inference stub (§5.3), started by the harness when needed |
| Pneuma server | 28080 | **Built from the working tree** by `start-bench-server`, so benchmarks always measure the code in front of you. `/metrics` is on the same port. |

Less3 and the observability stack are left out: artifact storage is best-effort, and the harness scrapes `/metrics`
itself. Ollama runs on the host (`127.0.0.1:11434`) and is shared with development. Record its version and the model
digests in the report.

### 5.3 Stubs

- **Embedding stub.** Port the Isis feature-hashing stub. It speaks the Ollama and OpenAI formats and has a
  configurable latency. It's for `load`.
- **Inference stub** (new). It returns the smallest valid ontology classification (one `Topic` cell per atom, no
  entities) and an empty summary, so the `lean` ingest profile never calls an LLM. It must speak whichever PolyPrompt
  provider format the bench model runner is configured with. Reports made with it are labelled
  `ingestProfile: lean`.

### 5.4 Provisioning (the Isis `ScopeProvisioner`, adapted)

- Create one bench **tenant**. Each dataset corpus gets one **subject**, named deterministically
  (`bench-{dataset}-{corpus}-{embeddingModel}-{suffix}`), with its own RecallDB collection created at the embedding
  model's dimensionality. If a subject with that name already exists and has the same document count, reuse it.
  `--reingest` rebuilds it.
- Write each document to `data/corpus/{dataset}/{corpus}/{docId}.{ext}`, then submit it with `title`, `labels` and
  `tags` from the dataset. Record the `docId → linkId` mapping in the report.
- Poll `GET /v1.0/links/{id}` until the link is `Ingested` or `Failed`. Report failures by stage from the job events.
  **Don't hide failures**: the Isis round-0 SciFact run silently lost 19% of its documents to a token-budget bug.
  Any ingest failure is reported at the top of the report and marks the run invalid for comparison.
- Dated corpora (Atlas, MultiHop-RAG) are submitted in date order, one document at a time, the way Isis does it. It
  doesn't matter to Pneuma today, since there's no recency signal, but it keeps datasets comparable if one is added.
- Warm the embedding model with `POST .../search/warmup` before timing anything.

### 5.5 Commands

| Command | Route(s) | Output |
|---|---|---|
| `prepare --format beir\|multihoprag` | offline | A neutral dataset JSON, with stratified `--limit` and `--seed` |
| `retrieval` | `GET /v1.0/subjects/{id}/search` (documents) and `GET /v1.0/search` (cells and evidence) | §6.1 metrics for each mode (`text`, `vector`, `hybrid`) plus the reference arm, broken down by type, with latency, stage breakdown and AUROC |
| `answer` | `POST /v1.0/query` (and `/query/stream` with `--stream`) | §6.3 metrics |
| `chat` | `POST /v1.0/chat/stream` (agentic, with `citations[].linkId`) | §6.3 metrics plus tool-call count and time to first token |
| `global` | `POST /v1.0/subjects/{id}/communities/build`, `/v1.0/query/global` vs `/v1.0/query` | Pairwise judge win rates (§3) |
| `agent` | Claude Code headless with Pneuma's `POST /mcp` vs no memory, in an empty temp directory with built-in tools disabled | Success rate, turns, cost |
| `ingest` | Link submission plus job polling | Documents/s, per-stage p50 and p95 from `pneuma_ingestion_stage_duration_seconds`, failure rate by stage, chunks per document, share of summary chunks, graph node and edge counts, duplicate-entity rate (§6.4) |
| `load` | A search, query and link-submit mix at concurrency 1, 4, 16 and 64, with or without stubs | Throughput, p50/p95/p99, error rate |
| `compare` | offline | Deltas with 95% bootstrap CIs; non-zero exit on a regression (§6.5) |
| `stub` | — | Runs the stubs on their own |

## 6. Metrics

### 6.1 Retrieval (document level; parity with Isis)

For each mode and each query type: **Hit@1, Recall@1/5/10, All@5/10, MRR@10, nDCG@10** (linear gain, with graded
relevance where the dataset has it). MultiHop-RAG also reports **Hits@4 and MAP@10** so the numbers line up with its
paper. Also reported: client latency p50/p95/p99, error count, and mode mismatches.

**Abstention signal.** AUROC of the top-hit score on answerable vs `negative` questions (Mann-Whitney U, ties count
half), computed once per score field Pneuma returns. Today that's one mixed score (§8, P2). Once P2 lands, compute it
separately on the fused score, `vectorScore` and the reranker score. Isis found its fused score was nearly useless
here (0.57–0.67), while raw vector similarity was better (up to 0.78). Pneuma should expect the same.

### 6.2 Evidence (passage level; new)

Document-level recall overstates quality when the right document comes back but the chunk holding the answer
doesn't. For queries with `evidence` spans:

- **Evidence@k**: the share of gold spans whose normalized text (lower-cased, whitespace-collapsed) appears in a
  retrieved snippet within the top k. Fall back to 80% token-overlap recall for spans that were split across chunks.
  Computed over `/v1.0/search` cell hits.
- **Summary-hit share**: the share of top-k hits that are summary chunks rather than content chunks. This needs P7.

### 6.3 Answering (parity with Isis `chat`, plus faithfulness)

- **Accuracy**: an LLM judge with a fixed prompt is given the question, the gold answer and the response, and answers
  yes or no. The judge is called **directly**, never through Pneuma.
- **Abstention**: the share of correct declines on `negative` and `null` questions. The judge decides whether the
  response declined, alongside `grounded:false`.
- **Evidence in context**: whether any gold document or evidence span is among the sources sent to the model.
  Accuracy is reported **separately for questions where the evidence was in context and where it wasn't**, to tell
  retrieval failures from generation failures. This split was the most useful diagnostic in the Isis suite.
- **Citation precision and recall**: map `[n]` markers to `sources[n]` and then to documents (or use
  `citations[].linkId` from `chat`), and compare against the gold documents.
- **Faithfulness** (new): the judge splits the answer into claims and checks each one against the source text sent
  to the model. The score is the share of supported claims. It catches hallucination that happens to be correct,
  which the accuracy score misses. This follows the RAGAS definition, implemented directly rather than with the
  library.
- **Existing eval harness.** `/v1.0/eval` stays as the operator feature. The benchmark doesn't use it, for two
  reasons: its judge is the subject's own inference model, and its facts carry no evidence ids. The harness can
  optionally **export** a dataset's questions as `EvalFact`s so operators can re-run them from the dashboard.
- **Judge hygiene**: use a stronger judge than the answering model for headline numbers, fix the judge prompt and
  model version in the report, and hand-check about 50 verdicts per round. Record the agreement rate in RESULTS.md.
  Use a non-reasoning model, or turn thinking off.

### 6.4 Ingest fidelity (new; Isis has no graph)

- **Success rate** by stage, with the error taxonomy.
- **Extraction coverage**: the share of the source document's tokens that appear in its stored content chunks. This
  catches DocumentAtom losing tables, headers or PDF text.
- **Chunk statistics**: chunks per document, the token-length distribution, the share that are summary chunks, and
  the share that are redundant (wholly contained in the previous chunk). Isis found 9% redundant tail chunks this way.
- **Graph quality** on `meridian`, where the gold entity list is known: entity precision and recall, the
  duplicate-entity rate (several nodes for one gold entity, which is what CKG #9 targets), and relationship
  precision and recall for the planted relationships.

### 6.5 Rigor (goes beyond Isis)

Isis compares point estimates against a fixed tolerance of 0.01. With 300 queries that's inside the noise. The
Pneuma `compare` command should:

- report a **paired bootstrap 95% CI** for each metric delta (10,000 resamples over queries, with a fixed seed), and
  flag a change only when the CI excludes zero **and** the delta is larger than the tolerance;
- run each LLM-judged benchmark **three times** and report the mean and spread, since Isis saw a few points of judge
  variance between runs;
- carry per-query outcomes in every report, so any delta can be traced to the queries that changed.

## 7. Ablation matrix

Each of these is a measured sweep on `pneuma-live`, `meridian`, SciFact (lean) and MultiHop-RAG, re-using the ingested
subjects wherever the setting only affects queries.

| Knob | Values | Scope | Needs |
|---|---|---|---|
| Mode | text, vector, hybrid | query | nothing (exists) |
| RRF k; lexical and semantic weights | k ∈ {20, 60, 100}; weights ∈ {0.5/1, 1/1, 1/0.5} | query | P4 |
| MMR | off, λ ∈ {0.5, 0.7, 0.9}; bag-of-words vs embedding similarity | query | P4 (and P9 for embedding similarity) |
| Candidate pool | max × {2, 4, 8} | query | P4 |
| Prompt rewrite | off, on | subject | exists |
| Reranker | none, LlmListwise, CrossEncoder (for example a local TEI `bge-reranker`) | subject | exists |
| Neighbor expansion | off, 1 hop, 2 hops; max nodes ∈ {5, 10} | query | P4 |
| Index summary chunks | on, off | ingest (`--scope-suffix`) | P7 |
| Chunking | FixedTokenCount / SentenceBased / ParagraphBased; size ∈ {128, 256, 512}; overlap ∈ {0, 32, 64} | ingest | exists (per subject) |
| Embedding model | all-minilm (384), nomic-embed-text (768), mxbai-embed-large or bge-m3 (1024), plus one hosted model (OpenAI or Voyage) | ingest | exists |

Chosen defaults must be supported by the sweep across **all** datasets. Isis's recency sweep showed that a setting
that helps one corpus can cost another about a point.

## 8. Product changes needed before Pneuma can be measured well

These are the Pneuma equivalents of Isis's round-2 items #10 and #5. Each one is small, and each makes a benchmark
signal possible or trustworthy. Every change needs Test.Shared coverage.

| # | Change | Why | Where |
|---|---|---|---|
| P1 | Add `linkId` (and `documentId` and chunk `position`) to `/v1.0/search` hits and to `QueryResponse.sources` | Without it, grounded answers can't be scored against document labels, and the harness has to reconstruct them from `assertedByJob` | `SearchRoutes.cs:~109` (currently dropped); `QueryResponse` |
| P2 | Return **per-hit evidence**: `fusedScore` (RRF, normalized to 0..1), `vectorScore`, `textScore`, `vectorRank`, `textRank`, `rerankScore` | Today `score` is the best raw score from either leg (cosine or TsRank), which isn't the value results are ranked by, and it isn't comparable across queries. Needed for AUROC and for any future "nothing relevant" threshold. | `GroundedQueryService.GatherAsync` / `RetrievedChunk`; search DTOs |
| P3 | **Suspected defect: subject search throws away the hybrid ranking.** `SearchRoutes.cs:174` re-sorts documents by `Best.Score`, the raw leg score, so in hybrid mode documents are ordered by a mix of cosine and TsRank values rather than by RRF. Cosine values usually sit well above TsRank, so the ranking probably leans on the vector leg. | Confirm it in round 0 (compare doc-level hybrid with the RRF order of the same hits), then order groups by the best **fused** score | `SearchRoutes.cs:169-174` |
| P4 | **Per-request retrieval overrides** (`rrfK`, `lexicalWeight`, `semanticWeight`, `diversityEnabled`, `diversityLambda`, `poolSize`, `neighborExpansion*`), admin-only, on search and query | Lets ablations run without restarting the server (D5). Isis passes `recencyWeight` and `minScore` per request for the same reason. | `QueryRequest`, the search query parameters, and `GroundedQueryService` |
| P5 | Stage histograms on `/v1.0/query` and `/v1.0/search` (`rewrite`, `text_leg`, `embed`, `vector_leg`, `fusion`, `mmr`, `neighbor_expand`, `rerank`, `generate`) | Today only chat records stages, so the query path's latency can't be broken down | `PneumaMetrics`; `GroundedQueryService` |
| P6 | Serialize `insufficientSupport` on `QueryResponse` (it's already set internally) | Abstention can be scored without relying on the model's wording | `QueryResponse.cs` |
| P7 | Tag each chunk with `chunkKind: content\|summary`, and add a per-subject `IndexSummaries` setting (default on) | Makes the summary-chunk share measurable and the D3 ablation possible | `ChunkingStage`, `IndexingStage`, `Subject` |
| P8 | `/v1.0/query/stream` parity: it skips rewrite and rerank and uses the tenant's answer model instead of the subject's | The streaming and non-streaming answers differ today. Either fix it, or benchmark both and document the gap. | `QueryRoutes.cs:~258-290` |
| P9 | Optional MMR similarity over **embeddings** (it's bag-of-words today) | A paraphrased near-duplicate isn't caught by bag-of-words. Keep whichever the sweep favors. | `GroundedQueryService` MMR (`:1139-1194`) |
| P10 | Remove or wire up `RetrievalSettings.VectorTopK` (no code reads it) | A dead setting is misleading in a report's config dump | `RetrievalSettings.cs` |

Round 0 should also check one known risk: **whether the RecallDB image matches any query term in full-text search,
or requires all of them.** Before its upstream patch, Isis's keyword leg scored nDCG 0.07 because RecallDB required
every query term. Pneuma pins the same `recalldb-server:v0.2.1` image, so the `text`-mode numbers in round 0 will
show whether it has the patch.

## 9. Hypotheses Round 0 should confirm or rule out

These predict where effort will pay off. They're written down in advance so the baseline can confirm or rule each one
out, rather than being read after the fact to fit.

1. The text leg is weak (the RecallDB any-term issue) or fine. The `text`-mode nDCG answers it.
2. Doc-level hybrid ranking follows the vector leg (P3). Compare `hybrid` and `vector` doc-level rankings.
3. Summary chunks help `paraphrase` and hurt `lexical` and `detail`, the same trade-off Isis saw with chunk headers
   (lexical Hit@1 fell from 0.77 to 0.57).
4. Pneuma's full pipeline trails the plain reference arm on SciFact (lean). If it does, the loss is in extraction,
   chunking or fusion, and extraction coverage (§6.4) will say which.
5. Neighbor expansion (off by default) helps `multihop` on `meridian` and MultiHop-RAG, and costs precision on
   everything else.
6. LLM listwise rerank helps Hit@1 but costs seconds. A cross-encoder gets most of the gain for about 100 ms and gives
   a calibrated score for abstention.
7. The top score can't separate answerable from unanswerable questions (AUROC below 0.7), as in Isis.
8. On `atlas`, Pneuma is level with Isis overall but behind on `superseded`, because it has no recency or
   supersession signal.
9. MultiHop-RAG `comparison` and `temporal` queries are the weakest types. That would favor CKG #10 (query
   decomposition) as the next big item.

## 10. Phased plan

### Phase 0: Instrumentation (product changes)
- ✅ P1 `linkId` on query sources (as a `linkId` tag) and on `/v1.0/search` hits, plus `documentId` and `position`
- ✅ P2 per-hit score evidence: normalized `fusedScore`, `vectorScore`, `textScore`, `vectorRank`, `textRank`, `chunkKind` (rerank score not yet: the rerankers do not return scores)
- ✅ P3 doc-level hybrid sort confirmed by round 0 (hybrid equalled vector on every dataset) and fixed; `granularity=chunk` added for passage-level scoring
- ✅ P4 per-request retrieval overrides (admin-only): `overrides` on query bodies, same-named query parameters on search
- ✅ P5 `pneuma_retrieval_stage_duration_seconds` (text_leg, embed, vector_leg, mmr, neighbor_expand, rewrite, rerank, generate) and `pneuma_retrieval_leg_failures_total`
- ✅ P6 `insufficientSupport` serialized on `QueryResponse`
- ✅ P7 `chunkKind` tag (content or summary); summaries are switched off per subject through the existing `summarizationMinCellLength` override instead of a new schema column
- ✅ P8 `/v1.0/query/stream` now shares the rewrite, retrieval, rerank, and subject-model path with `/v1.0/query`
- ⬜ P9 embedding-based MMR similarity
- ✅ P10 dead `VectorTopK` setting removed (code, docker configs, dashboard tip)
- ✅ Test.Shared cases: fused score and leg evidence, overrides, source link ids, text sniffing, type-detection fallback, chunk kinds, request-timeout reporting, model-client timeout floor

### Phase 1: Harness skeleton and retrieval parity
- ✅ `benchmarks/docker/compose.yaml` bench stack; `start-bench-server`; `docker/pneuma.bench.json` (corpus server moved into the harness, see §12)
- ✅ `src/Test.Benchmark` (assembly `Pneuma.Benchmark`): arguments, environment capture, typed Pneuma client, dataset store, JSON and Markdown reports
- ✅ Provisioner: subject and collection, served documents, link submission, job polling, failures by stage, reuse and `--reingest`
- ✅ `retrieval` with the Isis metric set plus Hits@4, MAP@10, Evidence@10, bootstrap intervals, per-type breakdown, AUROC, Prometheus stage deltas
- ✅ Reference arm (BM25, dense, RRF); BM25 validated: SciFact 0.676 (published 0.665 to 0.679), NFCorpus 0.321 (published 0.322 to 0.325)
- ✅ `compare` with paired bootstrap CIs
- ✅ `prepare` for BEIR (SciFact, NFCorpus) and MultiHop-RAG
- ✅ Stub model server (embeddings and chat, Ollama and OpenAI formats); the `lean` ingest profile

### Phase 2: Datasets
- ✅ `pneuma-live.json`: 62 docs (Pneuma's docs split at H2), 130 questions of seven types, every label and evidence span script-validated
- ✅ `meridian.json`: 110 docs in md, html, and txt (no pdf), 261 questions of eleven types, gold entities, relationships, chains, superseded facts; questions written by a separate pass
- ✅ `atlas.json` copied from Isis
- ⬜ Label spot-check by two people on a 10% sample

### Phase 3: Answering, agents, ingest and load
- ✅ `answer` (`--endpoint query|chat`): judge client, evidence-in-context split, citation P/R, `--faithfulness`, `--repeat`
- ✅ `global` pairwise runner
- ✅ `agent` runner and `agent/tasks-pneuma.json` (24 tasks); Claude Code 2.1.281 connects to Pneuma's MCP endpoint (it bridges the older protocol revision Pneuma declares). Not yet run: it spends API credits
- ✅ `ingest` runner (failures by stage, stage timings, extraction coverage, chunk statistics, summary share, redundant chunks, U+FFFD); graph quality not yet (Pneuma has no REST route to enumerate a subject's graph)
- ✅ `load` runner (closed-loop search; `--stub` for stub embeddings)
- ✅ `run-baseline.{sh,bat}` (`AGENT=1` opts into the paid run)

### Phase 4: Round 0 baseline
- ⬜ Run the full suite; write `benchmarks/README.md` and `benchmarks/RESULTS.md` (setup, hardware, per-dataset and per-type tables, reference arm, Isis head-to-head, defects found)
- ⬜ Settle each §9 hypothesis
- ⬜ Record every defect found (the Isis precedent: most early wins are bugs)

### Phase 5: Improvement rounds
- ⬜ Write `RETRIEVAL_IMPROVEMENTS.md`: every candidate fix scored 1–10 for value and simplicity, with the weak area it targets and a status column. Seed it with the round-0 findings and the open CKG items (#9 entity resolution, #10 query decomposition and routing), plus the §7 sweeps that were winners.
- ⬜ Round 1: every fix with simplicity ≥ 8; re-measure; sweep any new parameter across all datasets before choosing a default
- ⬜ Later rounds, repeated as above; RESULTS.md keeps a table with one column per round
- ⬜ CI gate: `compare` against the committed baseline on `pneuma-live` and `meridian` (stubbed, fast)

## 11. What parity means

Pneuma reaches parity with the Isis suite when it has:

- a black-box harness, an isolated bench stack, and deterministic, reusable subjects
- retrieval, answer/chat, agent, load, compare, prepare and stub commands
- at least one committed real-document dataset and one synthetic dataset, both with typed questions and negatives,
  and at least one public anchor with published reference numbers
- per-type breakdowns, AUROC abstention analysis, the evidence-in-context split and server-side stage breakdowns
- a RESULTS.md with a round-0 baseline and at least one measured improvement round driven by a scored
  RETRIEVAL_IMPROVEMENTS.md

Four things go **beyond** Isis: the reference arm, bootstrap confidence intervals, faithfulness scoring, and
ingest and graph fidelity metrics. Pneuma needs them because its pipeline has more stages that could quietly cost
quality (extraction, summarization, the graph), and a reference arm is the only direct way to measure what each
stage costs.

## 12. Where the implementation departs from this plan

- **Corpus server inside the harness, not an nginx container.** The bench Pneuma server runs on the host, so the
  harness serves documents itself on `127.0.0.1:28090` while ingestion runs. There is no extra container, and the
  served bytes are exactly what the reference arm indexes.
- **Default tenant, not a bench tenant.** The bench stack is isolated already, so the harness logs in as the seeded
  administrator and works in its tenant. The admin API key carries no tenant, so the harness uses a session token.
- **Lean profile without a schema change.** Classification goes to the stub model (empty subgraph), and summaries are
  switched off with the existing per-subject `summarizationMinCellLength` override (100000), which also serves
  `--index-summaries false`. No `IndexSummaries` column was added.
- **Model endpoints.** The harness creates and reuses only its own `bench-*` endpoints (10-minute timeout, queue of
  256). Pneuma's seeded endpoints (60-second timeout, no queue) reject concurrent ingestion calls.
- **Embedding throughput.** The shipped configuration runs the embedding stage one job at a time (`embedding: 1`).
  The bench server raised it to 4 at runtime (`PUT /v1.0/settings/ingestion`), which changes throughput, not results.
  SciFact and NFCorpus use `all-minilm`, which is Isis-comparable and much faster on a laptop GPU. The other sets use
  Pneuma's seeded `nomic-embed-text`.
- **Query encoding.** The harness form-encodes query strings (spaces as `+`), as the dashboards' `URLSearchParams`
  does, so round 0 measures what real clients got before the decoding fix.
- **Meridian has no PDFs.** It covers md, html, and txt.
- **Not yet built:** graph-quality scoring (Pneuma exposes no REST route to enumerate a subject's graph; it would need
  one, or direct LiteGraph reads), embedding-based MMR (P9), a rerank score on hits, and the two-person label
  spot-check.
- **Shared hardware.** During these runs another benchmark workload (the Isis suite) shared the same Ollama and GPU.
  Accuracy numbers are unaffected. Latency and throughput were measured under contention and are recorded as such in
  RESULTS.md.
