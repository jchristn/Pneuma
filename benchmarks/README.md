# Pneuma benchmarks

This directory holds a reproducible benchmark suite for Pneuma. It answers four questions: does Pneuma retrieve the
right documents and passages, do grounded answers come out right (and say "not in the sources" when they should),
what does the ingestion pipeline keep or lose on the way in, and what do search and ingestion cost in latency and
throughput. [RESULTS.md](RESULTS.md) has the current numbers; [../BENCHMARKING.md](../BENCHMARKING.md) is the plan
and the reasoning behind the design; [../RETRIEVAL_IMPROVEMENTS.md](../RETRIEVAL_IMPROVEMENTS.md) is the scored fix
backlog.

The harness (`src/Test.Benchmark`) is black-box. It talks to Pneuma only over REST (and MCP for the agent
benchmark), the same way a client does, and has no reference to Pneuma assemblies, so the same commands measure the
working tree or any deployment you point them at. It is modeled on the Isis suite in `C:\code\agentmemory\benchmarks`
and uses the same dataset format, so Isis datasets load unchanged.

| Command | Measures |
|---|---|
| `retrieval` | Document ranking per search mode (`text`, `vector`, `hybrid`) and for a plain reference RAG (`ref-bm25`, `ref-dense`, `ref-hybrid`): Hit@1, Recall@1/5/10, All@5/10, MRR@10, nDCG@10, Hits@4, MAP@10, passage-level Evidence@10, with 95% bootstrap intervals, per query type; latency; server-side stage breakdown; AUROC of top scores for answerable vs unanswerable questions |
| `answer` | Grounded answering through `/v1.0/query` or agentic chat (`--endpoint chat`): LLM-judged accuracy, correct declines, evidence-in-context split, citation precision/recall, optional per-claim faithfulness, repeat runs for variance |
| `ingest` | Ingest fidelity: failures by stage, stage timings, extraction coverage, chunk statistics, summary-chunk share, redundant chunks, U+FFFD corruption |
| `global` | Thematic questions: community-summary answers (`/v1.0/query/global`) vs local answers, pairwise-judged in both orders |
| `agent` | Claude Code headless with Pneuma's MCP server vs no knowledge source (success, turns, cost). Spends API credits |
| `load` | Closed-loop search throughput and latency percentiles per concurrency level, optionally against the stub embedding server |
| `compare` | Query-by-query diff of two retrieval reports with paired bootstrap intervals; non-zero exit on a regression |
| `prepare` | Converts BEIR (SciFact, NFCorpus) and MultiHop-RAG downloads into the dataset format |
| `stub` | Runs the stub model server on its own |

## 1. Stand up an isolated stack

Benchmarks run against their own Postgres, RecallDB, LiteGraph, DocumentAtom, and Less3 containers on non-default
ports, and a Pneuma server built from the working tree. They never touch `docker/` or a live deployment. Ollama runs on
the host.

```bash
docker compose -f benchmarks/docker/compose.yaml up -d   # Postgres :25432, RecallDB :28600, LiteGraph :28701, DocumentAtom :28000, Less3 :28100
benchmarks/start-bench-server.sh                         # Pneuma REST + /metrics :28080 (start-bench-server.bat on Windows)
ollama pull nomic-embed-text                             # default embedding model (768-dim; Pneuma's seeded default)
ollama pull gemma3:4b                                    # default inference model and judge (Pneuma's seeded default)
```

`docker compose -f benchmarks/docker/compose.yaml down -v` discards all benchmark data. The server's settings are
`docker/pneuma.bench.json`; it mirrors the shipped `docker/pneuma.json` retrieval and ingestion settings (neighbor
expansion on, eight ingest workers) except that pages are fetched with plain HTTP rather than the headless browser.

Pneuma ingests URLs, so the harness serves every benchmark document itself from an in-process corpus server on
`127.0.0.1:28090` (`/{dataset}/{corpus}/{docId}.{md|html|txt}`) while ingestion runs. The document id is recovered
from each link's URL when results are scored.

## 2. Datasets

Every dataset uses one neutral JSON format (`src/Test.Benchmark/Datasets`): corpora of documents and labelled
queries (`relevant` document ids, optional `grades`, `evidence` spans, `filter`, `answer`; an empty `relevant` list
marks an unanswerable question). Each corpus becomes its own Pneuma subject with its own collection.

| Dataset | In the repo? | Size | What it tests |
|---|---|---|---|
| `datasets/pneuma-live.json` | yes | 62 docs, 130 queries | Pneuma's own documentation, split at H2 sections: paraphrase, lexical (identifiers), multi, detail, filter, confusable, negative |
| `datasets/meridian.json` | yes | 110 docs (md/html/txt), 261 queries | A synthetic entity-rich institute: planted entity variants, multi-hop chains, superseded facts, confusable pairs, identifiers, themes; gold entity and relationship lists |
| `datasets/atlas.json` | yes (copied from Isis) | 170 docs, 260 queries | Head-to-head with Isis on identical data |
| SciFact (BEIR) | downloaded | 5,183 docs, 300 queries | Sanity anchor with published BM25 and embedding-model scores |
| NFCorpus (BEIR) | downloaded | 3,633 docs, 323 queries | Second BEIR anchor, graded relevance |
| MultiHop-RAG | downloaded | 609 articles, 300 of 2,556 queries (stratified) | Industry-standard multi-document RAG benchmark: inference, comparison, temporal, null queries |

The committed question sets were written with LLM assistance, by a pass that did not write the corpus, and every
relevance label and evidence span was checked against the text by script. Treat them as good but not gold.

Public data is downloaded at run time into the git-ignored `benchmarks/data/` and never redistributed:

```bash
B="dotnet run --project src/Test.Benchmark -c Release --"
cd benchmarks/data
curl -LO https://public.ukp.informatik.tu-darmstadt.de/thakur/BEIR/datasets/scifact.zip && unzip scifact.zip
curl -LO https://public.ukp.informatik.tu-darmstadt.de/thakur/BEIR/datasets/nfcorpus.zip && unzip nfcorpus.zip
mkdir -p multihoprag && curl -L -o multihoprag/corpus.json https://huggingface.co/datasets/yixuantt/MultiHopRAG/resolve/main/corpus.json \
  && curl -L -o multihoprag/MultiHopRAG.json https://huggingface.co/datasets/yixuantt/MultiHopRAG/resolve/main/MultiHopRAG.json
cd ../..
$B prepare --format beir --input benchmarks/data/scifact --name scifact --output benchmarks/data/scifact.json
$B prepare --format beir --input benchmarks/data/nfcorpus --name nfcorpus --output benchmarks/data/nfcorpus.json
$B prepare --format multihoprag --input benchmarks/data/multihoprag --name multihoprag --limit 300 --seed 7 --output benchmarks/data/multihoprag-300.json
```

## 3. Ingest profiles

Every Pneuma document goes through DocumentAtom extraction, an LLM ontology classification, and an LLM summary per
substantial cell before it is chunked and embedded. On a laptop GPU that is minutes per document, so there are two
profiles, recorded in every report:

- `--profile full` (default): the real inference model classifies and summarizes. Use for small and medium datasets
  and for anything that answers questions or uses the graph.
- `--profile lean`: ingestion runs against the in-process stub model (empty subgraph, no summaries), so only
  extraction, chunking, embedding, and indexing run. Retrieval-only, and fast enough for BEIR-size corpora. The
  subject's answering model is switched back to the real model after ingestion.

Subject names are deterministic (dataset, corpus, embedding model, profile, chunk settings, suffix), so a rerun reuses
a fully ingested subject. `--reingest` rebuilds it. The harness only reuses model endpoints it created itself
(`bench-*`, 10-minute timeout, request queue); Pneuma's seeded endpoints default to a 60-second timeout and no queue,
which is too tight for local classification.

## 4. Run

`run-baseline.sh` (or `.bat`) runs the standard suite. Individual commands:

```bash
B="dotnet run --project src/Test.Benchmark -c Release --no-build --"

# Retrieval (all three Pneuma modes plus the reference arm)
$B retrieval --dataset benchmarks/datasets/pneuma-live.json --label round1
$B retrieval --dataset benchmarks/data/scifact.json --profile lean
$B retrieval --dataset benchmarks/data/scifact.json --reference-only --no-dense     # validate BM25 without Pneuma

# Ablations on the same subjects: query-time settings apply to a reused subject
$B retrieval --dataset benchmarks/datasets/meridian.json --override-rrfK 20 --label rrf20           # per-request override (admin)
$B retrieval --dataset benchmarks/datasets/meridian.json --override-lexicalWeight 0.5 --label lex05
$B answer    --dataset benchmarks/datasets/pneuma-live.json --rerank llm --label rerank-llm
# Ingest-time settings get their own subject
$B retrieval --dataset benchmarks/datasets/pneuma-live.json --chunk-max-tokens 512 --chunk-overlap 64
$B retrieval --dataset benchmarks/datasets/pneuma-live.json --index-summaries false

# Answers, judged by a model called directly (never through Pneuma)
$B answer --dataset benchmarks/datasets/pneuma-live.json --judge-model gemma3:4b --faithfulness --repeat 3
$B answer --dataset benchmarks/datasets/pneuma-live.json --endpoint chat

# Ingest fidelity, thematic answers, agents, load
$B ingest --dataset benchmarks/datasets/meridian.json
$B global --dataset benchmarks/datasets/meridian.json
$B agent  --tasks benchmarks/agent/tasks-pneuma.json --model haiku          # spends API credits
$B load   --stub --dataset benchmarks/data/scifact.json --concurrency 1,4,16,64 --duration 30

# Regression gate
$B compare --baseline benchmarks/results/<old>.json --candidate benchmarks/results/<new>.json --tolerance 0.01 --latency-tolerance 0.25
```

Each run writes `benchmarks/results/<utc>-<kind>-<name>[-label].json` for machines and a `.md` for people. The
directory is git-ignored; RESULTS.md carries the numbers that matter. Every report records the git commit (`-dirty`
for uncommitted changes), the machine, the models, the ingest profile, and the configuration. An incomplete ingest is
flagged at the top of the report and the command exits 3.

Common options: `--url` (default `http://127.0.0.1:28080`), `--email` / `--password` (seeded admin), `--embedding-model`
/ `--embedding-url` / `--embedding-format`, `--inference-model`, `--judge-model` / `--judge-url` / `--judge-format`
(`ollama` or `openai`), `--results`, `--label`.

## 5. How to read the results

**Reference arm.** `ref-bm25` (Lucene-style analyzer, Pyserini defaults k1 0.9, b 0.4), `ref-dense` (the same
embedding model as Pneuma over 190-word windows, max-pooled per document), and `ref-hybrid` (RRF, k 60) run inside the
harness over the same served documents. The gap between Pneuma and this arm is what Pneuma's pipeline adds or costs.
BM25 reproduces the published scores: SciFact nDCG@10 0.676 (published 0.665 to 0.679 depending on configuration) and
NFCorpus 0.321 (published 0.322 to 0.325).

**Evidence@10.** The share of gold evidence spans found (normalized substring, or 80% token coverage) in the top 10
retrieved passages. It uses `granularity=chunk` on builds that support it; older builds return one best snippet per
document, which makes it a lower bound.

**Score separation.** AUROC of the top hit's score as a classifier of answerable vs unanswerable questions, computed
for each score the server returns: the reported score, the raw vector similarity, and the normalized fused score.

**Evidence in context (answer).** Accuracy is split by whether a relevant document or evidence span reached the
model, which tells retrieval failures from generation failures.

**Judges.** The default judge is the answering model, which is cheap but noisy. For headline numbers use a stronger
judge (`--judge-*`), `--repeat 3`, and spot-check about 50 verdicts by hand.

**Hardware.** Latency and throughput depend on the machine; compare runs from the same machine, and use `compare`
rather than absolute thresholds.
