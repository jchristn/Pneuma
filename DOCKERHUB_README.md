# Pneuma — breathing life into your information

**Pneuma** is a self-hostable **knowledge-graph data platform**. Point it at a body of source material — documents, web pages, and other artifacts about any kind of subject — and it ingests each one, extracts its semantic content, classifies it into an editable **ontology**, merges it into a **knowledge graph**, and indexes it for grounded, cited retrieval. On top of that graph it serves hybrid (lexical + semantic) search and question-answering that always cites its sources, with full provenance, rights, and multi-tenant access control — over a REST API and an in-process **Model Context Protocol (MCP)** server.

This image runs the **Pneuma backend server**: a C# service on Watson 7.1 that orchestrates ingestion, hosts the REST API and the in-process MCP endpoint, enforces multi-tenant RBAC with a full audit trail, and emits Prometheus metrics and OpenTelemetry/OTLP traces. It is designed to run as part of the Pneuma Docker Compose stack alongside LiteGraph, DocumentAtom, Partio, RecallDB, PostgreSQL (pgvector), Less3, Ollama, Prometheus, Grafana, and Tempo.

![Pneuma](https://raw.githubusercontent.com/jchristn/pneuma/main/assets/logo.png)

## Who it's for

- **Data engineers & data users** — turn unstructured documents and pages into a structured, queryable knowledge graph with typed entities, relationships, provenance, and rights.
- **AI engineers** — a production-grade RAG backend: hybrid retrieval, graph-neighbor expansion, prompt rewrite, optional LLM re-ranking, cited answers with an explicit "insufficient support" refusal, an LLM-judged evaluation harness, and a first-class MCP surface. Bring your own model (OpenAI, Gemini, or fully offline Ollama).
- **Software developers** — an OpenAPI-described REST API, C#/JS/Python SDKs, multi-tenant RBAC + audit, request-history capture, metrics/traces, and a provider-neutral data layer (PostgreSQL, SQLite, MySQL, SQL Server).

## Architecture

```
Subject link → ingestion job → worker pool:
  DocumentAtom (type detect + cell extract) → PolyPrompt (classify to your ontology)
  → LiteGraph (merge subgraph + cell nodes) → Partio (chunk/embed/summarize) → RecallDB (chunks + vectors)

Ask a question → hybrid retrieval over RecallDB (+ optional graph-neighbor expansion over LiteGraph)
  → grounded, cited answer (REST /v1.0/query or the MCP pneuma_query tool), or an "insufficient support" refusal
```

## Getting started

Use the Compose stack from the repository rather than running this image alone — the server depends on PostgreSQL, LiteGraph, DocumentAtom, Partio, and RecallDB.

```bash
git clone https://github.com/jchristn/pneuma
cd pneuma/docker
docker compose up -d
```

Then open the Pneuma API at `http://localhost:8080` (OpenAPI at `/openapi.json`, MCP at `/mcp`) and the admin, subject, and user dashboards at ports `3010`, `3011`, and `3012`.

Configuration is a mounted `pneuma.json` (web server, CORS, logging, database, auth, request history, ingestion concurrency, integration endpoints, telemetry), with secrets overridable via `PNEUMA_*` environment variables. A default administrator and built-in RBAC roles are seeded on first boot. **Change every default credential before exposing any service.**

## Related images

Pneuma's other components publish alongside this one: `jchristn77/pneuma-postgres` (PostgreSQL + pgvector), `jchristn77/pneuma-admin-ui`, `jchristn77/pneuma-subject-ui`, and `jchristn77/pneuma-user-ui`.

## Tags

- `latest` — most recent build.
- Version tags (e.g. `v0.1.0`) track releases documented in `CHANGELOG.md`. Pin an exact version for production; Pneuma is in its `0.x` alpha series and anything may change between releases.

## License

MIT. Copyright (c) 2026 Joel Christner.
