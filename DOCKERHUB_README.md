# Pneuma — Pneuma - information brought to life

Pneuma turns a subject's approved body of work into structured, connected, rights-aware cultural intelligence. Subjects submit their material; Pneuma ingests each artifact, classifies it into an ontology, merges it into a knowledge graph, and indexes it for grounded search — powering trustworthy fan-facing experiences that stay anchored to authoritative source material.

This image runs the **Pneuma backend server**: a C# service on Watson 7.1 that orchestrates ingestion, hosts the REST API, enforces multi-tenant RBAC, and emits Prometheus metrics and Radiant/OTLP traces. It is designed to run as part of the Pneuma Docker Compose stack alongside LiteGraph, DocumentAtom, Partio, Verbex, Postgres, Prometheus, Grafana, and Tempo.

![Pneuma](https://raw.githubusercontent.com/jchristn/pneuma/main/assets/logo.png)

## Use cases

- **Subject living archives.** Give any kind of subject a curated, searchable knowledge graph of their work that they control — records, tracks, lyrics, events, collaborators, themes, and the relationships among them.
- **Grounded fan experiences.** Back an "Ask" experience with retrieval that cites its sources, so answers come from the subject's corpus rather than open-web guesswork.
- **Rights-aware publishing.** Attach rights and provenance metadata to every node and edge so retrieval and display can honor subject-owned, licensed, public-domain, and restricted material differently.

## Architecture

```
Subject link → ingestion job → worker pool:
  DocumentAtom (type detect + cell extract) → PolyPrompt (classify to ontology)
  → LiteGraph (merge subgraph) → Partio (chunk/embed/summarize) → Verbex (index)
User search (Verbex) → representative graph nodes → node explorer with provenance
```

## Getting started

Use the Compose stack from the repository rather than running this image alone — the server depends on Postgres, LiteGraph, DocumentAtom, Partio, and Verbex.

```bash
git clone https://github.com/jchristn/pneuma
cd pneuma/docker
docker compose up -d
```

Then open the Pneuma API at `http://localhost:8080` (OpenAPI at `/openapi.json`), and the admin, subject, and user dashboards at ports `3010`, `3011`, and `3012`.

Configuration is a mounted `pneuma.json` (webserver, CORS, logging, database, auth, request history, ingestion concurrency, integration endpoints, telemetry), with secrets overridable via `PNEUMA_*` environment variables. A default administrator and built-in RBAC roles are seeded on first boot.

## Tags

- `latest` — most recent build.
- Version tags track releases documented in `CHANGELOG.md`.

## License

MIT.
