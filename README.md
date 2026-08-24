<!-- Logo -->
<p align="center"><img src="assets/logo.png" alt="Pneuma" width="140" /></p>

<h1 align="center">Pneuma — breathing life into your information</h1>

<p align="center"><strong>v0.1.0 · Alpha</strong></p>

> **This is alpha software.** Everything in Pneuma — APIs, database schemas, configuration, dashboards, and defaults — is subject to change without notice while the project is in its `0.x` series. Pneuma will adopt [semantic versioning](https://semver.org/) at its stable **1.0** release; until then, treat every build as a moving target and pin the exact version you deploy.

Pneuma is a self-hostable platform that turns a body of source material into a searchable, rights-aware **knowledge graph** — and exposes it to your applications and AI agents through a REST API, an in-process MCP server, and operator dashboards.

---

## What it is

Point Pneuma at a set of sources about a subject — documents, web pages, and other artifacts — and it ingests each one, extracts its semantic content, classifies it into an **ontology**, merges it into a **knowledge graph**, and indexes it for grounded, cited retrieval. On top of that graph it provides:

- a **REST API** (OpenAPI-described) for ingestion, search, grounded Q&A, and administration;
- an in-process **Model Context Protocol (MCP)** server so AI agents can drive the platform through the *same* authentication, authorization, and audit path as the REST API;
- three **React dashboards** (admin, subject, user) for operating the platform and exploring the graph.

Pneuma is the **intelligence layer** — ingestion, ontology, graph, retrieval, provenance, rights awareness, and RBAC. It is infrastructure you build on, not a finished end-user product.

## Use cases

Pneuma is aimed at **developers, data engineers, and AI engineers** who need structured, grounded knowledge out of unstructured sources:

- **Corpus → knowledge graph.** Turn a collection of documents and pages into a queryable graph of entities and relationships without hand-rolling an ingestion pipeline.
- **Ground an LLM or agent (RAG).** Serve cited, provenance-tracked answers from a curated corpus over REST (`/v1.0/query`, streaming) or via the MCP tools — instead of letting a model guess.
- **Give agents safe tools over your data.** Expose search, graph traversal, and grounded Q&A to an agent through MCP, with the same bearer/API-key auth, RBAC, and audit trail as every other call.
- **Structured extraction.** Convert documents into typed entities and relationships you control, via an ontology you can edit in plain language.
- **Domain search & exploration.** Build a search or graph-exploration experience over a specific subject's body of work, with results that link back to their sources.

## Benefits

- **Grounded and cited.** Answers are synthesized from retrieved graph nodes and carry their supporting sources, with an explicit "insufficient support" refusal when the corpus can't answer.
- **Provenance and reproducibility.** Every ingestion run records the version and content-hash of the prompts that shaped it, so a past run stays reproducible even after prompts change.
- **Bring your own model.** LLM access goes through a provider-neutral layer (OpenAI, Gemini, or local **Ollama**), so you can run **fully offline** or plug in a hosted model with a key.
- **Editable, natural-language ontology.** The ontology that drives classification is a prompt you can rewrite — reshape the graph's node and edge types without touching code.
- **Agent-ready.** A first-class MCP endpoint means agents get bounded, paginated, authorized tools out of the box.
- **Multi-tenant RBAC, fully audited.** Tenants, users, credentials, roles, permissions, and assignments — with every authorization decision (including admin bypasses) recorded.
- **Provider-neutral storage.** A handwritten, provider-neutral data layer runs on **PostgreSQL**, SQLite, MySQL, or SQL Server.
- **Self-hostable in one command.** The whole stack — platform, graph, search, embeddings, models, and observability — comes up with `docker compose up`.

## How it works

```
Subject submits a link  →  queued ingestion job  →  worker pool
  1. DocumentAtom   type detection            (unknown type → fail)
  2. DocumentAtom   semantic cell extraction
  3. PolyPrompt     classify cells → candidate subgraph (ontology)
  4. LiteGraph      merge subgraph → record node/edge IDs
  5. Partio         summarize + chunk + embed
  6. RecallDB       store chunk text + vectors, each linked back to its graph node

A user searches (RecallDB)  →  representative graph nodes  →  node explorer
  (contents, links, adjacent nodes, relationships, provenance, rights)

An app or agent asks a question  →  lexical + semantic retrieval over the graph
  →  grounded, cited answer  (REST /v1.0/query or the MCP pneuma_query tool)
```

Ingestion runs as an explicit **Categorize** phase (fetch → atomize → classify into a candidate plan) followed by a **Hydrate** phase (commit to the graph, embeddings, and index), with per-phase logs you can follow live. Grounded answering blends lexical (inverted-index) and semantic (vector) retrieval with optional graph-neighbor expansion, from one shared service used identically by REST and MCP.

Links can be submitted with **labels** and **tags** that are stamped onto every chunk and graph node they produce; search, grounded Q&A, and chat can then be **scoped** to those facets (a per-request `metadataFilter`, merged with a subject's default filter) so a query narrows to exactly the content you mean.

Each **subject** carries its own chat behavior: a unique URL slug (the user dashboard opens `/{slug}` as that subject's chat), a system prompt and ontology prompts that are appended to the global ones, a toggle for whether model *thinking* is shown, and a history-retention window. Every chat turn is persisted with its full timing/metadata, grouped into named **conversation threads** that can be reopened, renamed, and managed from a Conversations view on every dashboard. Users can rate answers (👍/👎 + comment), and operators review turns and ratings in the **History** and **Feedback** views, watch per-subject **Analytics** (turn volume, latency percentiles, per-stage timing), and run an LLM-judged **evaluation harness** of ground-truth facts against the live grounded pipeline. Deleting a subject returns immediately and runs its large cascade (links, jobs, artifacts, graph, index, history, feedback) in a background worker.

## How to get started

> **Docker only.** The supported way to run Pneuma is the Docker Compose stack. You do **not** need the .NET SDK, Node, or any language toolchain to run it — only Docker and Docker Compose.

```bash
git clone https://github.com/jchristn/Pneuma.git
cd Pneuma/docker
docker compose up -d
```

The stack comes up with a seeded administrator, built-in RBAC roles, a default API key, default model runners (Ollama active for offline use; OpenAI/Gemini activate when you supply keys), and a small starter graph so the user dashboard has something to explore immediately.

Then open the **Admin dashboard** and sign in:

| Dashboard | URL | Default login |
|-----------|-----|---------------|
| Pneuma Admin | http://localhost:3010 | `admin@pneuma` / `password` |
| Pneuma Subject | http://localhost:3011 | `admin@pneuma` / `password` |
| Pneuma User | http://localhost:3012 | `admin@pneuma` / `password` |

The Pneuma API is at **http://localhost:8080** (OpenAPI at `/openapi.json`; admin API key `pneumaadmin`). The full list of service ports and default credentials — including LiteGraph, DocumentAtom, Partio, RecallDB, Grafana, Prometheus, Tempo, and Postgres — is in [`docker/PORTS.md`](docker/PORTS.md).

> **Change every default credential before exposing any service beyond your machine.** Override the seeded admin with `PNEUMA_ADMIN_EMAIL` / `PNEUMA_ADMIN_PASSWORD` (see `docker/pneuma.json`).

To wipe the stack back to factory defaults, use `docker/reset.bat` (Windows).

## Architecture

| Layer | Technology |
|-------|-----------|
| Backend | C# on **Watson 7.1** |
| Control-plane DB | **PostgreSQL** (SQLite / MySQL / SQL Server also supported via a provider-neutral data layer) |
| Knowledge graph | **LiteGraph** |
| Type detection / cell extraction | **DocumentAtom** |
| Chunking / embedding / summarization | **Partio** |
| Retrieval store (vector + full-text) | **RecallDB** |
| LLM access | **PolyPrompt** (OpenAI, Gemini, Ollama / local) |
| BLOB / object storage | **Blobject** / S3 (Less3) |
| Logging | **SyslogLogging** |
| Observability | **Prometheus**, **Grafana**, **Tempo** |
| Dashboards | Three React / Vite apps: **admin**, **subject**, **user** |

Repository layout:

```
src/               Pneuma.Core library, Pneuma.Server (Watson host), Test.* suites
admin-dashboard/   subject-dashboard/  user-dashboard/    three React/Vite apps
sdk/               csharp/  js/  python/   client SDKs
docker/            compose.yaml, factory/ (resettable defaults), PORTS.md, reset.bat
assets/            logo, favicon, Grafana dashboards
REST_API.md        full REST API reference
MCP_API.md         MCP tool reference
```

Additional references: [`REST_API.md`](REST_API.md) (complete REST surface), [`MCP_API.md`](MCP_API.md) (MCP tools), and [`CHANGELOG.md`](CHANGELOG.md) (release history).

## Filing issues, enhancement requests, and pull requests

Pneuma is developed on GitHub at **https://github.com/jchristn/Pneuma**.

- **Bugs** — open an [issue](https://github.com/jchristn/Pneuma/issues) with what you did, what you expected, what happened, and enough detail to reproduce (relevant logs, the service and version, and your configuration with secrets redacted).
- **Enhancement requests** — open an issue describing the use case and the outcome you want; because Pneuma is in alpha, this is the best time to influence the direction of an API or feature before it stabilizes.
- **Pull requests** — contributions are welcome. Please open an issue first to discuss anything non-trivial, keep changes focused, and match the existing code style (the backend conventions are documented in `CLAUDE.md`). Build the backend with `dotnet build src/Pneuma.sln`; each dashboard builds with `npm ci && npm run build`.

Given the alpha status, interfaces may change between `0.x` releases — please note the version you're running when you report an issue.

## License

Pneuma is released under the **MIT License**. See [`LICENSE`](LICENSE).
