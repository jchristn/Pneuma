<!-- Logo -->
<p align="center"><img src="assets/logo.png" alt="Pneuma" width="140" /></p>

# Pneuma - information brought to life

Pneuma is a platform where subjects curate their body of work into a searchable, rights-aware **knowledge graph** and build fan-facing experiences on top of it. A subject (for any kind of subject) submits links to their material; Pneuma ingests each artifact, classifies it into an ontology, merges it into a knowledge graph, and indexes it for grounded search — so fans, partners, and researchers can explore the work the way the subject intends.

Pneuma is the **intelligence layer**: ingestion, metadata, ontology, knowledge graph, retrieval, provenance, rights awareness, RBAC, and the APIs and dashboards that expose them. Audience experiences (AIM) consume Pneuma through its API.

## Architecture

```
Subject submits link → queued ingestion job → worker pool
  1. DocumentAtom   type detection            (unknown type → fail)
  2. DocumentAtom   semantic cell extraction
  3. PolyPrompt     classify cells → candidate subgraph (ontology)
  4. LiteGraph      merge subgraph → record node/edge IDs
  5. Partio         summarize + chunk + embed
  6. Verbex         index chunks, each linked back to its graph node

User searches (Verbex) → representative graph nodes → node explorer
  (contents, links [new tab], adjacent nodes, relationships, provenance, rights)
```

| Layer | Technology |
|-------|-----------|
| Backend | C# on **Watson 7.1**, instrumented with **Radiant** traces |
| Control-plane DB | **Postgres** (SQLite/MySQL/SQL Server also supported via a provider-neutral data layer) |
| Knowledge graph | **LiteGraph** (+ UI) |
| Type detection / cell extraction | **DocumentAtom** (+ UI) |
| Chunking / embedding / summarization | **Partio** (+ dashboard) |
| Inverted index / search | **Verbex** (+ dashboard) |
| LLM access | **PolyPrompt** (OpenAI, Gemini, Ollama/local) |
| BLOB storage | **Blobject** |
| Logging | **SyslogLogging** |
| Observability | **Prometheus**, **Grafana**, **Tempo** |
| Dashboards | Three React/Vite apps: **admin**, **subject**, **user** |

## Getting started

Prerequisites: Docker + Docker Compose, .NET 10 SDK (to build), Node 20+ (to build dashboards).

```bash
cd docker
docker compose up -d
```

The stack comes up with a seeded administrator, built-in RBAC roles, default model runners (Ollama active for offline use; OpenAI/Gemini activate when you supply keys), and a small starter graph so the user dashboard has something to explore immediately. The Admin dashboard's **Model Runners** page live-monitors each model endpoint's health (deduplicated by base URL) with a status pill, uptime history, and a health-details modal.

## Ports & credentials

All services are published on `localhost` by the Docker Compose stack. Change every default credential before exposing anything beyond your machine.

### Web dashboards (with default login)

| Dashboard | URL | Default credentials |
|-----------|-----|---------------------|
| **Pneuma Admin dashboard** | http://localhost:3010 | `admin@pneuma` / `password` |
| **Pneuma Subject dashboard** | http://localhost:3011 | `admin@pneuma` / `password` (or an subject user you create) |
| **Pneuma User dashboard** | http://localhost:3012 | `admin@pneuma` / `password` (or any tenant user) |
| **Grafana** | http://localhost:3000 | `admin` / `admin` |
| **LiteGraph UI** | http://localhost:3001 | access token `litegraphadmin` |
| **DocumentAtom UI** | http://localhost:3002 | none (no authentication) |
| **Partio dashboard** | http://localhost:8401 | bearer token `partioadmin` |
| **Verbex dashboard** | http://localhost:8601 | bearer token `verbexadmin` (tenant `default`) |

The three Pneuma dashboards all authenticate against the Pneuma API with the **seeded administrator `admin@pneuma` / `password`** (a user with `IsSystemAdmin`). Create additional subject/user logins from the Admin dashboard. Change the seed via `PNEUMA_ADMIN_EMAIL` / `PNEUMA_ADMIN_PASSWORD` (see `docker/pneuma.json` and `docker/factory/`).

### Backend services & APIs

| Service | URL | Auth |
|---------|-----|------|
| Pneuma API server | http://localhost:8080 (OpenAPI at `/openapi.json`) | Bearer token from `POST /v1.0/token`; admin API key `pneumaadmin` via `x-api-key` |
| LiteGraph server | http://localhost:8701 | bearer `litegraphadmin` |
| DocumentAtom server | http://localhost:8000 | none |
| Partio server | http://localhost:8400 | bearer `partioadmin` |
| Verbex server | http://localhost:8600 (container `8080`) | bearer `verbexadmin` |
| Ollama (local models) | http://localhost:11434 | none |
| Prometheus | http://localhost:9090 | none |
| Tempo | http://localhost:3200 (OTLP `4317`/`4318`) | none |
| Postgres (Pneuma control plane) | `localhost:15432` | `pneuma` / `pneuma` (database `pneuma`) |

A condensed version of this table lives in [`docker/PORTS.md`](docker/PORTS.md).

To wipe everything back to factory defaults:

```bash
docker/reset.bat        # Windows
```

## Repository layout

```
src/            Pneuma.Core library, Pneuma.Server (Watson host), Test.* (Touchstone)
admin-dashboard/ subject-dashboard/ user-dashboard/   three React/Vite apps
sdk/            csharp/ js/ python/ SDKs with test harnesses
docker/         compose.yaml, factory/ (resettable defaults), reset.bat
assets/         logo, favicon, Grafana dashboards
REST_API.md     full REST API reference
Pneuma.postman_collection.json
PNEUMA_PLAN.md     implementation plan (progress tracker)
```

## Documentation

- `REST_API.md` — complete REST surface (admin + user + ingestion).
- `PNEUMA_PLAN.md` — phased implementation plan with progress checkboxes.
- `CHANGELOG.md` — release history.

## License

MIT — see `LICENSE`.
