# Pneuma Telemetry Guide

Pneuma emits two complementary streams of operational telemetry — **metrics** (aggregate counters and
latency histograms) and **traces** (distributed spans of individual requests) — across the whole product:
HTTP, the ingestion pipeline, the retrieval/answer path, and every outbound integration. This guide explains
what is emitted, how it is exposed and collected, how to reach Grafana, and how to read the data to answer
real operational questions.

---

## 1. At a glance

| | Metrics | Traces |
|---|---|---|
| **Emitted by** | `PneumaMetrics` (in-process, Prometheus text format) | `TelemetryService` (OpenTelemetry via Radiant) |
| **Exposed at** | `GET /metrics` on the Pneuma server | OTLP gRPC push |
| **Collected by** | Prometheus (`:9090`) scraping `/metrics` | Tempo (`:3200`, OTLP ingest `:4317`) |
| **Visualized in** | Grafana (`:3000`) dashboard panels | Grafana → Explore → Tempo |

Everything is provisioned in the docker stack under `docker/` and `docker/factory/`; a plain
`docker compose up -d --build` brings up Pneuma, Prometheus, Tempo, and Grafana wired together.

---

## 2. What Pneuma measures (metrics inventory, by domain)

All metric names are prefixed `pneuma_`. Labels are deliberately **low-cardinality** (no ids), so series
stay bounded.

### HTTP
- `pneuma_http_requests_total{method,route,status}` — request counter.
- `pneuma_http_request_duration_seconds{...}` — request-latency histogram (p50/p95/p99 derived in Grafana).

### Ingestion
- `pneuma_ingestion_jobs_total{outcome}` — jobs by outcome (started/completed/failed/cancelled).
- `pneuma_ingestion_completed_total`, `pneuma_ingestion_failed_total` — lifetime counters.
- `pneuma_ingestion_stage_total{stage,outcome}` — per-stage event counter across **every** pipeline stage
  (ContentRetrieval, TypeDetection, CellExtraction, Classification, GraphMerge, Summarization, Chunking,
  Embedding, Indexing), including `queued` when a stage waited for a concurrency slot.
- `pneuma_ingestion_stage_duration_seconds{stage}` — per-stage duration histogram (where stalls show up).

### Retrieval & Answer
- `pneuma_chat_answers_total{outcome}` — answered chat/query turns by outcome.
- `pneuma_chat_answer_duration_seconds{outcome}` — total answer-latency histogram.
- `pneuma_chat_stage_duration_seconds{stage}` — per-stage answer-pipeline latency histogram, one series per
  coarse stage: `prompt_rewrite`, `compaction`, `tool`, `final_inference` (the same stages persisted per turn
  in `performanceJson` and surfaced in the dashboard History detail and Analytics views).

### Integrations
- `pneuma_integration_requests_total{service,operation,outcome}` — outbound calls to RecallDB, Partio,
  LiteGraph, DocumentAtom, PolyPrompt, by outcome (ok/error).
- `pneuma_integration_request_duration_seconds{service,operation}` — per-integration latency histogram.

### Authorization / uptime
- `pneuma_authz_decisions_total{result}` — permit/deny decisions.
- `pneuma_uptime_seconds` — process uptime gauge.

## 3. What Pneuma traces (spans)

Traces are emitted over OTLP and stored in Tempo. Spans exist for:

- **Ingestion** — a root span per job (`ingestion <sourceUrl>`, tagged `pneuma.job.id`, `pneuma.tenant.id`,
  `pneuma.subject.id`) with a child span per pipeline stage (`stage:<Name>`), so a slow job's stage breakdown
  is visible in one trace.
- **Requests & integrations** — request capture and downstream integration calls carry spans, tagged with the
  service and operation, so a slow answer resolves to the specific RecallDB/Partio/LiteGraph call that caused
  it.

Per-turn answer telemetry is additionally persisted structurally on each chat turn (`performanceJson`, the
`chatturnperfevents` table) and rendered in the dashboard History detail modal and the Analytics view — a
DB-native complement to the Prometheus metrics and Tempo spans.

---

## 4. How it is exposed and collected

- **Metrics exposure.** The Pneuma server serves the current metrics in Prometheus text-exposition format at
  `GET /metrics`. Health is at `GET /v1.0/api/health`.
- **Metrics collection.** Prometheus (`docker/prometheus.yaml`) scrapes the Pneuma `/metrics` endpoint on an
  interval and stores the series in its TSDB (`:9090`).
- **Trace exposure & collection.** The server pushes spans over OTLP to Tempo (`:4317` ingest); Tempo serves
  trace queries on `:3200`.
- **Datasources & dashboards.** Grafana is provisioned (`docker/grafana/provisioning/…` and the `docker/factory`
  equivalent) with **Prometheus** and **Tempo** datasources and a set of **per-domain dashboards** mounted
  from `assets/grafana/*.json` — one dashboard each for Overview, HTTP, Ingestion, Chat & Retrieval, and
  Integrations (all in the Grafana **Pneuma** folder).

---

## 5. Accessing Grafana

1. Bring up the stack: `docker compose up -d --build` (from `docker/`, or `docker/factory/` for the seeded
   demo).
2. Open **http://localhost:3000**.
3. Log in with the default credentials **`admin` / `admin`** (set via `GF_SECURITY_ADMIN_*`; change them for
   any non-local deployment).
4. Open **Dashboards → Browse → the Pneuma folder**. Five domain dashboards auto-load from provisioning:
   **Pneuma — Overview**, **— HTTP**, **— Ingestion**, **— Chat & Retrieval**, and **— Integrations**.

Related URLs: Prometheus **http://localhost:9090**, Tempo API **http://localhost:3200**.

---

## 6. Reading the dashboards (one per domain)

The observability dashboards are split by domain — open the one that matches your question:

- **Pneuma — Overview** — uptime, total request rate, error ratio, ingestion completed-vs-failed. Start here
  for a health snapshot.
- **Pneuma — HTTP** — request rate by route and status class, latency quantiles (p50/p95/p99), and the top
  routes by p95. Use it to find a slow or erroring endpoint.
- **Pneuma — Ingestion** — job rate by outcome, per-stage throughput, per-stage failure rate, and **per-stage
  p95 duration**. This is where a stuck or slow ingestion stage becomes obvious — a rising p95 on `Embedding`
  or `Classification` points straight at the bottleneck (usually model-runner contention).
- **Pneuma — Chat & Retrieval** — answer rate by outcome, answer p95 latency, and **per-stage p95 latency**
  (`prompt_rewrite` / `retrieval` / `rerank` / `tool` / `final_inference`). Use it to see whether answer
  slowness is generation, retrieval, or reranking.
- **Pneuma — Integrations** — request and error rate by service and the p95 latency per service+operation. When
  an answer or ingestion is slow, this tells you whether a downstream (RecallDB/Partio/LiteGraph/DocumentAtom)
  is the cause.

## 7. Reading traces

For a single slow request rather than an aggregate:

1. In Grafana, go to **Explore** and pick the **Tempo** datasource.
2. Search by service/span name or a trace id (chat/query responses and ingestion logs surface correlation
   ids). Ingestion jobs appear as `ingestion <url>` roots.
3. Open the trace to see the span waterfall — the ingestion stage or the specific integration call that
   dominated the time is the widest bar.

## 8. Typical workflows

- **"Ingestion feels slow."** Ingestion row → per-stage p95. The tallest stage is the bottleneck; if it's
  `Embedding`/`Classification`, check the Integrations row for Partio latency, then open the job's trace.
- **"Answers are slow."** Retrieval & Answer row → answer p95 + per-stage p95. If `final_inference` dominates
  it's the model; if `retrieval`/`rerank`, check RecallDB / the rerank model in Integrations.
- **"Something is erroring."** Overview error ratio → HTTP status-class panel to find the route → Integrations
  error rate to see if a downstream is failing → the trace for the exact call.

## 9. Notes & extension

- Metrics are per-process and in-memory (reset on restart); Prometheus retains the history. There is no remote
  write by default.
- Labels avoid ids to keep cardinality bounded; scope by `tenant`/`subject` only where the set is small.
- To add a metric, extend `PneumaMetrics` (a `Record*` method + a family in `Render()`); to add a panel, edit
  the relevant domain dashboard under `assets/grafana/pneuma-*.json` (mirror the change into
  `docker/factory/assets/grafana/`).
