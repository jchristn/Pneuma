# Ingestion improvements

This plan compares how Pneuma gets content in with two sibling products that share much of its stack, and lists
every improvement worth making to ingestion and data acquisition. Each item is scored for simplicity and value, and
each is planned across the whole product: server, backend, dashboards, SDKs, MCP, Postman, documentation, and tests.

- **AssistantHub** (`C:\Code\AssistantHub`) is a RAG assistant platform. It has file upload, reusable ingestion rules,
  and scheduled crawlers for web sites and CIFS/NFS file shares, built on CrawlSharp and Blobject.
- **Isis** (`C:\Code\AgentMemory`) is an agent-memory platform. It has no document ingestion or crawlers at all
  (its README says so), but its write path was hardened by a benchmark effort: model-aware chunk budgets, embedding
  task prefixes, a contextual chunk header, a shared retry handler, per-key write locks, and supersession.

No code was changed to produce this plan. Line references are to the working tree as of commit `3d64869`. Items
that overlap `RETRIEVAL_IMPROVEMENTS.md` carry its letter (for example "RI-E") and are planned in full here so this
document stands on its own for ingestion work; implement each once and tick both documents.

## How Pneuma ingests today

A user submits a URL to a subject (`POST /v1.0/subjects/{id}/links`, or `/links/bulk` for a list). The link and an
ingestion job are created in one transaction and the job waits in the `ingestionjobs` table. `IngestionWorkerService`
polls the table, claims the oldest queued job, and runs `IngestionProcessor` in two phases:

1. **Categorization.** ContentRetrieval (Playwright headless Chromium, falling back to plain HTTP; a SHA-256 of the
   fetched bytes lets an unchanged link finish early), TypeDetection (DocumentAtom `/typedetect`, with a `TextSniffer`
   fallback to plain text), CellExtraction (DocumentAtom `/atom/{type}`), and Classification (an LLM maps cells into
   the subject's ontology, in overlapping batches of 25 cells).
2. **Hydration.** OntologyCanonicalization, GraphMerge (a Source node, one Cell node per cell, and merged entity nodes
   in the tenant's LiteGraph graph), RelationshipConsolidation, Summarization (an LLM summary per cell of 128 or more
   characters), Chunking (TextChunker 0.3.1, counted in the embedding model's own tokens), Embedding (batches of 64,
   an LRU cache, retry on 429/5xx), and Indexing (one RecallDB document per chunk, tagged with its Cell node id).

Each stage runs through `StageRunner`, which checks for operator cancellation, takes a per-stage concurrency slot,
applies a stage deadline, and records an event, a Prometheus sample, and a span. A failed job is retried in place up to
`MaxAttempts` (3) times with exponential backoff, and every retry starts again from ContentRetrieval. Deletion cascades
through background workers to LiteGraph, RecallDB, the blob store, and the S3 artifact buckets.

What Pneuma does not have is as important as what it has. It accepts only URLs: there is no file upload, no way to
push text, no crawler, no sitemap or feed support, no scheduled refresh, and no connectors to file shares, buckets,
or SaaS systems. Links cannot carry credentials. MCP can read ingestion state but cannot submit anything.

## How the three products compare

| Capability | Pneuma | AssistantHub | Isis |
|---|---|---|---|
| Ways in | URL, bulk URL list | JSON/base64 upload, folder drag-and-drop, crawlers | Agent upsert (REST/MCP), batch of 100 |
| Crawlers and connectors | None | Web (CrawlSharp), CIFS, NFS | None |
| Scheduling and sync | None; manual re-ingest | Interval schedules; Added/Changed/Deleted delta; last run's failures retried | None |
| Queue | DB table, single-process claim, no crash recovery | None (`Task.Run`), dashboard throttles to 5 | None (synchronous) |
| Extraction | DocumentAtom cells kept as cells | DocumentAtom, flattened to one string | None |
| Enrichment | Ontology classification into a graph, cell summaries | Hierarchical summaries only | None on write |
| Chunking | TextChunker, model tokens, per-subject strategy/size/overlap | Partio (remote), 12 strategies, overlap strategies | TextChunker, model budget with margin, retrieval-sized, contextual header |
| Embedding robustness | Retry 429/5xx on embeddings only | Retry transient + unwrap proxied 500s; per-endpoint limiter | Retry handler with `Retry-After` and jitter; re-chunk on context-length error; task prefixes |
| Re-ingest | New job; old chunks and nodes stay searchable | Old vectors orphaned | Delete-then-create keyed chunks; version counter |
| Dedup | Link-level content hash (skip unchanged) | None | Advisory near-duplicate report; supersession |
| Failure reporting | Free-text error, per-stage events | Free-text status message, processing log, performance events | Error classifier to HTTP status |
| Credentials for sources | n/a | Plaintext in DB and API responses | n/a |
| MCP | Read-only ingestion tools | Full CRUD for documents, rules, crawl plans | Upsert, delete, enumerate |
| SDKs | C#, JS, Python (link submit, jobs) | C#, JS, Python (full, except reprocess) | None |

AssistantHub's crawler design is the model to follow for acquisition, and its bugs are the list of traps to avoid
(see [Peer defects to avoid](#peer-defects-to-avoid)). Isis's write path is the model to follow for chunking and
embedding. Pneuma's own pipeline (cell-level provenance, the graph, per-stage gates, a real delete cascade) is already
stronger than either peer's and should be kept.

## Scoring

Every item is scored on two axes from 1 to 10.

- **Simplicity** is how easy the item is to integrate. 10 is a small, local change with no new dependency; 1 is a
  cross-cutting effort with new external dependencies, new entities, and a large UI.
- **Value** is the benefit to users and operators: content that can get in, correctness of what is stored, retrieval
  quality, safety, and operability. 10 is essential; 1 is marginal.
- **Total** is Simplicity + Value (at most 20).

The table is ordered by Value, descending. Ties are broken by Total, then by Simplicity. IDs are stable: **P** items
are pipeline changes, **T** items are new file types, **A** items are data-acquisition sources (one per connector
type, so each can be planned and scheduled separately), and **S** items are product surfaces.

## Ranked items

| Rank | ID | Item | Area | Simplicity | Value | Total | Origin | Also |
|---:|---|---|---|:---:|:---:|:---:|---|---|
| 1 | A1 | File upload (multipart, multi-file, folder drag-and-drop) | Acquisition | 6 | 9 | **15** | AssistantHub | |
| 2 | P1 | Replace the previous version on re-ingest; idempotent retries | Pipeline | 5 | 9 | **14** | Pneuma gap, Isis | RI-E |
| 3 | A4 | Web crawler (CrawlSharp) | Acquisition | 5 | 9 | **14** | AssistantHub | |
| 4 | A0 | Source and sync framework (prerequisite for A3–A19) | Acquisition | 3 | 9 | **12** | AssistantHub | |
| 5 | A2 | Inline content push API (text, Markdown, HTML, JSON) | Acquisition | 8 | 8 | **16** | Isis, industry | |
| 6 | P3 | No silent partial loss: completeness accounting | Pipeline | 7 | 8 | **15** | Pneuma gap | |
| 7 | P5 | Fetch safety: SSRF guard, size cap, TLS, politeness | Pipeline | 7 | 8 | **15** | Pneuma gap | |
| 8 | P2 | Durable queue: atomic claim, crash recovery, one job per link | Pipeline | 6 | 8 | **14** | Pneuma gap, AssistantHub | RI-S (part) |
| 9 | P4 | Shared transient-retry policy and per-endpoint limiter for all model calls | Pipeline | 6 | 8 | **14** | Isis, AssistantHub | |
| 10 | P6 | Embedding model profiles: task prefixes and retrieval-sized chunks | Pipeline | 6 | 8 | **14** | Isis | RI-Y |
| 11 | S1 | MCP ingestion write tools and agent instructions | Surface | 8 | 7 | **15** | AssistantHub, Isis | |
| 12 | P8 | Contextual chunk header on the embedding text | Pipeline | 7 | 7 | **14** | Isis | RI-O |
| 13 | P9 | Per-subject enrichment switches (classification, summaries, summary indexing) | Pipeline | 7 | 7 | **14** | Benchmarks | |
| 14 | P10 | Structured failure categories, codes, and attempt history | Pipeline | 7 | 7 | **14** | Pneuma gap | |
| 15 | A3 | Scheduled refresh of existing links | Acquisition | 7 | 7 | **14** | AssistantHub | |
| 16 | A5 | Sitemap source | Acquisition | 7 | 7 | **14** | AssistantHub, industry | |
| 17 | P7 | Model-aware token budget and re-chunk on context-length errors | Pipeline | 6 | 7 | **13** | Isis | RI-F |
| 18 | A7 | Amazon S3 and S3-compatible bucket source | Acquisition | 6 | 7 | **13** | Industry | |
| 19 | A13 | SharePoint Online and OneDrive source (Microsoft Graph) | Acquisition | 3 | 7 | **10** | Industry | |
| 20 | P12 | Type-detection hints, hard-fail on unsupported types, keep Markdown | Pipeline | 8 | 6 | **14** | Pneuma gap | |
| 21 | P16 | Ingestion observability: queue, retries, volume, processing log | Pipeline | 8 | 6 | **14** | AssistantHub | |
| 22 | S2 | SDK parity for ingestion and sources | Surface | 8 | 6 | **14** | AssistantHub | |
| 23 | P14 | Conditional GET and a stable content hash | Pipeline | 7 | 6 | **13** | AssistantHub, Pneuma gap | |
| 24 | A9 | CIFS/SMB file-share source | Acquisition | 6 | 6 | **12** | AssistantHub | |
| 25 | A20 | Authenticated links (headers, cookies, basic, bearer) | Acquisition | 6 | 6 | **12** | AssistantHub | |
| 26 | P13 | Structure-aware chunking: merge small cells, keep headings, group table rows | Pipeline | 5 | 6 | **11** | AssistantHub, Pneuma gap | |
| 27 | P15 | Serialize entity merges on shared keys | Pipeline | 5 | 6 | **11** | Isis | RI-S |
| 28 | A12 | Git repository source (GitHub, GitLab, any Git remote) | Acquisition | 5 | 6 | **11** | Industry | |
| 29 | P11 | Resume a failed job from the failed stage | Pipeline | 4 | 6 | **10** | Pneuma gap | |
| 30 | A15 | Confluence source | Acquisition | 4 | 6 | **10** | Industry | |
| 31 | A14 | Google Drive source | Acquisition | 3 | 6 | **9** | Industry | |
| 32 | P18 | Unicode sanitizing before storage | Pipeline | 9 | 5 | **14** | Isis | RI-C |
| 33 | A6 | RSS and Atom feed source | Acquisition | 7 | 5 | **12** | Industry | |
| 34 | P17 | Cross-link duplicate detection | Pipeline | 6 | 5 | **11** | Isis | RI-U |
| 35 | P21 | Event notifications (webhooks) for jobs and sync runs | Pipeline | 6 | 5 | **11** | AssistantHub gap, industry | |
| 36 | T1 | Archive expansion (ZIP, TAR, GZip) | File type | 6 | 5 | **11** | Industry | |
| 37 | A8 | Azure Blob Storage source | Acquisition | 6 | 5 | **11** | Industry | |
| 38 | S5 | Ingestion benchmark gate in CI | Surface | 6 | 5 | **11** | Isis | |
| 39 | P19 | Fair scheduling and priority across tenants and subjects | Pipeline | 5 | 5 | **10** | Pneuma gap | |
| 40 | P20 | Named ingestion profiles | Pipeline | 5 | 5 | **10** | AssistantHub | |
| 41 | T2 | Email files (EML, MSG, MBOX) | File type | 5 | 5 | **10** | Industry | |
| 42 | A16 | Notion source | Acquisition | 4 | 5 | **9** | Industry | |
| 43 | T3 | Audio and video transcription | File type | 3 | 5 | **8** | Industry | |
| 44 | S4 | Documentation accuracy fixes | Surface | 10 | 4 | **14** | Pneuma gap | |
| 45 | S3 | Postman parity | Surface | 9 | 4 | **13** | AssistantHub | |
| 46 | A10 | NFS file-share source | Acquisition | 6 | 4 | **10** | AssistantHub | |
| 47 | T5 | Source-code awareness | File type | 5 | 4 | **9** | Industry | |
| 48 | A19 | SQL database query source | Acquisition | 5 | 4 | **9** | Industry | |
| 49 | P22 | Subject export and import bundle | Pipeline | 4 | 4 | **8** | Isis (OKF) | |
| 50 | A18 | IMAP mailbox source | Acquisition | 4 | 4 | **8** | Industry | |
| 51 | A17 | Slack source | Acquisition | 3 | 4 | **7** | Industry | |
| 52 | A11 | Server-local directory source | Acquisition | 8 | 3 | **11** | Industry | |
| 53 | T4 | EPUB and OpenDocument formats | File type | 5 | 3 | **8** | Industry | |

## Conventions every item follows

To keep the item sections short, these rules apply to every item and are not repeated.

- **Code style and layout.** Everything follows `CLAUDE.md`: no `var`, no tuples, one type per file, XML docs,
  `_PascalCase` privates, `ConfigureAwait(false)`, classic `using` blocks, SyslogLogging instead of the console.
- **Entities.** New entities get a `PrettyId` prefix in `Constants.cs` and a generator in `Helpers/IdGenerator.cs`,
  carry `TenantId`, and are stored in structured columns (child tables for lists) in all four providers (`Sqlite`,
  `Mysql`, `Postgresql`, `SqlServer`), through a versioned, idempotent migration recorded in `schema_migrations`.
  Queries always scope by tenant; unique indexes lead with `tenant_id`. `src_` is already taken by graph Source nodes,
  so data sources use `dsrc_` and sync runs use `sync_`.
- **Authorization.** New resources get a `ResourceTypeEnum` value and the standard permission checks, with denials
  and bypasses audited as `AUTHENTICATION.md` describes. Secrets are write-only: stored encrypted with the existing
  `Aes256Cipher`, never returned by REST or MCP (a `hasSecret` flag instead), and redacted in request history.
- **Routes.** New routes are registered by a feature route registrar, carry OpenAPI metadata so the API Explorer
  picks them up, return JSON errors through `RouteHelper.ExceptionAsync`, and read query strings with
  `RouteHelper.Query`. Every enumeration follows the bounded paging protocol in `MCP_API.md`.
- **Dashboards.** New screens live in the admin dashboard, and in the subject dashboard when a subject owner needs
  them. They use the hand-rolled `ApiClient`, i18next strings, custom confirm modals, and settings tips; work in
  light and dark themes; and are checked at 1280, 768, and 390 px.
- **SDKs.** Every new REST operation is added to all three SDKs (`sdk/csharp`, `sdk/js`, `sdk/python`) with the same
  method names (camelCase in C# and JavaScript, snake_case in Python) and a README example.
- **MCP.** Tools are added to `McpToolCatalog` with behavioural descriptions (when to call it, what it returns, what
  it costs), obey the paging protocol, and are listed in `MCP_API.md`. Write tools require the same permission as
  the REST route.
- **Postman.** Every new route gets a request in `Pneuma.postman_collection.json`, in a folder named for the feature,
  with a saved example response and at least one negative request (bad input or missing permission).
- **Docs.** Every item updates `REST_API.md`, `MCP_API.md` when tools change, `TELEMETRY.md` when metrics change,
  `README.md` when a user-visible capability appears, and the `[Unreleased]` section of `CHANGELOG.md`.
- **Tests.** Tests are Touchstone descriptors in `Test.Shared` (no console output), run by `Test.Automated`,
  `Test.Xunit`, and `Test.Nunit`. Each item lists positive tests (the feature works) and negative tests (bad input,
  missing permission, failure paths) and names the suite. Test servers and stubs bind to `127.0.0.1`. Connectors are
  tested against loopback stub servers, never real SaaS accounts, with an optional live suite gated by environment
  variables.
- **Benchmarks.** Items that change what is stored or how it is embedded are measured with the harness in
  `benchmarks/` (see `BENCHMARKING.md`) before and after, and the result is recorded in `benchmarks/RESULTS.md`.

## Item details

Items appear in rank order. A4 (web crawler) ranks above A0 (the framework it is built on) because of its value, but
A0 must be built first; see [Sequencing](#sequencing).

### 1. A1: File upload (multipart, multi-file, folder drag-and-drop)

**Why.** Pneuma can only ingest what is reachable by URL. Anything on a user's disk, behind a login, or on an
intranet the server cannot reach has no way in; the benchmark harness had to stand up its own file server to feed
Pneuma a corpus (`BENCHMARKING.md`, D1). AssistantHub accepts uploads and lets a user drop a whole folder onto the
documents page. Upload is the most basic way into any RAG product.

**Design.** An uploaded file becomes a link like any other, so the whole pipeline, the delete cascade, and every
existing surface work unchanged. The upload is stored in the blob store (`IBlobStore`, already used for raw blobs)
under `uploads/{tenantId}/{linkId}/{filename}`, and the link's URL is a stable internal URI
(`pneuma-upload://{linkId}/{urlEncodedFilename}`). ContentRetrievalStage recognizes the scheme and reads the bytes from
the blob store instead of fetching; nothing else in the pipeline changes. Re-ingest re-reads the stored bytes.
Replacing a file stores a new version on the same link, and P1 then retires the old chunks.

**Server / backend.**
- `POST /v1.0/subjects/{id}/uploads` takes `multipart/form-data` with one or more `file` parts plus optional
  `labels`, `tags`, and `title` fields, and returns `201` with the created links (the `BulkSubmitLinkResponse`
  shape). Each part is streamed to the blob store. Nothing is buffered as base64; AssistantHub's JSON/base64 upload
  holds each file in memory several times over.
- `PUT /v1.0/links/{id}/content` replaces the stored file of an upload link and queues a re-ingest.
- `GET /v1.0/links/{id}/source` already exists and serves the stored bytes back for upload links.
- `SubjectLink` gains `SourceKind` (`Url`, `Upload`, `Inline`, `DataSource`), `OriginalFilename`, `ContentType`,
  and `SizeBytes`. The migration adds the columns with `Url` as the default kind.
- `IngestionSettings.MaxUploadBytes` (default 100 MB, clamped 1 MB to 2 GB) and `MaxFilesPerUpload` (default 100),
  also in `IngestionTuning` and the processing settings view. Watson's maximum request body size is raised to match.
- The upload's declared content type and file extension become hints for type detection (P12).
- The same checks as link submit: the subject needs an embedding model, an inference model, and an existing
  collection.

**Dashboard.**
- Subject dashboard `LinksView.jsx`: an "Upload files" button opening a new `FileUploadModal.jsx` (file picker with
  `multiple`, labels and tags through the existing label and tag editor), and a drop zone over the links table that
  accepts files and whole folders (walk `DataTransferItem.webkitGetAsEntry()` recursively, as AssistantHub's
  `fileDropUtils.js` does). The admin dashboard's `LinksView.jsx` gets the same, with a subject picker.
- A persistent `UploadProgressPanel.jsx` (docked, survives navigation) shows per-file upload progress
  (`XMLHttpRequest.upload.onprogress`), then the ingestion stage mapped from the link status, with retry and dismiss
  actions. The client uploads four files at a time; the server's queue does the real throttling.
- Link rows show an upload icon, the original filename, and the size, and upload links get a "Replace file" action.

**SDKs.** `uploadFiles(subjectId, files, options)` / `upload_files(...)` taking paths or streams, and
`replaceLinkContent(linkId, file)`. The C# SDK uses `MultipartFormDataContent` with streamed content; JavaScript
accepts `Blob` and `File` in browsers and paths in Node; Python uses multipart with open file handles.

**MCP.** Binary upload over MCP is a poor fit; agents use `pneuma_submit_content` (A2) instead. No upload tool.

**Postman.** An "Uploads" folder: one file, three files, a file with labels and tags, an over-size file (413), a
subject without models (400), and a request that is not multipart (415).

**Docs.** `REST_API.md` (a new section with a `curl -F` example), the `README.md` feature list, and the upload size
settings in the settings reference.

**Tests.** `UploadSuite` (new).
- Positive: one file creates one link and one job and reaches `Ingested` with the bytes read from the blob store (the
  fetcher is never called); three files in one request create three links; labels and tags reach the chunks;
  `GET /links/{id}/source` returns the exact bytes; replacing the content re-ingests and leaves one version
  searchable (with P1); deleting the link removes the stored upload.
- Negative: no file parts (400); a file over `MaxUploadBytes` (413, nothing stored); more than `MaxFilesPerUpload`
  files (400); a subject without models or a collection (400); a caller without permission to update the subject
  (403, audited); a filename with path traversal (`../x`) is sanitized; a zero-byte file (400); an upload to another
  tenant's subject (404).

**Depends on.** P12 for type hints and P1 for clean replacement.

### 2. P1: Replace the previous version on re-ingest; idempotent retries (RI-E)

**Why.** Re-ingesting a link creates a new job but never removes the previous job's chunks, Source node, or Cell
nodes, so both versions stay searchable and retrieval returns stale text beside current text. Retries within one job
re-create graph nodes as well. Every acquisition item in this plan (refresh, crawlers, connectors) re-ingests changed
content routinely, so this defect would multiply with each of them. Isis solved the same problem with
delete-then-create on keys derived from the parent (`{id}-c{n}`); AssistantHub has the defect (orphaned vectors on
reprocess and on crawler updates).

**Design.** A link has exactly one live version. A job writes under its own `jobId` tag as it does today, and only
after it finishes Indexing does it retire every earlier job's output for the same link: RecallDB documents tagged with
the link but a different job, and the LiteGraph Source and Cell nodes (and their edges) asserted by earlier jobs.
Entity nodes are shared and are kept; edges asserted only by a retired job are removed through the existing
consolidation bookkeeping. Retiring after success, rather than deleting first, means a failed re-ingest leaves the old
version searchable. Within a job, each retry first deletes what the previous attempt of the same job wrote, so retries
are idempotent.

**Server / backend.**
- `IngestionProcessor` calls a new `VersionRetirementService.RetireOlderVersionsAsync(link, job)` after Indexing, and
  before each retry attempt removes the attempt's own partial writes (graph by job, RecallDB by `jobId`).
- RecallDB deletion by filter (`documents/delete/filter`) for each earlier job id of the link, taken from the
  `ingestionjobs` table; `IGraphRepository.DeleteByJobAsync` for each earlier job.
- `SubjectLink.CurrentJobId` records the live version and is returned by `GET /links/{id}`.
- A job event "Retired N chunks and M nodes from earlier versions".
- Metric `pneuma_ingestion_retired_total{kind="chunk"|"node"}`.

**Dashboard.** The link detail views (subject `LinkDetailModal.jsx`, admin `IngestionLogModal.jsx`) show "Current
version: job_… (ingested …)", and a link's job list marks older jobs "Superseded". No new screens.

**SDKs.** `currentJobId` on the link model in all three SDKs. **MCP.** `pneuma_get_link` returns `currentJobId`.

**Postman.** The "Reingest link" request gets a test script asserting that the chunk count does not double.

**Docs.** `REST_API.md` re-ingest semantics ("the previous version stays searchable until the new one is indexed,
then it is removed") and `CHANGELOG.md` (Fixed).

**Tests.** `IngestionSuite` and `IngestionStagesSuite`.
- Positive: ingest, change the fake fetcher's content, re-ingest: RecallDB holds only the new job's chunks and the
  graph only the new Source and Cell nodes; shared entities survive; a retry after a failure at Embedding leaves
  exactly one set of chunks; `CurrentJobId` moves to the new job.
- Negative: a re-ingest that fails at Classification leaves the old version fully searchable and `CurrentJobId`
  unchanged; a retirement failure (RecallDB unavailable) ends the job `CompletedWithWarnings` (P3) with both versions
  present, and the next successful re-ingest removes both older versions; retirement never touches another link's
  chunks, even when the content is identical.
- Benchmark: ingest pneuma-live, re-ingest every link, and check that chunk counts are unchanged and retrieval
  metrics are within noise.

**Depends on.** Nothing. Unblocks A3 through A19.

### 3. A4: Web crawler (CrawlSharp)

**Why.** Documentation sites, knowledge bases, and product sites are the most common corpus for a subject, and today
each page must be submitted by hand. AssistantHub's web crawler (CrawlSharp 1.0.22) follows links within a scope,
reads `sitemap.xml`, honors `robots.txt` by default, throttles, and can render JavaScript.

**Design.** A `Web` source type on the A0 framework. The crawler enumerates URLs; each URL becomes a link on the
subject, keyed by the normalized URL; the regular pipeline ingests it. Two improvements over AssistantHub:
- **Enumerate without keeping page bodies.** AssistantHub holds every page body in memory until the crawl ends. Here
  the crawl records only URLs and version tokens (ETag, Last-Modified, a hash of the page), and saves each fetched
  page to the blob store keyed by URL and run, so ingestion reuses it instead of downloading it twice.
- **URL include and exclude patterns and a page cap**, which AssistantHub lacks.

**Settings** (`WebSourceSettings`, validated and clamped): `StartUrls` (1 to 50), `FollowLinks` (true), `MaxDepth`
(3, 1 to 20), `MaxPages` (1000, 1 to 100000), `Scope` (`SameHost` default, `SameRootDomain`, `ChildPaths`),
`IncludePatterns` and `ExcludePatterns` (globs over the full URL), `UseSitemap` (true), `RespectRobotsTxt` (true;
turning it off requires a tenant administrator and is audited), `CrawlDelayMs` (500, 0 to 60000),
`MaxParallelRequests` (4, 1 to 16), `UserAgent` (defaults to `IngestionSettings.UserAgent` with a `Pneuma-Crawler`
token), `RenderJavaScript` (false; uses the Playwright fetcher when true), `Authentication` (A20),
`DropQueryParameters` (tracking parameters such as `utm_*` removed during normalization), and `AllowedContentTypes`
(HTML, PDF, and Office documents by default).

**Server / backend.** `WebSourceConnector : IDataSourceConnector` wrapping CrawlSharp. URL normalization (lower-case
host, no fragment, listed parameters dropped, remaining parameters sorted, default port removed) so one page never
becomes two links, and `<link rel="canonical">` collapses duplicates. Every discovered URL passes the P5 fetch-safety
check. The crawler honors `Retry-After` on 429 and backs off per host.

**Dashboard.** The A0 source form renders these settings from the type catalog. "Preview" crawls to depth 1 and lists
what would be added. The run view shows discovered, skipped (robots, pattern, type, cap), added, changed, unchanged,
and removed counts.

**SDKs, MCP, Postman.** The generic source operations from A0 cover the crawler; `pneuma_create_source` accepts
`type: "Web"`. Postman gets a "Create web source" example and a preview request.

**Docs.** A "Crawling a web site" section in `DATA_SOURCES.md` (scope, robots, politeness, patterns) and each setting
in `REST_API.md`.

**Tests.** `WebCrawlerSuite` (new), against a loopback site served by a stub Watson server with a known link graph,
a `robots.txt`, a `sitemap.xml`, a redirect loop, a page with a canonical link, and query-string variants.
- Positive: depth and page caps are honored exactly; include and exclude patterns filter; sitemap URLs are added;
  canonical and tracking-parameter variants collapse to one link; a second run with unchanged ETags adds nothing; a
  changed page is re-ingested; a removed page is reported, and removed only when deletions are on.
- Negative: a `robots.txt` disallow is respected; a redirect loop terminates; off-scope links are skipped; a start URL
  at `127.0.0.1` or a private range is rejected by P5 unless the tenant allows it; `MaxDepth` 0 and `MaxPages` above
  the cap are rejected (400); a page that returns 500 is recorded as a failed item and retried on the next run.

**Depends on.** A0, P1, P5, and A20 for sites behind a login.

### 4. A0: Source and sync framework

**Why.** Every connector (crawler, sitemap, feed, bucket, share, SaaS system) needs the same machinery: configuration
with secrets, a schedule, enumeration, a delta against the last run, turning objects into links, deleting what
disappeared, a run history, and an operator UI to test, preview, start, and stop. AssistantHub built this once
(`CrawlPlan`, `CrawlOperation`, `CrawlerBase`, `CrawlSchedulerService`), and each of its crawlers is small as a result.
Built once in Pneuma, every later A item becomes a connector class, a settings type, and a form.

**Design.**
- **DataSource** (`dsrc_`): `TenantId`, `SubjectId`, `Name`, `Type` (`DataSourceTypeEnum`: `Web`, `Sitemap`, `Feed`,
  `S3`, `AzureBlob`, `Cifs`, `Nfs`, `LocalDirectory`, `Git`, `SharePoint`, `GoogleDrive`, `Confluence`, `Notion`,
  `Slack`, `Imap`, `Sql`), `Enabled`, the type's settings, a filter, a schedule, sync flags, `Labels` and `Tags`
  applied to every link it creates, and state (`Status`: `Idle`, `Running`, `Stopping`, `Error`; `LastRunId`,
  `LastRunUtc`, `LastSuccessUtc`, `NextRunUtc`).
- **Settings storage.** One typed DTO per type (`WebSourceSettings`, `S3SourceSettings`, and so on) stored in a
  per-type table (`datasourcesettings_web`, …) with real columns, per the rule against JSON blobs for known shapes.
  Secrets (passwords, tokens, keys, OAuth refresh tokens) live in `datasourcesecrets` (`source_id`, `name`,
  `ciphertext`), encrypted with `Aes256Cipher` and write-only. This avoids AssistantHub's plaintext credentials, which
  its GET routes and MCP tools return.
- **Filter:** `IncludePatterns`, `ExcludePatterns`, `AllowedContentTypes`, `MinSizeBytes`, `MaxSizeBytes`,
  `MaxObjects`, `ModifiedAfterUtc`.
- **Schedule:** `Manual`, `Interval` (`IntervalMinutes`, 5 to 525600), or `Cron` (`CronExpression` and `TimeZone`,
  evaluated with the Cronos package), plus `PauseUntilUtc`.
- **Sync flags:** `ProcessAdditions` (true), `ProcessUpdates` (true), `ProcessDeletions` (false by default, as in
  AssistantHub, so a misconfigured source cannot empty a subject), `MaxDeletionFraction` (0.2: a run that would delete
  more than 20% of the source's links holds until an operator confirms), and `RetryFailedItems` (true).
- **SourceItem** (`datasourceitems`): one row per object ever seen, with `ExternalKey` (URL, path, object key, or page
  id), `LinkId`, `VersionToken` (ETag, Last-Modified, revision id, or content hash), `SizeBytes`, `LastSeenRunId`,
  `Status` (`Active`, `Missing`, `Failed`, `Excluded`), and `LastError`. This is the delta baseline, kept in the
  database. AssistantHub keeps it in JSON files on local disk, which fails with more than one server and is lost with
  the container.
- **SyncRun** (`sync_`): `SourceId`, `Trigger` (`Schedule`, `Manual`, `Api`, `Webhook`), phase timestamps
  (enumeration, dispatch, ingestion), counters (enumerated, added, updated, unchanged, deleted, skipped, failed,
  bytes), `Status` (`Running`, `Ingesting`, `Succeeded`, `PartiallySucceeded`, `Failed`, `Cancelled`, `Held`), and
  `Error`, with a child table `syncrunitems` (`ExternalKey`, `Action`, `Outcome`, `Error`, `LinkId`, `JobId`). Runs
  older than the source's `RunRetentionDays` (default 30) are pruned by a background service.
- **Connector contract** (`IDataSourceConnector`): `ValidateSettings`; `TestConnectivityAsync`, which returns a
  layered result (settings, DNS, TCP, TLS, authentication, root access), each layer with a message, as AssistantHub's
  CIFS diagnostics do; `EnumerateAsync`, an `IAsyncEnumerable<SourceObject>` of key, name, content type, size,
  version token, modified time, and optional per-object labels and tags; and `OpenAsync(SourceObject)`, returning a
  stream and a content type. Connectors register with a `DataSourceConnectorFactory`, and each publishes a settings
  schema (fields, types, ranges, defaults, help text, secret flags) that the dashboard renders.
- **Sync engine** (`SourceSyncService`): enumerate, then compare with the source's items. **Added** objects become
  links (`SourceKind = DataSource`, `DataSourceId`, `ExternalKey`, and a URI such as `s3://bucket/key` or
  `smb://host/share/path`) with jobs. **Changed** objects re-ingest their link (P1 swaps versions). **Missing** objects
  delete their link when deletions are on, and are marked `Missing` otherwise. Items that failed last run are retried.
  ContentRetrievalStage opens `DataSource` links through `connector.OpenAsync` instead of HTTP. After dispatch the run
  is `Ingesting`, and it finishes only when every job it created is terminal, so a run succeeds only if its documents
  actually ingested (AssistantHub's `EnsureDocumentIngestionCompletedAsync` rule).
- **Scheduler** (`SourceSchedulerService`, a hosted service): every 30 seconds it atomically claims due sources
  (`NextRunUtc <= now` and `Status = Idle`, with a row-count check or `SKIP LOCKED`, so two servers never run the same
  source). At startup it marks runs left `Running` as `Failed` ("Recovered at startup") and resets their sources. A
  source has at most one run at a time.

**Server / backend routes.**
- `GET /v1.0/source-types`: the catalog of connector types with their settings schemas.
- `POST /v1.0/subjects/{id}/sources`, `GET /v1.0/subjects/{id}/sources`, and `GET /v1.0/sources` (tenant-wide).
- `GET|PUT|DELETE /v1.0/sources/{id}`. Delete takes `?deleteLinks=true|false`; the default is false, and the links
  stay as ordinary links.
- `POST /v1.0/sources/test` (unsaved settings) and `POST /v1.0/sources/{id}/test`.
- `POST /v1.0/sources/{id}/preview?maxObjects=100`: a dry-run enumeration with the would-be delta.
- `POST /v1.0/sources/{id}/sync` (202, returns the run) and `POST /v1.0/sources/{id}/stop`.
- `GET /v1.0/sources/{id}/runs`, `GET /v1.0/sync-runs/{id}`, `GET /v1.0/sync-runs/{id}/items`, and
  `GET /v1.0/sources/{id}/items?status=`.
- `POST /v1.0/sync-runs/{id}/confirm-deletions` for a run held by `MaxDeletionFraction`.
- Permissions: `ResourceTypeEnum.DataSource` with create, read, update, delete, and execute (sync and stop).
- Metrics: `pneuma_sync_runs_total{type,outcome}`, `pneuma_sync_objects_total{type,action}`,
  `pneuma_sync_run_duration_seconds{type}`, `pneuma_sync_bytes_total{type}`, and the gauge `pneuma_sync_running`.

**Dashboard.**
- Admin: a new "Sources" view (`SourcesView.jsx`) under Ingestion, listing name, subject, type, status, schedule,
  last result, and **next run**, with actions to edit, duplicate, enable or disable, sync now, stop, view runs, test,
  preview, and delete.
- Subject dashboard: a "Sources" tab beside Links, scoped to the subject.
- `SourceFormModal.jsx` renders the type's settings schema: grouped fields, inline help and ranges, secret fields
  shown as "set" with a replace button, a **Test connectivity** button that works before saving and shows each
  diagnostic layer, and a **Preview** button.
- `SyncRunsModal.jsx` lists runs with counters and durations; `SyncRunItemsModal.jsx` shows the per-object delta
  with outcome and error, filterable by action. A held run shows a confirm-deletions action.
- `LinksView.jsx` (both dashboards) adds a source column and filter, and badges links that a source owns.
- The admin home page's ingestion card adds "sources running" and "sync failures in the last 24 hours".

**SDKs.** `listSourceTypes`, `createSource`, `listSources`, `listSubjectSources`, `getSource`, `updateSource`,
`deleteSource`, `testSource`, `testSourceSettings`, `previewSource`, `syncSource`, `stopSource`, `listSyncRuns`,
`getSyncRun`, `listSyncRunItems`, `listSourceItems`, and `confirmSyncRunDeletions`.

**MCP.** `pneuma_enumerate_sources`, `pneuma_get_source`, `pneuma_create_source`, `pneuma_update_source`,
`pneuma_test_source`, `pneuma_preview_source`, `pneuma_sync_source`, `pneuma_stop_source`,
`pneuma_enumerate_sync_runs`, and `pneuma_get_sync_run`. The descriptions say that a sync is asynchronous and how to
poll it, and that secrets are write-only. Deleting a source is not exposed over MCP, because it can be irreversible.

**Postman.** A "Sources" folder with every route, one create example per connector type, and negatives (invalid
schedule, unknown type, and a test script asserting secrets are absent from responses).

**Docs.** A new `DATA_SOURCES.md` covering concepts (source, item, run, delta), scheduling, deletion safety, secrets,
and a section per connector that each A item fills in. Also `REST_API.md`, `MCP_API.md`, `TELEMETRY.md`, and
`README.md`.

**Tests.** `DataSourceSuite` (new), using a `FakeConnector` whose objects the test controls.
- Positive: the first run adds every object; a second run with no changes adds and updates nothing; a changed version
  token re-ingests exactly that link; a removed object with deletions on deletes its link and cascades; an item that
  failed last run is retried; a run ends `Succeeded` only after its jobs complete, and `PartiallySucceeded` when some
  fail; the source's labels and tags reach chunks; interval and cron schedules compute `NextRunUtc` correctly across a
  daylight-saving change; two scheduler instances racing claim a due source once; startup recovery resets a stuck
  run; pruning removes old runs and their items.
- Negative: with deletions off, links stay and items are marked `Missing`; a run that would delete more than
  `MaxDeletionFraction` is held and deletes nothing until confirmed; secrets never appear in GET, list, MCP, or request
  history; a draft connectivity test persists nothing; preview creates no links or jobs; a second sync while one is
  running returns 409; stop cancels enumeration and dispatch; an invalid cron expression (400); an interval under 5
  minutes (400); a source on another tenant's subject (404); a caller without DataSource permission (403, audited); a
  subject without models or a collection cannot get a source (400).

**Depends on.** P1 (changed objects), P2 (queue robustness under bursts of jobs), and P3 (run outcomes). Unblocks
A3 through A19.

### 5. A2: Inline content push API (text, Markdown, HTML, JSON)

**Why.** Agents, scripts, and integrations often have content in hand (a transcript, a generated report, a ticket, a
record from another system) and no URL to give. Isis's whole write path is the caller pushing content, and Pneuma has
no raw-text route (`BENCHMARKING.md`, D1). It is also the simplest path for an integration that does its own
acquisition.

**Design.** `POST /v1.0/subjects/{id}/content` with `{ title, content, contentType, externalKey?, labels, tags }`. The
content is stored in the blob store like an upload (A1) and becomes a link with `SourceKind = Inline` and the URI
`pneuma-inline://{linkId}`. `contentType` is `text/plain`, `text/markdown`, `text/html`, or `application/json`, and it
replaces type detection because the caller has declared it. `externalKey` makes the call an upsert: a second push with
the same key to the same subject replaces that link's content (P1 retires the old version) instead of adding a
duplicate, which is Isis's slug behavior. `POST /v1.0/subjects/{id}/content/batch` takes up to 100 items and returns a
result per item; unlike Isis's batch, it never skips an invalid item silently and never stops at the first error.

**Server / backend.** Size limit `MaxInlineContentBytes` (default 10 MB). `SubjectLink.ExternalKey` with a unique index
on `(tenant_id, subject_id, external_key)`; two concurrent pushes with the same key are serialized by that index (the
loser retries as an update). Content is sanitized (P18) before it is stored.

**Dashboard.** Subject dashboard `LinksView.jsx`: an "Add text" button opening `AddTextModal.jsx` (title, a Markdown
editor with preview, labels, and tags). Such links show "Pasted text" in place of a URL, and the detail modal shows the
text.

**SDKs.** `submitContent(subjectId, item)` and `submitContentBatch(subjectId, items)`.

**MCP.** `pneuma_submit_content` (subject, title, content, contentType, externalKey, labels, tags). The description
tells agents to reuse `externalKey` when updating something they wrote before and never to submit secrets.

**Postman.** An "Inline content" folder: Markdown, JSON, upsert by key, a batch with one invalid item, and an
over-size item (413).

**Docs.** `REST_API.md`, `MCP_API.md`, and an agent example in the MCP instructions (S1).

**Tests.** `InlineContentSuite` (new).
- Positive: Markdown content ingests without calling type detection; the same `externalKey` twice leaves one link and
  one live version with the second content searchable; a batch of three creates three links; JSON content goes
  through DocumentAtom's JSON route.
- Negative: empty content (400); an unsupported `contentType` (400); content over the limit (413); a batch over 100
  items (400); a batch with one invalid item returns per-item results and still creates the valid ones; the same
  `externalKey` on another subject creates a separate link; lone surrogates are replaced (P18).

**Depends on.** A1's storage path, P1, and P18.

### 6. P3: No silent partial loss: completeness accounting

**Why.** Several stages drop work with only a log warning while the job still ends `Completed`: a failed
classification batch (part of the document never reaches the graph), a failed cell summary, a failed Cell node
creation, and chunks without an embedding (dropped at Indexing). An operator cannot tell a complete ingest from a
partial one. AssistantHub has the same flaw: a failed RecallDB batch still reports `Completed`.

**Design.** Every stage reports what it received and what it produced into a per-job `IngestionCompleteness` record:
cells extracted, cells classified, classification batches failed, Cell nodes created and failed, summaries attempted
and failed, chunks produced, chunks embedded, and chunks indexed. A new terminal status, `CompletedWithWarnings` for
jobs and `IngestedWithWarnings` for links, is used when anything was dropped, with a warnings list naming each loss.
`IngestionSettings.PartialLossPolicy` is `Warn` (default) or `Fail`, which fails the job on any loss so it is retried.
Content chunks without an embedding always fail the job; they are never dropped.

**Server / backend.** `IngestionContext` gains the counters, and each stage fills in its own. `IngestionJournal`
chooses the final status. The new status values are added to validation in every provider. Metric
`pneuma_ingestion_partial_total{stage,reason}`.

**Dashboard.** A warning badge and count on job and link rows in both dashboards, the warnings listed in the job log
modal, a "With warnings" status filter, and warnings counted apart from failures on the home page.

**SDKs, MCP.** The new status values plus `warnings` and `completeness` on jobs; `pneuma_get_job` returns both.

**Postman.** Updated job examples. **Docs.** Status tables in `REST_API.md`, and `TELEMETRY.md`.

**Tests.** `IngestionStagesSuite`.
- Positive: a clean run records matching in and out counts and ends `Completed`.
- Negative: one of three classification batches fails, ending `CompletedWithWarnings` with the batch named, or
  `Failed` under `PartialLossPolicy = Fail`; one summary fails, giving a warning; an embedding call returns fewer
  vectors than chunks and the job fails rather than dropping them; a Cell node creation fails, giving a warning with
  the cell index; the counters survive a retry without double counting.

### 7. P5: Fetch safety: SSRF guard, size cap, TLS, politeness

**Why.** The content fetcher fetches any URL a user submits: `http://127.0.0.1:…`, the cloud metadata address
(`169.254.169.254`), services on the Docker network (RecallDB, Postgres, LiteGraph), and `file:` URIs through Chromium.
There is no size limit, certificate errors are ignored (`IgnoreHTTPSErrors = true`), and nothing limits the request
rate per host. Crawlers (A4, A5, A6) multiply the exposure because they follow links found in content.

**Design.**
- **Scheme allow-list:** `http` and `https` for fetched links. Upload, inline, and source links are resolved before
  the fetcher is involved.
- **Address guard:** resolve the host and reject loopback, link-local, private (RFC 1918), unique-local IPv6,
  multicast, and metadata addresses. The check is repeated after every redirect and on the address the socket actually
  connects to, through a `SocketsHttpHandler.ConnectCallback`, which defeats DNS rebinding. In Playwright, every
  request goes through `page.RouteAsync` with the same check, and `file:`, `ftp:`, and `chrome:` are blocked.
- **Allow-lists:** `IngestionSettings.AllowedPrivateHosts` (system) and a per-tenant list, for deployments that
  legitimately ingest intranet sites.
- **Size cap:** `MaxDownloadBytes` (default 100 MB), enforced while streaming rather than after.
- **TLS:** certificates are validated by default; `AllowInvalidCertificates` per tenant for internal PKI, audited.
- **Politeness:** a per-host concurrency limit (`MaxRequestsPerHost`, default 2) and minimum interval, shared by the
  fetcher and every crawler, honoring `Retry-After`.

**Server / backend.** A `FetchSafetyPolicy` used by `HttpContentFetcher`, `PlaywrightContentFetcher`, and every HTTP
connector. Link submission validates the URL shape and scheme up front, returning 400 instead of queuing a job that
will fail. A blocked fetch is an `IngestionHardFailException` with category `Blocked` (P10), and each blocked
submission is audited.

**Dashboard.** The processing settings view gets the private-host allow-list, the size cap, and the TLS option, with
warnings in the settings tips. A blocked link shows "Blocked: private address" rather than a generic failure.

**SDKs, MCP.** No new operations. **Postman.** Negatives that submit `http://127.0.0.1:8600/` and `file:///etc/passwd`
(both 400).

**Docs.** A "Fetch safety" section in `README.md` and the settings reference, and `CHANGELOG.md` (Security).

**Tests.** `FetchSafetySuite` (new).
- Positive: a public address passes; a host on the allow-list passes; a redirect that stays public is followed.
- Negative: loopback, `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `169.254.169.254`, `[::1]`, and `fc00::/7` are
  blocked; a public URL that redirects to a private address is blocked; a host whose DNS answer changes to a private
  address between the check and the connection is blocked (tested with a custom resolver); `file:` and `ftp:` are
  rejected at submission; a response over `MaxDownloadBytes` is aborted mid-stream; an invalid certificate fails
  unless allowed.

### 8. P2: Durable queue: atomic claim, crash recovery, one job per link

**Why.** `ClaimNextQueuedAsync` selects the oldest queued job and updates it without checking that the update
affected a row, so two server processes can claim the same job. Jobs left `Processing` by a crash or restart stay that
way forever. Nothing stops two jobs for the same link from running at once (a re-ingest while the first ingest is
still running), which races on the link's chunks and nodes. The claim also sets the stage to `TypeDetection`, which is
stale now that ContentRetrieval runs first. Connectors enqueue in bursts, so these become everyday problems.

**Design.**
- **Atomic claim.** Postgres: `UPDATE … WHERE id = (SELECT id … FOR UPDATE SKIP LOCKED LIMIT 1) RETURNING *`.
  SQL Server: `UPDATE TOP (1) … WITH (UPDLOCK, READPAST) OUTPUT inserted.*`. MySQL 8: `SELECT … FOR UPDATE SKIP LOCKED`
  inside a transaction. SQLite: the existing write semaphore plus an affected-row check.
- **Leases.** A claimed job gets `LeaseOwner` (the server instance id) and `LeaseExpiresUtc`, renewed by a heartbeat
  every 30 seconds while it runs. A sweeper requeues jobs whose lease expired (incrementing the attempt count, with
  the event "Requeued after the worker stopped"), and at startup an instance requeues the jobs it owned.
- **One active job per link.** A new job for a link that already has a queued job supersedes it (the queued job is
  cancelled as "Superseded"); if a job for the link is running, the new one waits, because the claim skips jobs whose
  link has a running job.
- The claim sets the stage to `ContentRetrieval`.

**Server / backend.** Columns `lease_owner`, `lease_expires_utc`, and `attempt_count` on `ingestionjobs` in every
provider; a heartbeat in `IngestionWorkerService`; metric `pneuma_ingestion_requeued_total{reason}`.

**Dashboard.** The live view (`IngestionLiveView.jsx`) shows each job's lease owner and attempt count.

**SDKs, MCP, Postman.** New fields only.

**Docs.** A "Running more than one server" section in `README.md`.

**Tests.** `IngestionQueueSuite` (new), in the shared database contract suites so it runs against all four providers.
- Positive: fifty concurrent claims never return the same job twice; a job with an expired lease is requeued once and
  runs; an instance requeues its own jobs at startup; the claim sets `ContentRetrieval`.
- Negative: a live lease is never stolen; a re-ingest while a job is queued cancels the queued job; while a job is
  running, a new job for the same link waits; a job that exceeds `MaxAttempts` through requeues ends `Failed` with
  category `WorkerLost`.

### 9. P4: Shared transient-retry policy and per-endpoint limiter for all model calls

**Why.** Only embeddings retry on rate limiting, and they detect it by searching the error text for "429".
Classification and summarization calls have no retry: a 429 from a shared gateway silently drops a summary, or fails
the classification, which then retries the whole job from the fetch. The benchmark lost documents to exactly this on a
shared model gateway. Isis puts a `TransientRetryHandler` (429, 502, 503; honors `Retry-After`; jitter; buffered body)
under every model client. AssistantHub also unwraps a transient error hidden in a proxy's 500 response and limits
concurrency per endpoint to what the endpoint says it can take.

**Design.**
- A `TransientRetryHandler : DelegatingHandler` on the shared handler in `ModelClientFactory`, so every PolyPrompt
  call (embedding, chat, completion, rerank) gets it. It retries 408, 429, 502, 503, 504, and a 500 whose body names
  one of those codes; honors `Retry-After` (seconds or a date); backs off exponentially with full jitter from 1 second
  to a 30-second cap; and stops at the runner's `MaxRetries` (default 5). It never retries 400, 401, 403, 404, or 422.
- A per-runner concurrency limiter (`EndpointConcurrencyLimiter`) sized by a new `ModelRunner.MaxConcurrentRequests`
  (default 4), shared by ingestion and query traffic so ingestion cannot starve queries. Leases are taken in a fixed
  order when a stage needs two runners, which avoids deadlock.
- Exhausted retries raise `ModelEndpointUnavailableException`. P10 classifies it as `ModelUnavailable`, which the job
  retries after a longer delay, and query routes map it to 503.
- The string-matching `EmbedWithRetryAsync` is removed.

**Server / backend.** `ModelRunner.MaxConcurrentRequests` and `MaxRetries` (migration, validation, runner form).
Metrics `pneuma_model_retries_total{runner,status}` and `pneuma_model_limiter_wait_seconds{runner}`.

**Dashboard.** The two new fields on the admin model runner form, with tips, and retry counts on the model runner
health view.

**SDKs, MCP, Postman.** Runner fields only.

**Docs.** A "Running against a shared model gateway" note in `README.md`, and `TELEMETRY.md`.

**Tests.** `ExternalServicesSuite`, growing `StubEmbeddingEndpoint` into a stub model server that can answer 429 with
`Retry-After`, 502, a 500 wrapping a 429, and 400.
- Positive: embedding, chat, and summarization each succeed after two 429 responses; `Retry-After: 2` delays at least
  two seconds; the limiter never lets more than `MaxConcurrentRequests` calls reach the stub at once.
- Negative: a 400 is not retried; retries stop at `MaxRetries` and raise `ModelEndpointUnavailableException`; a
  cancelled token ends a backoff wait immediately; a summarization 429 no longer drops the summary.

### 10. P6: Embedding model profiles: task prefixes and retrieval-sized chunks (relates to RI-Y)

**Why.** Several embedding models are trained with task prefixes and lose accuracy without them. nomic-embed-text
expects `search_document: ` before documents and `search_query: ` before queries; mxbai, the BGE family,
snowflake-arctic-embed, and E5 have their own. Pneuma sends raw text for both (the string `search_document` appears
nowhere in `src`). Isis adds the prefixes automatically by model name through `EmbeddingModelProfiles`. Its benchmark
also found that retrieval-sized chunks beat chunks that fill the model's window: capping nomic chunks at 128 tokens,
and other models at 75% of their budget up to 256, raised Atlas nDCG@10 from 0.802 to 0.831.

**Design.** An `EmbeddingModelProfiles` table in code, overridable in settings, mapping a model-name pattern to a
document prefix, a query prefix, the maximum input tokens, the tokenizer family, and a recommended chunk size. Applied
in `NativeSemanticProcessor` for documents and in `GroundedQueryService.EmbedQueryAsync` for queries, behind a
per-runner switch `ApplyTaskPrefixes` (default true for new runners). A subject's `ChunkMaxTokens` may be 0 ("auto"),
resolved from the profile; an explicit value still wins. Prefixes change every vector, so runners that exist at
migration time get `ApplyTaskPrefixes = false` until an operator re-embeds.

**Server / backend.** A re-embed operation, `POST /v1.0/subjects/{id}/reembed`, that re-runs Chunking, Embedding, and
Indexing from the stored cell artifacts for every link, one job per link, with no fetch and no classification. It also
serves P7 and P8 when their settings change. The embedding cache key includes the prefix.

**Dashboard.** The model runner form gets "Apply the model's task prefixes", with a tip naming the prefixes for the
selected model. Subject settings show chunk size as "Auto (128 for nomic-embed-text)". A "Re-embed subject" action
uses a custom confirm modal that states the cost.

**SDKs.** `reembedSubject(subjectId)`. **MCP.** Not exposed; it is a heavy operation. **Postman.** A re-embed request.

**Docs.** Model guidance in `README.md`, `REST_API.md`, and the benchmark result in `benchmarks/RESULTS.md`.

**Tests.** `EmbeddingProfileSuite` (new).
- Positive: a nomic runner sends `search_document: ` for chunks and `search_query: ` for queries (asserted at the
  stub); an unknown model gets no prefix; auto chunk size resolves to 128 for nomic; re-embed replaces vectors without
  calling the fetcher or the classifier.
- Negative: `ApplyTaskPrefixes = false` sends raw text; an explicit `ChunkMaxTokens` overrides the profile; re-embed
  on a subject with running jobs returns 409.
- Benchmark: Meridian, Atlas, and pneuma-live retrieval before and after prefixes and auto chunk size.

### 11. S1: MCP ingestion write tools and agent instructions

**Why.** Pneuma's MCP surface can read jobs and links but cannot submit, re-ingest, stop, or delete anything
(`MCP_API.md` says write tools will come "as they land"). An agent that finds a relevant page has to ask a human to add
it. AssistantHub exposes full document, rule, and crawl-plan CRUD over MCP. Isis shows how to steer agents: its MCP
`initialize` result carries server instructions that tell the model when to write and how (reuse keys, never store
secrets), and its tool descriptions are written as behavior rather than as API summaries.

**Design.** New tools, each requiring the same permission as its REST route:
`pneuma_submit_link` (subject, url, title, labels, tags), `pneuma_submit_links` (bulk, at most 100),
`pneuma_submit_content` (A2), `pneuma_reingest_link`, `pneuma_stop_job`, `pneuma_restart_job`, and
`pneuma_get_link_chunks` (bounded, to let an agent check what was stored). Deletion stays off MCP. Server
instructions gain an "Adding knowledge" paragraph: check `pneuma_enumerate_links` for the URL first, prefer
`externalKey` upserts for generated content, poll `pneuma_get_job` rather than resubmitting, and never submit
credentials or personal data. Every write tool's description states that ingestion is asynchronous and how long it
usually takes.

**Server / backend.** Registrations in `McpToolCatalog` and handlers that call the existing services (not the REST
routes) so behavior matches. Tool calls are captured in request history like REST calls.

**Dashboard.** The API Explorer's MCP tab (if present) lists the new tools; otherwise no change.

**SDKs.** Not applicable. **Postman.** An "MCP" folder with a `tools/call` request per new tool.

**Docs.** `MCP_API.md` (the tool table and a worked "agent adds a page, waits, verifies" example) and the agent
instructions shipped with the product.

**Tests.** `McpSuite`.
- Positive: `pneuma_submit_link` creates a link and job; `pneuma_submit_content` with the same key twice leaves one
  link; `pneuma_reingest_link` queues a job; `pneuma_stop_job` cancels; `tools/list` includes each tool with a schema.
- Negative: a read-only credential gets a permission error (and an audit record); an invalid URL is rejected before a
  job exists; bulk over 100 is rejected; a link id from another tenant is not found.

### 12. P8: Contextual chunk header on the embedding text (RI-O)

**Why.** A chunk cut from the middle of a document often lacks the words that say what it is about: the document
title, the section heading, the product name. Isis prepends a `title: summary` header to the text it embeds (not to
the text it stores), within a budget of 25% of the chunk, and its benchmark attributes part of its gain to it.
Pneuma has richer context than Isis: the link title, the page title found by DocumentAtom, and the heading path of
the cell.

**Design.** `SemanticChunk` gains `EmbeddingText`, built as `"{documentTitle} > {headingPath}\n\n{chunkText}"` and
trimmed to `HeaderBudgetFraction` (default 0.25) of the chunk budget on a word boundary, with the header's tokens
counted against the budget so the embedded text still fits the model. The stored and returned text stays the chunk
text. A per-subject switch `ChunkHeaders` (`None`, `Title`, `TitleAndHeadings`; default `TitleAndHeadings` for new
subjects, `None` for existing ones until re-embedded).

**Server / backend.** Heading paths come from DocumentAtom cells (Markdown and HTML headings; PDF outline where
DocumentAtom provides it), carried on each cell by CellExtractionStage. The document title is the link title, else the
first top-level heading, else the file name. EmbeddingStage embeds `EmbeddingText`; the cache key uses it.

**Dashboard.** Subject settings: a "Chunk headers" selector with a tip. The chunk viewer (`/links/{id}/chunks`) shows
the header in a muted line above each chunk.

**SDKs, MCP.** The `chunkHeaders` subject field; chunk responses include `embeddingHeader`.

**Postman.** Subject update example. **Docs.** `REST_API.md`, the subject settings reference.

**Tests.** `ChunkingSuite`.
- Positive: the header is in the embedded text and not in the stored text; the header plus chunk fits the token
  budget; a long title is truncated at a word boundary.
- Negative: `ChunkHeaders = None` embeds the bare chunk; a cell without headings gets the title only; a header budget
  outside 0 to 0.5 is rejected.
- Benchmark: Meridian (whose alias and paraphrase questions depend on titles) and pneuma-live before and after.

### 13. P9: Per-subject enrichment switches (classification, summaries, summary indexing)

**Why.** Every document costs one classification call per 25 cells and one summary call per cell over 128
characters. The benchmark measured the summaries' effect: with summary chunks in the index, evidence-in-context fell
from 0.870 to 0.744 and answer accuracy from 0.565 to 0.488 on pneuma-live. The only way to turn summarization off
today is to raise `SummarizationMinCellLength` through a concurrency override, which is a workaround, and there is no
way to skip graph classification for a subject that only needs search.

**Design.** Three subject fields, each with a system default:
- `ClassificationMode`: `Full` (today's behavior) or `Off` (no LLM classification; the graph gets the Source and Cell
  nodes only, so provenance and neighbor expansion still work).
- `SummarizeCells` (true or false).
- `IndexSummaries` (`Off`, `Separate`, `Blended`): `Off` stores summaries in the graph only; `Separate` indexes them
  with `chunkKind = summary` and retrieval excludes them unless a request asks; `Blended` is today's behavior. The
  default for new subjects is `Off`, based on the benchmark.

**Server / backend.** ClassificationStage and SummarizationStage skip cleanly and record "skipped by subject setting"
events. Retrieval honors `IndexSummaries = Separate` with a tag filter on `chunkKind`. A subject without an inference
model becomes valid when classification and summaries are both off (search-only subjects). The `SummarizationMinCellLength`
workaround stays as a tuning knob.

**Dashboard.** Subject create and edit (admin `SubjectsView.jsx`, subject dashboard settings, and the setup wizard): an
"Enrichment" section with the three settings, tips that state the cost and the benchmark effect, and a note that
changing them affects new ingests (with a re-ingest shortcut).

**SDKs, MCP.** Subject fields in all three SDKs and in `pneuma_create_subject` / `pneuma_update_subject`.

**Postman.** A "search-only subject" create example. **Docs.** `REST_API.md`, `README.md` (choosing a profile),
`BENCHMARKING.md` (the lean profile becomes a real setting instead of a threshold override).

**Tests.** `IngestionStagesSuite` and `IngestionSuite`.
- Positive: `ClassificationMode = Off` ingests with no completion call and creates Source and Cell nodes;
  `SummarizeCells = false` makes no summary calls; `IndexSummaries = Separate` stores summary chunks that retrieval
  skips by default and returns when asked.
- Negative: a subject with classification on and no inference model is still rejected (400); invalid enum values
  (400); switching a setting does not alter already-ingested links until they are re-ingested.

### 14. P10: Structured failure categories, codes, and attempt history

**Why.** A failed job carries free text only. Operators cannot filter "all failures caused by the model gateway" or
"all unsupported files", dashboards cannot suggest a fix, and retry decisions depend on exception types scattered
through the processor. `NotSupportedException` for an unmapped type is retried three times although it can never
succeed. AssistantHub and Isis share the weakness in reporting; Isis's `ErrorClassifier` at least maps exceptions to
HTTP statuses consistently.

**Design.** `IngestionFailureCategoryEnum`: `Fetch` (HTTP error from the source), `Blocked` (P5), `TooLarge`,
`UnsupportedType`, `Extraction` (DocumentAtom failed), `NoContent`, `ModelUnavailable`, `ModelRejected` (4xx from a
model, including context length), `Configuration` (no model, no collection), `Storage` (RecallDB, LiteGraph, blob),
`Timeout`, `WorkerLost`, `Cancelled`, `Internal`. Each has a retry policy (none, short, long) and a remediation string.
A job keeps an attempt history (`ingestionjobattempts`: attempt number, stage, category, message, started, ended).

**Server / backend.** An `IngestionFailureClassifier` maps exceptions (including `HttpRequestException` status codes,
`IngestionHardFailException`, and the P4 exception) to categories; the processor asks it whether and when to retry.
Jobs and links get `FailureCategory`. `GET /v1.0/jobs?failureCategory=` filters. Metric label `category` on
`pneuma_ingestion_jobs_total{outcome="failed"}`.

**Dashboard.** A category chip on failed rows, a category filter in the jobs and links views, the remediation text in
the job log modal, and an attempts table. The home page's failure card groups by category.

**SDKs, MCP.** `failureCategory` and `attempts` on job models; a category filter on job listings and on
`pneuma_enumerate_jobs`.

**Postman.** Filter example. **Docs.** A failure-category table with remediation in `REST_API.md`.

**Tests.** `IngestionStagesSuite`.
- Positive: each category is produced by its trigger (fake fetcher 404 gives `Fetch`, Unknown type gives
  `UnsupportedType`, stub model 429 exhaustion gives `ModelUnavailable`, stub 400 context-length gives
  `ModelRejected`); the attempt history lists each attempt.
- Negative: `UnsupportedType`, `Blocked`, `Configuration`, and `ModelRejected` are not retried; an unknown exception
  becomes `Internal` and is retried; the filter rejects an unknown category (400).

### 15. A3: Scheduled refresh of existing links

**Why.** Pages change. Today a link is ingested once and only re-ingested by hand, so subjects drift out of date. The
content-hash delta already makes an unchanged re-fetch cheap. AssistantHub schedules re-crawls of whole sources;
Pneuma also needs refresh for links submitted one at a time.

**Design.** `RefreshIntervalMinutes` on a link (null means never) and a subject default `DefaultRefreshIntervalMinutes`.
`NextRefreshUtc` is set after each successful ingest. A `LinkRefreshService` (hosted) claims due links in small
batches, spreads them with jitter so a subject's links do not all refresh at once, and queues a re-ingest with trigger
`Refresh`. With P14, an unchanged page costs one conditional request. Links owned by a source (A0) are refreshed by
their source's schedule instead.

**Server / backend.** Columns on `subjectlinks`; `PUT /v1.0/links/{id}` accepts the interval; a bulk update
`POST /v1.0/links/refresh-interval` for many links. Job `Trigger` (`Submit`, `Reingest`, `Refresh`, `Source`, `Upload`,
`Api`). Metric `pneuma_link_refresh_total{outcome=changed|unchanged|failed}`.

**Dashboard.** Link detail: "Refresh every …" with presets (never, daily, weekly, monthly); the links table gets a
"Next refresh" column and a bulk action. Subject settings: the default interval.

**SDKs, MCP.** `refreshIntervalMinutes` on links and subjects, and `setLinksRefreshInterval`. MCP `pneuma_submit_link`
accepts the interval.

**Postman.** Set and bulk-set interval requests. **Docs.** `REST_API.md`, `README.md`.

**Tests.** `LinkRefreshSuite` (new).
- Positive: a due link is re-ingested once; an unchanged page completes early and moves `NextRefreshUtc`; a changed
  page replaces its version (P1); jitter spreads 100 due links over the window.
- Negative: an interval under 60 minutes is rejected; a failed refresh keeps the old version and backs off; a deleted
  or inactive link is never refreshed; source-owned links are ignored by the service.

### 16. A5: Sitemap source

**Why.** Most documentation sites publish a sitemap that lists every page with a last-modified date. Ingesting from
the sitemap is more complete and far cheaper than crawling: nothing is fetched to discover pages, and unchanged pages
are skipped without a request. AssistantHub reads only the root `sitemap.xml` as part of its crawl.

**Design.** A `Sitemap` source type: `SitemapUrls` (sitemaps or sitemap indexes; `robots.txt` `Sitemap:` lines are
followed when a site root is given), `IncludePatterns`, `ExcludePatterns`, `MaxUrls` (10000), and `UseLastModified`
(true). Enumeration parses sitemap indexes recursively (gzip supported), yields each `<loc>` with `<lastmod>` as the
version token, and hands the rest to the A0 engine.

**Server / backend.** `SitemapSourceConnector`, streaming XML parsing (`XmlReader`, DTD processing off, size capped),
the P5 policy on every sitemap and page URL.

**Dashboard.** The A0 form; preview shows the first 100 URLs with their last-modified dates.

**SDKs, MCP, Postman.** Generic source operations; a sitemap create example.

**Docs.** A `DATA_SOURCES.md` section.

**Tests.** `SitemapSourceSuite` (new), with a stub server serving a sitemap index, a gzipped child sitemap, and pages.
- Positive: every URL is enumerated; `lastmod` drives change detection; patterns filter; a sitemap index nests.
- Negative: an XML bomb or DTD is rejected; a sitemap over the size cap is rejected; a sitemap URL at a private address
  is blocked; a malformed sitemap fails the run with category `Extraction` and changes nothing; a URL outside the
  sitemap's host is skipped.

### 17. P7: Model-aware token budget and re-chunk on context-length errors (RI-F)

**Why.** TextChunker now counts tokens in the embedding model's tokenizer (round 2 of the benchmark), but the chunk
size is still the subject's `ChunkMaxTokens` with no check against the model's real input limit, no safety margin for
tokenizer differences, no allowance for special tokens and task prefixes (P6), and no recovery when the model rejects
a chunk as too long. Isis hit this directly: chunks sized to the exact limit made 19% of SciFact fail. It fixed it with
a 1% margin (at least 2 tokens), a reserve for `[CLS]` and `[SEP]` on WordPiece models, deduction of the task prefix,
and a re-chunk at 0.75, 0.5, and 0.3 of the budget when the model reports a context-length error.

**Design.** The effective budget is `min(ChunkMaxTokens, profile.MaxInputTokens − prefix − special − margin)`, from P6's
profiles plus a per-runner `MaxInputTokens` override. ChunkingStage clamps to it and records the effective value in
the job log. EmbeddingStage catches a context-length rejection (P10's `ModelRejected` with a context-length message),
re-chunks the offending cell at the next scale, and retries that cell only.

**Server / backend.** `ModelRunner.MaxInputTokens` (0 means use the profile). Metric
`pneuma_ingestion_rechunk_total{scale}`.

**Dashboard.** Model runner form: "Maximum input tokens" with the profile's value as placeholder. Subject settings
show the effective chunk size when it is lower than the configured one.

**SDKs, MCP.** Runner field. **Postman.** Runner example. **Docs.** `REST_API.md`, model guidance in `README.md`.

**Tests.** `ChunkingSuite` and `ExternalServicesSuite`.
- Positive: a subject with `ChunkMaxTokens = 8192` on a 512-token model produces chunks that fit with the margin; a
  stub that rejects inputs over N tokens with a context-length error triggers a re-chunk and the job completes; the
  prefix is counted.
- Negative: a model that rejects even the 0.3 scale fails the cell with `ModelRejected` (and P3 records it); a
  non-context 400 does not trigger re-chunking.

### 18. A7: Amazon S3 and S3-compatible bucket source

**Why.** Organizations keep document sets in object storage: exports, archives, data lakes, and the output of other
systems. S3-compatible stores (MinIO, Less3, Wasabi, Cloudflare R2) use the same API. Pneuma already depends on
Blobject.Core 5.1.0, whose provider packages include Amazon S3.

**Design.** An `S3` source type. Settings: `Endpoint` (blank for AWS), `Region`, `Bucket`, `Prefix`,
`IncludeSubfolders` (true), `UsePathStyle` (for S3-compatible stores), `AccessKey` and `SecretKey` (secrets), or
`UseInstanceCredentials` (the server's IAM role), plus the A0 filter. Enumeration lists objects (paged) and uses the
ETag and size as the version token, so unchanged objects cost nothing. `OpenAsync` streams the object.

**Server / backend.** `S3SourceConnector` over Blobject's S3 provider (or AWSSDK.S3, already referenced, if Blobject
lacks paging by prefix). Connectivity test layers: endpoint reachable, credentials valid, bucket exists, list
permission, read permission on one object. Object URIs `s3://bucket/key`.

**Dashboard.** The A0 form, with an "S3-compatible" toggle that reveals endpoint and path style.

**SDKs, MCP, Postman.** Generic source operations; an S3 example and a Less3 (S3-compatible) example in Postman.

**Docs.** A `DATA_SOURCES.md` section including the minimal IAM policy (`s3:ListBucket`, `s3:GetObject`).

**Tests.** `S3SourceSuite` (new), against the Less3 container already in the development stack, or a stub
S3 server on loopback.
- Positive: objects under a prefix are enumerated with paging past 1000 keys; a changed ETag re-ingests; a deleted
  object is removed when deletions are on; content type comes from the object metadata.
- Negative: wrong credentials fail the connectivity test at the authentication layer; a missing bucket fails at the
  root layer; objects above `MaxSizeBytes` are skipped and counted; a secret key never appears in responses.

### 19. A13: SharePoint Online and OneDrive source (Microsoft Graph)

**Why.** For many enterprises, SharePoint document libraries and OneDrive are where policies, procedures, and project
documents live. It is the most requested enterprise connector, and neither peer has it.

**Design.** A `SharePoint` source type using Microsoft Graph with an Entra ID app registration (client credentials:
`TenantId`, `ClientId`, `ClientSecret` or certificate, all secrets). Settings: `SiteUrl` or `DriveId`, `LibraryName`,
`FolderPath`, `IncludeSubfolders`, and the A0 filter. Enumeration uses the Graph **delta** API
(`/drives/{id}/root/delta`), storing the delta link as the source's cursor so later runs fetch only changes and
deletions, which avoids a full listing. The version token is the item's `eTag`/`cTag`. Office files are downloaded as
is (DocumentAtom handles Word, Excel, and PowerPoint); pages (`.aspx`) are fetched through the pages API as HTML.
Permissions are not mirrored in this item; a subject is readable by whoever can read the subject.

**Server / backend.** `SharePointSourceConnector` with the Graph REST API over `HttpClient` (or Microsoft.Graph SDK),
token caching, throttling that honors Graph's `Retry-After`. A `Cursor` column on DataSource for delta links (also used
by A14, A15, A16). Connectivity test: token acquisition, site resolution, drive access.

**Dashboard.** The A0 form, with a setup guide link explaining the app registration and the `Sites.Read.All` (or
`Sites.Selected`) permission.

**SDKs, MCP, Postman.** Generic operations; a SharePoint example.

**Docs.** A `DATA_SOURCES.md` section with step-by-step app registration.

**Tests.** `SharePointSourceSuite` (new), against a loopback stub that implements the Graph token, site, drive,
delta, and content endpoints.
- Positive: the first run lists all items and stores the delta link; the second run uses it and sees only the changed
  item; a deleted item arrives through delta and is removed when deletions are on; 429 with `Retry-After` is honored.
- Negative: an invalid client secret fails at the authentication layer; an expired delta link (410) triggers a full
  re-enumeration; items in excluded folders are skipped; the client secret is never returned.
- A live suite gated by `PNEUMA_TEST_SHAREPOINT_*` environment variables.

### 20. P12: Type-detection hints, hard-fail on unsupported types, keep Markdown

**Why.** DocumentAtom returned `Unknown` for some Markdown files in the benchmark; the new `TextSniffer` fallback
ingests them as plain text, which throws away their headings and lists. The HTTP `Content-Type` header and the URL's
extension are ignored even though they are usually right. A type DocumentAtom detects but Pneuma cannot route throws
`NotSupportedException`, which is retried three times.

**Design.** Detection order: a declared type (inline content, P12 hints from uploads) → DocumentAtom → the response's
`Content-Type` and the file extension → `TextSniffer`. When the sniffer accepts text and the hints say Markdown
(`.md`, `.markdown`, `text/markdown`) or the text has Markdown structure (headings, lists, fences), it is routed to
DocumentAtom's Markdown extractor, not plain text. An unmapped type raises `IngestionHardFailException` with category
`UnsupportedType` (P10).

**Server / backend.** The fetcher returns the response content type; `IngestionContext` carries hints;
`TypeDetectionStage` logs which method decided ("DocumentAtom", "Content-Type", "extension", "sniffer").

**Dashboard.** The job log shows the detection method. **SDKs, MCP, Postman.** No change.

**Docs.** The detection order in `REST_API.md`.

**Tests.** `IngestionStagesSuite`.
- Positive: DocumentAtom `Unknown` plus `.md` routes to Markdown with headings preserved; a `text/markdown`
  content type does the same; a plain `.txt` stays text.
- Negative: an unmapped detected type fails once, without retries, as `UnsupportedType`; binary content with a
  misleading `.md` extension still fails; a wrong `Content-Type` does not override a confident DocumentAtom result.

### 21. P16: Ingestion observability: queue, retries, volume, processing log

**Why.** Operators cannot see how deep the queue is, how often jobs retry, or how much content and how many tokens a
job produced. AssistantHub writes a readable processing log per document (the resolved endpoints, the parameters used,
slot wait times, and on error the response body and a content excerpt) and a tenant-wide ingestion analytics page.
Pneuma's job events and `/jobs/summary` cover stages but not these.

**Design.**
- Metrics: `pneuma_ingestion_queue_depth{tenant}` (gauge), `pneuma_ingestion_job_attempts` (histogram),
  `pneuma_ingestion_bytes_total`, `pneuma_ingestion_cells_total`, `pneuma_ingestion_chunks_total{kind}`,
  `pneuma_ingestion_tokens_total{purpose=embed|classify|summarize}`, `pneuma_ingestion_job_duration_seconds`.
- Job fields: `BytesFetched`, `CellCount`, `ChunkCount`, `EmbeddedTokenCount`, `CompletionTokenCount`.
- A parameters event at job start recording the resolved settings (models and runners, chunk strategy, effective
  chunk size, overlap, header mode, enrichment switches, concurrency) so a job's result can be explained later.
- On failure, the event includes the upstream response body (truncated to 2 KB, secrets redacted) and a 500-character
  excerpt of the content being processed.
- `GET /v1.0/ingestion/analytics?hours=` returning throughput, failure rate by category, and p50/p95 duration by
  stage, per tenant.

**Server / backend.** Counters in `IngestionContext`, written by `IngestionJournal`; the analytics query aggregates
`ingestionjobs` and `ingestionjobevents`.

**Dashboard.** Admin: an "Ingestion analytics" view with the hand-rolled `ActivityChart` (throughput, failures by
category, stage durations) and a per-job volume column. The job log modal shows the parameters event as a table.

**SDKs, MCP.** `getIngestionAnalytics(hours)`; the new job fields. MCP `pneuma_ingestion_summary` gains the volume
totals.

**Postman.** Analytics request. **Docs.** `TELEMETRY.md` (every metric), `REST_API.md`.

**Tests.** `IngestionSuite` and `ApiSuite`.
- Positive: after a happy-path ingest the job's counts match the chunks in RecallDB; the queue gauge rises and falls;
  the analytics route returns the job.
- Negative: a failure event's response body has secrets redacted and is truncated; analytics with `hours` over the
  maximum is clamped; another tenant's jobs never appear.

### 22. S2: SDK parity for ingestion and sources

**Why.** The three SDKs lack re-ingest (single and bulk), `jobs/live`, `jobs/summary`, the ingestion settings, bulk
link and job deletion, job deletion, and link source download, all of which exist in REST. AssistantHub's SDKs cover
almost its whole ingestion surface. Every new item in this plan adds operations too.

**Design.** Add the missing existing operations now; add each new operation with its item. A parity check lists every
REST route from the OpenAPI document and fails when an SDK lacks a method for it (with an explicit exclusion list for
routes that are intentionally not in SDKs).

**Server / backend.** None. **Dashboard.** None.

**SDKs.** C#: `ReingestLinkAsync`, `ReingestLinksAsync`, `GetLiveJobsAsync`, `GetJobSummaryAsync`,
`GetIngestionSettingsAsync`, `UpdateIngestionSettingsAsync`, `DeleteLinksAsync`, `DeleteJobAsync`, `DeleteJobsAsync`,
`DownloadLinkSourceAsync`. The same in JavaScript (camelCase) and Python (snake_case). Each SDK README gets an
ingestion section.

**MCP.** Not applicable. **Postman.** Not applicable.

**Docs.** SDK READMEs and `README.md`'s SDK section.

**Tests.** A `SdkParitySuite` that loads `openapi.json` and each SDK's method list (reflection for C#, an exported
manifest for JS and Python) and compares. Each SDK's own test project gets positive and negative tests per method
against a loopback test server (404 for an unknown link, 403 for a read-only credential).

### 23. P14: Conditional GET and a stable content hash

**Why.** Refresh (A3) and crawling (A4) re-fetch pages, and today every fetch downloads the whole page. The delta
skip hashes the rendered HTML, which changes on every fetch for pages with timestamps, session tokens, or ads, so a
page that has not changed in substance is re-ingested anyway. AssistantHub also re-downloads everything and compares
ETags only after the download.

**Design.** Store `ETag` and `LastModified` on the link after each fetch and send `If-None-Match` and
`If-Modified-Since` next time; a 304 completes the job early without fetching. Compute a second hash over the
extracted cell text (after CellExtraction) and skip Classification and everything after it when the text hash
matches, even if the raw bytes differed. Keep the raw-byte hash for the early exit.

**Server / backend.** Link columns `http_etag`, `http_last_modified`, `text_hash`. `HttpContentFetcher` sends the
conditional headers; the Playwright fetcher first issues a lightweight conditional request and renders only on a
change. Job event "Unchanged (304)" or "Unchanged text".

**Dashboard.** The link detail shows the last change date separately from the last check date.

**SDKs, MCP, Postman.** New link fields only.

**Docs.** Re-ingest semantics in `REST_API.md`.

**Tests.** `IngestionStagesSuite`.
- Positive: a stub that answers 304 ends the job early without a body; a page whose HTML changed only in a timestamp
  outside the content ends after CellExtraction with no model calls; a real text change runs the full pipeline.
- Negative: a server that ignores conditional headers still works; a manual re-ingest with "force" skips both
  shortcuts.

### 24. A9: CIFS/SMB file-share source

**Why.** Departmental document shares on Windows file servers and NAS devices are where much organizational knowledge
still lives. AssistantHub supports them through Blobject.CIFS with metadata-only enumeration and detailed diagnostics
(settings, TCP 445, authentication, share root), and maps `localhost` to `host.docker.internal` inside containers.

**Design.** A `Cifs` source type. Settings: `Host`, `Share`, `Path`, `Domain`, `Username`, `Password` (secret),
`IncludeSubfolders`, and the A0 filter, plus AssistantHub's skip list (`.DS_Store`, `Thumbs.db`, `desktop.ini`,
`~$` lock files, `.tmp`, `.bak`, `.swp`). Enumeration is metadata-only (path, size, modified time as the version
token); `OpenAsync` streams the file.

**Server / backend.** `CifsSourceConnector` over Blobject.CIFS 5.1.0 (the same major version as Pneuma's Blobject.Core).
Connectivity layers: settings, DNS, TCP 445, SMB negotiate and authentication, share and path access. URIs
`smb://host/share/path`.

**Dashboard.** The A0 form.

**SDKs, MCP, Postman.** Generic operations; a CIFS example.

**Docs.** A `DATA_SOURCES.md` section, including Docker networking notes for shares on the host.

**Tests.** `CifsSourceSuite` (new), unit-level against a fake file-share provider behind the connector's
file-system seam, plus an integration suite against a Samba container gated by `PNEUMA_TEST_SMB`.
- Positive: nested folders enumerate; a modified file re-ingests; skip-list files are ignored.
- Negative: an unreachable host fails at the TCP layer with a clear message; bad credentials fail at the
  authentication layer; a missing share fails at the root layer; the password is never returned.

### 25. A20: Authenticated links (headers, cookies, basic, bearer)

**Why.** Much valuable content sits behind a login: internal wikis, vendor portals, private documentation. A link
cannot carry credentials today. AssistantHub's web crawler supports Basic, API-key header, and bearer
authentication.

**Design.** A tenant-scoped `FetchCredential` entity (`fcred_`): `Name`, `Type` (`Basic`, `Bearer`, `Header`,
`Cookie`), secret fields (encrypted, write-only), and `HostPatterns` (the credential is only ever sent to matching
hosts, so a redirect or crawled link cannot leak it elsewhere). Links, bulk submissions, and web sources reference a
credential by id. The Playwright fetcher sets the header or cookie through its context.

**Server / backend.** `/v1.0/fetch-credentials` CRUD (secrets write-only), a `CredentialId` on links and web source
settings, and the fetcher's host-pattern check. Audit on create, update, use failure, and delete.

**Dashboard.** Admin settings: a "Fetch credentials" view. Link submit and bulk-submit modals and the web source form:
a credential picker.

**SDKs.** Credential CRUD and a `credentialId` option on submit methods. **MCP.** `pneuma_submit_link` accepts a
credential id; credentials themselves are not manageable over MCP.

**Postman.** Credential CRUD and a link submit using one. **Docs.** `REST_API.md`, `DATA_SOURCES.md`.

**Tests.** `FetchCredentialSuite` (new).
- Positive: a Basic credential reaches a stub that requires it and the page ingests; a bearer credential works on the
  crawler; a cookie credential works with Playwright.
- Negative: a redirect to a host outside `HostPatterns` drops the credential; secrets never appear in responses or
  request history; a link cannot reference another tenant's credential (404); deleting a credential in use is refused
  (409) or detaches it with confirmation.

### 26. P13: Structure-aware chunking: merge small cells, keep headings, group table rows

**Why.** Chunks never cross cell boundaries, so documents made of many short cells (lists, FAQs, short paragraphs,
tables emitted one row per cell) produce many tiny chunks that embed poorly and crowd the result list. AssistantHub
offers row grouping with headers and context prefixes for tables; its flattening loses provenance, which Pneuma must
keep.

**Design.** A chunk-assembly step in ChunkingStage that packs consecutive small cells under the same heading into one
chunk up to the budget (`MergeSmallCells`, default true below `MinChunkTokens` = 64), while recording every source
cell id on the chunk (`CellNodeIds`, the first one in `litegraphNodeId` for compatibility). Table rows are grouped
`TableRowsPerChunk` (default 5) at a time with the header row repeated. Very long cells are split as today.

**Server / backend.** RecallDB tag `cellNodeIds` (comma-separated) and retrieval resolving all of them for neighbor
expansion and citations. Subject fields `MergeSmallCells`, `MinChunkTokens`, `TableRowsPerChunk`.

**Dashboard.** Subject chunking settings gain the three fields; the chunk viewer shows how many cells a chunk spans.

**SDKs, MCP, Postman.** Subject fields. **Docs.** Chunking section in `REST_API.md`.

**Tests.** `ChunkingSuite`.
- Positive: twenty one-line list cells become two or three chunks; each merged chunk lists its cells; a 20-row table
  becomes four chunks each starting with the header; merged chunks never cross a heading.
- Negative: `MergeSmallCells = false` keeps one chunk per cell; `TableRowsPerChunk` outside 1 to 100 is rejected.
- Benchmark: pneuma-live and Atlas retrieval before and after.

### 27. P15: Serialize entity merges on shared keys (RI-S)

**Why.** Entity resolution in `SubgraphMerger` finds a node by canonical name and creates it when missing, with no
lock, while up to eight jobs run at once. Two documents that mention the same entity at the same moment create two
nodes. Connectors increase the concurrency. Isis fixed the equivalent race with per-key async locks.

**Design.** A `KeyedAsyncLock` keyed by `tenantId/subjectId/nodeType/canonicalName` around find-or-create in
`MergeNodesAsync`, taken in sorted key order for a batch. For more than one server, a database-backed advisory lock
(Postgres `pg_advisory_xact_lock` on a hash of the key; an `entitylocks` table elsewhere). A periodic "merge
duplicates" maintenance job (`POST /v1.0/subjects/{id}/graph/dedupe`) repairs existing duplicates by merging edges
into the oldest node.

**Server / backend.** The lock, the advisory-lock provider, the dedupe job, and metric
`pneuma_graph_entity_lock_wait_seconds`.

**Dashboard.** Subject graph tools: a "Merge duplicate entities" action with a custom confirm modal and a result
count.

**SDKs.** `dedupeSubjectGraph`. **MCP.** Not exposed. **Postman.** Dedupe request.

**Docs.** `REST_API.md`.

**Tests.** `GraphMergeSuite` (new).
- Positive: twenty concurrent merges of the same entity create one node; the dedupe job merges two pre-existing
  duplicates and keeps all edges.
- Negative: different entity types with the same name stay separate; a lock timeout fails the stage as transient
  rather than creating a duplicate.

### 28. A12: Git repository source (GitHub, GitLab, any Git remote)

**Why.** Engineering knowledge lives in repositories: READMEs, `docs/` folders, design documents, ADRs, and code. A
Git source keeps a subject in step with a branch.

**Design.** A `Git` source type. Settings: `RemoteUrl`, `Branch` (default branch when blank), `Paths` (globs, default
`**/*.md`, `docs/**`), `Username` and `Token` (secret, for private repositories), `Depth` (shallow clone). Enumeration
clones or fetches into a work directory under the data path and walks the tree at the branch head; the version token
is the file's blob SHA, so only changed files re-ingest. Link URIs are the web URL of the file when the host is known
(GitHub, GitLab, Bitbucket, Azure DevOps patterns), so citations open the file in a browser; otherwise `git://`.

**Server / backend.** `GitSourceConnector` over LibGit2Sharp (native binaries per platform; verify in the Docker image)
or the `git` CLI in the image. Work directories are per source and removed with the source. The P5 address guard
applies to `RemoteUrl`.

**Dashboard.** The A0 form.

**SDKs, MCP, Postman.** Generic; a GitHub example.

**Docs.** A `DATA_SOURCES.md` section, including token scopes (read-only contents).

**Tests.** `GitSourceSuite` (new), against a local bare repository created by the test.
- Positive: files matching the globs are enumerated; a new commit that changes one file re-ingests only that file; a
  deleted file is removed when deletions are on; web URLs are built for known hosts.
- Negative: a bad token fails at authentication; a missing branch fails at the root layer; binary files outside the
  globs are ignored; a remote at a private address is blocked.

### 29. P11: Resume a failed job from the failed stage

**Why.** Every retry restarts from ContentRetrieval, so a job that failed at Embedding repeats the fetch,
extraction, classification (the most expensive LLM step), and summaries. Pneuma already stores each stage's output as
an artifact (source, atoms, subgraph, chunks, embeddings) in S3-compatible storage.

**Design.** Each stage records a checkpoint when its artifact is stored. A retry within the same job, and a new
`POST /v1.0/jobs/{id}/resume`, restart at the first stage without a checkpoint, loading earlier outputs from the
artifact store. Resuming is only offered when the link's content hash is unchanged and the subject's settings that
affect earlier stages have not changed since (a settings fingerprint on the job).

**Server / backend.** `ingestionjobcheckpoints` (job, stage, artifact key, fingerprint); loaders for each artifact
type in `IngestionContext`; the processor's retry path.

**Dashboard.** "Resume" beside "Restart" on failed jobs, with a tooltip naming the stage it would resume from.

**SDKs.** `resumeJob(jobId)`. **MCP.** `pneuma_resume_job`. **Postman.** Resume request.

**Docs.** `REST_API.md` (restart versus resume).

**Tests.** `IngestionStagesSuite`.
- Positive: a job that fails at Embedding resumes without calling the fetcher or the classifier; the final result
  equals an uninterrupted run.
- Negative: resume is refused (409) when the fingerprint changed or artifacts are missing (S3 disabled), and restart
  is suggested.

### 30. A15: Confluence source

**Why.** Confluence is the knowledge base for many engineering and product organizations.

**Design.** A `Confluence` source type for Cloud and Data Center. Settings: `BaseUrl`, `SpaceKeys`, `IncludeArchived`
(false), `IncludeAttachments` (true), `Labels` (only pages with these labels), and authentication (Cloud: email and API
token; Data Center: personal access token), as secrets. Enumeration uses the REST API with CQL
(`space in (…) and lastmodified > {cursor}`) so later runs fetch only changed pages; the version token is the page
version number. Page bodies are fetched in the `export_view` HTML representation and ingested as HTML; attachments
become their own links. Page hierarchy (ancestors) becomes the link title path, which P8 uses as a header.

**Server / backend.** `ConfluenceSourceConnector` over `HttpClient`, honoring `Retry-After`. Deleted pages are found by
comparing the full id list on a periodic full run (CQL does not return deletions).

**Dashboard.** The A0 form. **SDKs, MCP, Postman.** Generic; a Confluence example.

**Docs.** A `DATA_SOURCES.md` section.

**Tests.** `ConfluenceSourceSuite` (new), against a loopback stub of the content, CQL search, and attachment
endpoints.
- Positive: pages in the listed spaces are enumerated; a new page version re-ingests; attachments become links; the
  cursor limits the second run to changed pages.
- Negative: a bad token fails at authentication; a space key that does not exist fails the connectivity test;
  archived pages are skipped by default.

### 31. A14: Google Drive source

**Why.** Google Workspace organizations keep documents, sheets, and slides in Drive.

**Design.** A `GoogleDrive` source type using a service account (JSON key as a secret) with domain-wide delegation or a
shared drive the account can read. Settings: `DriveId` or `FolderId`, `IncludeSubfolders`, `IncludeSharedDrives`, and
the A0 filter. Enumeration uses the Changes API with a stored page token as the cursor. Google-native files are
exported (Docs to DOCX or HTML, Sheets to XLSX, Slides to PPTX); other files download as is. The version token is the
file's `version` or `md5Checksum`.

**Server / backend.** `GoogleDriveSourceConnector` over the Drive REST API (or the Google.Apis.Drive.v3 package),
exponential backoff on `userRateLimitExceeded`.

**Dashboard.** The A0 form, with a setup guide link for creating the service account.

**SDKs, MCP, Postman.** Generic; a Drive example.

**Docs.** A `DATA_SOURCES.md` section.

**Tests.** `GoogleDriveSourceSuite` (new), against a loopback stub of the token, files, changes, and export endpoints.
- Positive: files enumerate; a Google Doc is exported and ingested; the changes token limits the second run; a
  trashed file is removed when deletions are on.
- Negative: an invalid key fails at authentication; an export over Google's 10 MB export limit is recorded as a
  failed item with a clear error; files outside the folder are skipped.

### 32. P18: Unicode sanitizing before storage (RI-C)

**Why.** Extracted text, link titles, labels, and tags go to LiteGraph and RecallDB without any Unicode checks. A lone
surrogate (common in text extracted from damaged PDFs or produced by JavaScript string slicing) can make a record
unstorable or corrupt it downstream. Isis replaces invalid surrogates with U+FFFD before tokenizing or storing, and
tests it. TextChunker already keeps surrogate pairs whole (the `Emoji_SurrogatePairsStayWhole` test).

**Design.** A `TextSanitizer.ReplaceInvalidSurrogates` helper (and removal of NUL and other C0 control characters
except tab, newline, and carriage return) applied to cell text after extraction, to inline content (A2), and to link
titles, labels, and tag values on submission.

**Server / backend.** The helper in `Pneuma.Core/Helpers`, called by CellExtractionStage and the submission routes.
**Dashboard, SDKs, MCP, Postman.** None. **Docs.** `CHANGELOG.md` (Fixed).

**Tests.** `IngestionStagesSuite` and `ApiSuite`.
- Positive: a cell with a lone high surrogate is stored with U+FFFD; valid emoji are untouched.
- Negative: a title with a NUL character is cleaned, not rejected; a label made only of control characters is
  rejected (400) because nothing is left.

### 33. A6: RSS and Atom feed source

**Why.** Blogs, release notes, changelogs, advisories, and news are published as feeds. A feed source keeps a subject
current with new posts without crawling.

**Design.** A `Feed` source type. Settings: `FeedUrls`, `IngestEntryContent` (use the entry's embedded HTML when
present) or `FollowEntryLinks` (fetch the linked page), `MaxEntries` (500), `MaxAgeDays` (0 for no limit), and the
A0 filter. The version token is the entry's `updated` date or a hash of its content; the external key is the entry id
(`guid` or `id`), falling back to the link. Entries that drop out of a feed are **not** treated as deletions (feeds
are windows, not inventories), so this type forces `ProcessDeletions = false` unless `MaxAgeDays` is set, in which
case entries older than the limit are removed.

**Server / backend.** `FeedSourceConnector` using `System.ServiceModel.Syndication` or a small tolerant parser for RSS
2.0 and Atom 1.0, with DTD processing off and the P5 policy.

**Dashboard.** The A0 form. **SDKs, MCP, Postman.** Generic; a feed example.

**Docs.** A `DATA_SOURCES.md` section explaining why deletions behave differently.

**Tests.** `FeedSourceSuite` (new), against a stub server serving RSS and Atom documents.
- Positive: new entries are added; an entry whose `updated` changes is re-ingested; embedded content is used when
  configured; `MaxAgeDays` removes old entries.
- Negative: an entry missing from the next fetch is not deleted; a malformed feed fails the run and changes nothing;
  an XML external entity is not resolved.

### 34. P17: Cross-link duplicate detection (RI-U)

**Why.** The same document often arrives under several URLs (mirrors, print views, `http` and `https`, a PDF and its
HTML twin), and crawlers make that more common. Duplicates waste storage and push distinct results out of the top
ranks. Isis reports near-duplicates on write (`similarMemories`) and found that no similarity threshold cleanly
separates replacements from related-but-distinct records, so detection should be exact first and advisory beyond
that.

**Design.** Exact: after CellExtraction, the text hash (P14) is compared with other links in the subject; a match
completes the job as `Duplicate` with `DuplicateOfLinkId`, storing no chunks, and the link shows it. Near: after
Embedding, the first few chunks' vectors are searched in the subject's collection; links above
`NearDuplicateThreshold` (default 0.95) are recorded as advisory `SimilarLinks` without blocking. A subject setting
`DuplicatePolicy` (`Skip` default, `Keep`).

**Server / backend.** Link fields `DuplicateOfLinkId` and `SimilarLinkIds` (child table), a text-hash index per
subject, and handling in delete: deleting the original promotes a duplicate by re-ingesting it.

**Dashboard.** A "Duplicate of …" badge linking to the original, and a "Similar documents" section in the link
detail.

**SDKs, MCP.** Link fields; `pneuma_get_link` returns them. **Postman.** Examples. **Docs.** `REST_API.md`.

**Tests.** `IngestionSuite`.
- Positive: the same content at two URLs stores one set of chunks and marks the second link; deleting the original
  re-ingests the duplicate; near-identical content is flagged as similar but ingested.
- Negative: `DuplicatePolicy = Keep` ingests both; identical text in different subjects is not a duplicate.

### 35. P21: Event notifications (webhooks) for jobs and sync runs

**Why.** Integrations need to know when content is ready (to notify users, trigger evaluations, or chain workflows)
without polling. AssistantHub has no notifications, which its crawler design would have benefited from.

**Design.** A tenant-scoped `Webhook` entity (`whk_`): `Url`, `Events` (`job.completed`, `job.failed`,
`job.completed_with_warnings`, `sync.completed`, `sync.failed`, `sync.held`), `Secret` (write-only) used for an
HMAC-SHA256 signature header, `Enabled`. A delivery worker posts JSON payloads with retries (exponential, up to 24
hours) and records deliveries (`webhookdeliveries`: status, response code, attempts) with retention. The P5 address
guard applies to webhook URLs.

**Server / backend.** `/v1.0/webhooks` CRUD, `POST /v1.0/webhooks/{id}/test`, `GET /v1.0/webhooks/{id}/deliveries`,
`POST /v1.0/webhook-deliveries/{id}/redeliver`. Metric `pneuma_webhook_deliveries_total{outcome}`.

**Dashboard.** Admin: a "Webhooks" view with a form, a test button, and a deliveries table.

**SDKs.** Webhook CRUD, test, deliveries, redeliver, and a signature verification helper in each SDK.

**MCP.** Not exposed. **Postman.** A "Webhooks" folder.

**Docs.** A "Webhooks" section in `REST_API.md` with the payload schema and signature verification examples.

**Tests.** `WebhookSuite` (new), with a loopback receiver.
- Positive: a completed job delivers one signed payload whose signature verifies; a failing receiver is retried and
  then succeeds; redeliver sends again.
- Negative: a private-address URL is rejected unless allowed; a wrong secret fails verification in the SDK helper; a
  disabled webhook sends nothing; the secret never appears in responses.

### 36. T1: Archive expansion (ZIP, TAR, GZip)

**Why.** Document sets are often delivered as archives, and today an archive fails type detection.

**Design.** When type detection finds an archive, the job expands it (with limits: `MaxArchiveEntries` 1000,
`MaxExpandedBytes` 1 GB, a compression-ratio limit against zip bombs, no nested archives past depth 2, no absolute or
`..` paths) and creates one child link per supported entry (`ParentLinkId`, URI `pneuma-archive://{linkId}/{path}`,
bytes stored in the blob store). The archive link itself ends `Expanded` and stores no chunks. Deleting it deletes the
children; re-ingesting it replaces them with P1 semantics per entry path.

**Server / backend.** `System.IO.Compression` for ZIP and GZip, `System.Formats.Tar` for TAR. An `Expansion` stage
between TypeDetection and CellExtraction that short-circuits for archives.

**Dashboard.** The link list shows archives as expandable rows with their children.

**SDKs, MCP.** `parentLinkId` on links and a filter by parent. **Postman.** Upload a ZIP example.

**Docs.** Supported types list and limits.

**Tests.** `ArchiveSuite` (new).
- Positive: a ZIP of three documents creates three child links that ingest; re-ingesting a changed ZIP updates only
  the changed entry.
- Negative: a zip bomb is refused by the ratio limit; an entry with `../` is skipped; more than `MaxArchiveEntries`
  fails with `TooLarge`; an encrypted ZIP fails with a clear message.

### 37. A8: Azure Blob Storage source

**Why.** The Azure equivalent of A7, for organizations standardized on Azure.

**Design.** An `AzureBlob` source type. Settings: `AccountName`, `Container`, `Prefix`, `IncludeSubfolders`, and
authentication by account key, SAS token, or connection string (secrets), plus the A0 filter. Enumeration lists blobs
with ETags as version tokens; `OpenAsync` streams the blob.

**Server / backend.** `AzureBlobSourceConnector` over Blobject's Azure provider (or Azure.Storage.Blobs).
Connectivity layers: endpoint, authentication, container, list, read.

**Dashboard.** The A0 form. **SDKs, MCP, Postman.** Generic; an Azure example.

**Docs.** A `DATA_SOURCES.md` section with the minimal role (Storage Blob Data Reader).

**Tests.** `AzureBlobSourceSuite` (new), against the Azurite emulator container, gated by `PNEUMA_TEST_AZURITE`, plus
unit tests with a fake provider.
- Positive: blobs under a prefix enumerate; a changed ETag re-ingests; deletions are applied when enabled.
- Negative: an expired SAS fails at authentication; a missing container fails at the root layer; secrets are never
  returned.

### 38. S5: Ingestion benchmark gate in CI

**Why.** Several items here change what is stored (P1, P6, P7, P8, P9, P13, P17). The benchmark harness can measure
ingest fidelity (coverage, redundant chunks, failures, throughput) and retrieval quality, and Isis uses its
`compare` command as a regression gate. Pneuma's harness exists but nothing runs it automatically.

**Design.** A CI job on pull requests that touch `src/Pneuma.Core/Ingestion` or chunking: bring up the bench stack,
run `ingest` and `retrieval` on the small Atlas dataset in the lean profile with the stub model server (deterministic
and free) and a pinned local embedding model, and run `compare` against the stored baseline with thresholds (no
ingest failures, coverage at or above 0.98, redundant chunks at or below 1%, nDCG@10 no lower than the baseline minus
0.02). A nightly job runs the full set with real models and publishes the report.

**Server / backend.** None. **Dashboard.** None. **SDKs, MCP.** None.

**Postman.** None. **Docs.** `benchmarks/README.md` (the gate and how to update the baseline), `BENCHMARKING.md`.

**Tests.** The gate itself; plus a harness self-test that a deliberately broken chunker (duplicate chunks) fails it.

### 39. P19: Fair scheduling and priority across tenants and subjects

**Why.** The queue is global first-in, first-out. A tenant that submits a 10,000-page crawl delays every other
tenant's single link for hours. Connectors make large bursts routine.

**Design.** The claim picks the tenant with the oldest waiting job among tenants below their running-job share
(round-robin by tenant, then by subject within a tenant), and honors a job `Priority` (`High` for single interactive
submissions and uploads, `Normal` for bulk, `Low` for sources and refresh). Per-tenant limits `MaxRunningJobs` and
`MaxQueuedJobs` (a submission over the queued limit returns 429 with `Retry-After`).

**Server / backend.** Claim query changes per provider (a ranked subquery), job `Priority`, tenant limit fields, and
metric `pneuma_ingestion_queue_wait_seconds{priority}`.

**Dashboard.** The queue view groups by tenant and shows priority; tenant settings expose the limits.

**SDKs, MCP.** `priority` option on submit methods (restricted to `Normal` and `Low` for non-admins).

**Postman.** Examples. **Docs.** `REST_API.md`, `README.md`.

**Tests.** `IngestionQueueSuite`.
- Positive: with tenant A holding 100 queued jobs and tenant B one, B's job is claimed within the first two claims; a
  `High` job overtakes `Normal` jobs in the same tenant.
- Negative: a submission over `MaxQueuedJobs` returns 429; a non-admin cannot set `High`.

### 40. P20: Named ingestion profiles

**Why.** Chunking, enrichment, header, and duplicate settings are per subject, and every new subject re-enters them.
AssistantHub's `IngestionRule` is a named, reusable profile that uploads and crawlers reference. Once sources exist, a
source may need different settings than the subject default (for example, table-heavy spreadsheets from a share).

**Design.** An `IngestionProfile` entity (`iprof_`) holding the settings from P6, P7, P8, P9, P13, and P17 plus
chunk strategy, size, and overlap. A subject references a default profile; a source (A0) and an upload request may
reference another. Effective settings are resolved profile, then subject overrides, and recorded in the job's
parameters event (P16). A system profile set is seeded at first boot ("Search only", "Knowledge graph",
"Tables and spreadsheets").

**Server / backend.** `/v1.0/ingestion-profiles` CRUD, `ProfileId` on subjects and sources, and resolution in
`IngestionProcessor`. Seeding is idempotent.

**Dashboard.** Admin: an "Ingestion profiles" view with a form grouped like the subject settings; subject and source
forms get a profile picker.

**SDKs, MCP.** Profile CRUD in SDKs; `pneuma_enumerate_ingestion_profiles` in MCP (read-only).

**Postman.** A "Profiles" folder. **Docs.** `REST_API.md`, `README.md`.

**Tests.** `IngestionProfileSuite` (new).
- Positive: a subject using "Search only" ingests without classification; a source profile overrides the subject's
  chunk size; seeding twice creates no duplicates.
- Negative: deleting a profile in use is refused (409); a profile from another tenant cannot be referenced.

### 41. T2: Email files (EML, MSG, MBOX)

**Why.** Correspondence carries decisions and context, and exported mail is a common upload.

**Design.** Parse with MimeKit (EML, MBOX) and MsgReader (Outlook MSG) before DocumentAtom: each message becomes an
HTML or text document with a header block (from, to, date, subject) and its body; attachments become child links like
archive entries (T1). Message ids are external keys, so a re-uploaded mailbox does not duplicate messages. Headers
become tags (`from`, `date`, `subject`) for filtering.

**Server / backend.** An email extractor in the expansion stage from T1.

**Dashboard.** Email links show the subject line and sender. **SDKs, MCP, Postman.** None beyond upload.

**Docs.** Supported types.

**Tests.** `EmailFormatSuite` (new).
- Positive: an EML with a PDF attachment produces a message link and an attachment link; MBOX with 10 messages
  produces 10 links; tags carry the headers.
- Negative: a malformed MIME part is skipped with a warning (P3); a duplicate message id is not ingested twice.

### 42. A16: Notion source

**Why.** Many teams keep their wiki and project notes in Notion.

**Design.** A `Notion` source type using an internal integration token (secret) shared with the pages to ingest.
Settings: `RootPageIds` or `DatabaseIds`, `IncludeChildPages` (true). Enumeration walks the page tree through the
Notion API (search filtered by `last_edited_time` after the stored cursor); the version token is `last_edited_time`.
Page blocks are rendered to Markdown by the connector (Notion has no export endpoint in its public API) and ingested as
Markdown, so headings survive for P8.

**Server / backend.** `NotionSourceConnector` over `HttpClient`, handling Notion's 3 requests per second limit and
`Retry-After`, and paging of block children.

**Dashboard.** The A0 form, with a setup note on sharing pages with the integration.

**SDKs, MCP, Postman.** Generic; a Notion example.

**Docs.** A `DATA_SOURCES.md` section.

**Tests.** `NotionSourceSuite` (new), against a loopback stub of the search, page, and block-children endpoints.
- Positive: a page tree renders to Markdown with headings, lists, and code blocks; an edited page re-ingests.
- Negative: an unshared page returns 404 and is skipped with a warning; an invalid token fails at authentication;
  429 is retried after `Retry-After`.

### 43. T3: Audio and video transcription

**Why.** Recorded meetings, talks, training videos, and podcasts hold knowledge that is otherwise unsearchable.

**Design.** Audio and video types (MP3, WAV, M4A, MP4, WebM, MOV) are transcribed before extraction by a model runner
with a new `Transcription` capability (an OpenAI-compatible `/v1/audio/transcriptions` endpoint such as Whisper or
faster-whisper; through PolyPrompt if it supports transcription, otherwise directly). Video audio is extracted with
FFmpeg in the image. The transcript, with timestamps every paragraph, is ingested as text; chunks carry `startSeconds`
so citations can link to the moment. Size and duration limits (`MaxMediaMinutes`, default 180).

**Server / backend.** A `TranscriptionStage` (gated per stage like others), the capability on `ModelRunner`, FFmpeg in
the Docker image, and metric `pneuma_transcription_seconds_total`.

**Dashboard.** Model runner form: the capability. Subject settings: the transcription runner. Citations show a
timestamp.

**SDKs, MCP.** Runner capability and subject field. **Postman.** Upload an MP3 example.

**Docs.** Supported types, model setup.

**Tests.** `TranscriptionSuite` (new), with a stub transcription endpoint.
- Positive: an MP3 upload is transcribed and ingested; chunks carry timestamps.
- Negative: no transcription runner configured fails with `Configuration`; media over the duration limit fails with
  `TooLarge`; a corrupt file fails with `Extraction`.

### 44. S4: Documentation accuracy fixes

**Why.** Several documents disagree with the code, which misleads operators and agents.

**Design.** Fix, each in its file:
- `REST_API.md` lists ingestion stages as "TypeDetection → CellExtraction → Classification → GraphMerge → Embedding
  (chunking+embedding) → Indexing", omitting ContentRetrieval, OntologyCanonicalization, RelationshipConsolidation,
  Summarization, and Chunking.
- `MCP_API.md` lists `maxConcurrentTasks` as a subject concurrency override, but `SubjectConcurrencyOverrides` has no
  such field.
- `CLAUDE.md` says Watson 7.1; the packages are now Watson 7.2.
- `benchmarks/docker/compose.yaml` says Ollama runs on the host at `127.0.0.1:11434`; runs now use a configurable
  model gateway.
- `ClaimNextQueuedAsync` sets the initial stage to `TypeDetection` (a code fix, done in P2; the docs should describe
  `ContentRetrieval` as the first stage).

**Server / backend, Dashboard, SDKs, MCP, Postman.** None.

**Tests.** A documentation test that parses the stage list in `REST_API.md` and compares it with
`IngestionStageEnum`, and one that compares the override keys in `MCP_API.md` with `SubjectConcurrencyOverrides`.

### 45. S3: Postman parity

**Why.** The Postman collection lacks `GET /jobs/live`, `DELETE /links/{id}`, `DELETE /jobs/{id}`,
`POST /links/delete`, and `POST /jobs/delete`, and has few negative requests. AssistantHub's collection covers its
ingestion routes and includes negative cases for its chat-with-document flow.

**Design.** Add the missing requests with saved examples, and a negative request per folder. Add collection-level test
scripts that assert status codes, so the collection can run in Newman.

**Server / backend, Dashboard, SDKs, MCP.** None.

**Docs.** `README.md` mention of the collection and Newman.

**Tests.** A parity test (shared with S2) that compares collection request paths with the OpenAPI routes, and an
optional Newman run in CI against a test server.

### 46. A10: NFS file-share source

**Why.** Linux and Unix environments share documents over NFS. AssistantHub supports NFS v3 through Blobject.NFS.
Demand is lower than for SMB.

**Design.** An `Nfs` source type. Settings: `Host`, `Export`, `Path`, `Version` (`V3` default, `V4`), `UserId` and
`GroupId` (AUTH_SYS), `IncludeSubfolders`, and the A0 filter plus the skip list from A9. Metadata-only enumeration
with modified time and size as the version token.

**Server / backend.** `NfsSourceConnector` over Blobject.NFS 5.1.0. Connectivity layers: DNS, TCP 2049 (and the
portmapper for V3), mount, path.

**Dashboard.** The A0 form. **SDKs, MCP, Postman.** Generic; an NFS example.

**Docs.** A `DATA_SOURCES.md` section, including the need for the server to export to the Pneuma host.

**Tests.** `NfsSourceSuite` (new), unit tests with a fake provider plus an integration suite against an NFS server
container gated by `PNEUMA_TEST_NFS`.
- Positive: files enumerate; a modified file re-ingests.
- Negative: a denied mount fails at the mount layer with a clear message; a wrong export path fails at the path layer.

### 47. T5: Source-code awareness

**Why.** Git sources (A12) will bring code. Chunking code as prose splits functions in the middle and loses the file
and symbol context.

**Design.** Recognize code by extension (C#, JavaScript and TypeScript, Python, Go, Java, SQL, and others) and route it
past DocumentAtom to a code extractor that splits on top-level declarations (a lightweight per-language regex splitter
first; Tree-sitter later). Each cell carries the file path and symbol name, which P8 uses as the header. Chunk strategy
`Code` keeps a declaration whole when it fits and splits at blank lines otherwise.

**Server / backend.** A code extractor and strategy; a subject or profile switch to include or exclude code.

**Dashboard.** The chunk strategy picker gains "Code". **SDKs, MCP, Postman.** None.

**Docs.** Supported types and the Git source section.

**Tests.** `CodeChunkingSuite` (new).
- Positive: a C# file with three methods yields three cells named after the methods; the path is in the header.
- Negative: a minified JavaScript file falls back to fixed-size chunks; an unknown extension is treated as text.

### 48. A19: SQL database query source

**Why.** Some knowledge lives in databases: product catalogs, FAQ tables, ticket systems, CMS content.

**Design.** A `Sql` source type. Settings: `Provider` (Postgres, MySQL, SQL Server, SQLite), `ConnectionString`
(secret), `Query` (a read-only `SELECT`), `KeyColumn`, `TitleColumn`, `ContentColumns` (rendered as a Markdown
document per row, with column names as headings), `VersionColumn` (for example `updated_at`), and `TagColumns`. Each
row becomes a link keyed by `KeyColumn`. The connector runs the query in a read-only transaction with a timeout and a
row limit.

**Server / backend.** `SqlSourceConnector` reusing the provider drivers already referenced by Pneuma. The query is
validated to be a single `SELECT` (parsed, not string-matched), and the connection is opened read-only where the
provider supports it.

**Dashboard.** The A0 form, with a preview that shows the first rows as rendered documents.

**SDKs, MCP, Postman.** Generic; a Postgres example.

**Docs.** A `DATA_SOURCES.md` section recommending a read-only database user.

**Tests.** `SqlSourceSuite` (new), against SQLite files created by the test.
- Positive: rows become links with the key and title; a changed `updated_at` re-ingests; tag columns become tags.
- Negative: a query containing `DELETE` or two statements is rejected; a missing key column fails validation; the
  row limit is enforced; the connection string is never returned.

### 49. P22: Subject export and import bundle

**Why.** Operators need to move a subject between environments, back it up in a readable form, or seed a demo. Isis
designed an Open Knowledge Format bundle (Markdown with YAML front matter, a root index, a tolerant parser) that is
readable and diffable in Git, though its import route was never built.

**Design.** `POST /v1.0/subjects/{id}/export` produces a ZIP: `subject.json` (settings, without secrets), one Markdown
file per link (front matter with URL, title, labels, tags, content hash; body is the extracted cell text in order),
and optionally the graph as GraphML. `POST /v1.0/subjects/{id}/import` takes the ZIP and creates inline-content links
(A2) keyed by the original URL, so importing twice updates rather than duplicates. Export runs as a background job
with a download link.

**Server / backend.** An export job, a tolerant front matter parser, and blob storage for the ZIP with an expiry.

**Dashboard.** Subject settings: "Export" and "Import" actions with progress.

**SDKs.** `exportSubject`, `downloadExport`, `importSubject`. **MCP.** Not exposed. **Postman.** Export and import.

**Docs.** The bundle format.

**Tests.** `ExportImportSuite` (new).
- Positive: export then import into a new subject reproduces the links and searchable content; importing twice does
  not duplicate.
- Negative: a bundle with malformed front matter imports the valid files and reports the bad ones; a bundle over the
  size limit is rejected; secrets are absent from `subject.json`.

### 50. A18: IMAP mailbox source

**Why.** Shared mailboxes (support, sales, a project alias) accumulate answers worth searching.

**Design.** An `Imap` source type. Settings: `Host`, `Port`, `UseTls` (true), `Username`, `Password` or OAuth token
(secret), `Folders`, `SinceDate`, and `IncludeAttachments`. Enumeration uses IMAP `UIDVALIDITY` and `UID` as the
cursor and message id as the key; each message is handled by the T2 email extractor. Messages removed from the folder
are deletions only when explicitly enabled.

**Server / backend.** `ImapSourceConnector` over MailKit.

**Dashboard.** The A0 form. **SDKs, MCP, Postman.** Generic; an IMAP example.

**Docs.** A `DATA_SOURCES.md` section, including app passwords and OAuth for common providers.

**Tests.** `ImapSourceSuite` (new), against a GreenMail or similar IMAP test container gated by `PNEUMA_TEST_IMAP`,
plus unit tests with a fake folder.
- Positive: new messages are added; the UID cursor limits the next run; attachments become child links.
- Negative: a `UIDVALIDITY` change triggers a full re-enumeration; bad credentials fail at authentication; plaintext
  IMAP without TLS requires an explicit setting.

**Depends on.** T2.

### 51. A17: Slack source

**Why.** Team channels hold answers to recurring questions. Value is lower than document sources because chat is noisy
and short-lived, and the privacy considerations are larger.

**Design.** A `Slack` source type using a bot token (secret) with read scopes for chosen public channels. Settings:
`ChannelIds`, `SinceDate`, `IncludeThreads` (true), `MinThreadLength` (3 messages). Each thread (or each day of a
channel's unthreaded messages) becomes one Markdown document with authors and times; the cursor is the latest message
timestamp. Private channels, direct messages, and user profile data are out of scope.

**Server / backend.** `SlackSourceConnector` over the Web API (`conversations.history`, `conversations.replies`),
honoring Slack's tiered rate limits and `Retry-After`.

**Dashboard.** The A0 form, with a warning about what will become searchable.

**SDKs, MCP, Postman.** Generic; a Slack example.

**Docs.** A `DATA_SOURCES.md` section including the required scopes and privacy guidance.

**Tests.** `SlackSourceSuite` (new), against a loopback stub of the Web API.
- Positive: a thread becomes one document; a new reply re-ingests the thread; short threads are skipped.
- Negative: a channel the bot is not in fails with a clear message; an invalid token fails at authentication.

### 52. A11: Server-local directory source

**Why.** Single-server and air-gapped deployments sometimes place documents on a mounted volume. Value is low because
upload (A1) and file-share sources (A9, A10) cover most cases, and a local path is a security consideration.

**Design.** A `LocalDirectory` source type. Settings: `Path`, `IncludeSubfolders`, and the A0 filter. Paths must fall
under one of the system setting `AllowedLocalRoots` (empty by default, so the type is disabled until an administrator
configures a root). Enumeration uses file size and modified time.

**Server / backend.** `LocalDirectorySourceConnector` over Blobject's disk provider, with canonical-path checks that
defeat `..` and symbolic links out of the root.

**Dashboard.** The A0 form, hidden unless a root is configured.

**SDKs, MCP, Postman.** Generic.

**Docs.** A `DATA_SOURCES.md` section and the Docker volume example.

**Tests.** `LocalDirectorySourceSuite` (new), against a temporary directory.
- Positive: files enumerate; a modified file re-ingests.
- Negative: a path outside the allowed roots is rejected; a symbolic link that escapes the root is skipped; the type is
  unavailable when no roots are configured.

### 53. T4: EPUB and OpenDocument formats

**Why.** Books and manuals ship as EPUB, and some organizations use OpenDocument (ODT, ODS, ODP). Both fail type
detection today.

**Design.** EPUB is a ZIP of XHTML chapters: expand it through T1's machinery, keep the spine order, and ingest each
chapter as HTML with the book and chapter titles as the header. OpenDocument files are ZIPs of XML: convert
`content.xml` to HTML (ODT) or to CSV per sheet (ODS) in a small extractor, unless DocumentAtom adds native support
first, in which case map the routes.

**Server / backend.** Extractors in the expansion stage. **Dashboard, SDKs, MCP, Postman.** None beyond upload.

**Docs.** Supported types.

**Tests.** `DocumentFormatSuite`.
- Positive: an EPUB yields chapters in order with titles; an ODT's headings survive.
- Negative: a DRM-protected EPUB fails with a clear `UnsupportedType` message; a corrupt ODT fails with `Extraction`.

## Sequencing

The ranking says what is worth most. The order of work also depends on prerequisites. A practical sequence:

1. **Make re-ingest and the queue safe** (P1, P2, P3, P4, P5, P10, P18, S4). Every acquisition feature re-ingests and
   enqueues in volume, so these come first. They are mostly local changes.
2. **Open the simple ways in** (A1, A2, S1, P12, A3, P14). Upload, inline content, MCP writes, and scheduled refresh
   need no connector framework.
3. **Improve what is stored** (P6, P7, P8, P9, P13, P16), each measured with the benchmark (S5 automates it).
4. **Build the source framework** (A0), then the connectors in value order: A4 (web), A5 (sitemap), A7 (S3), A9
   (CIFS), A20 (authenticated fetch), A12 (Git), A6 (feeds), A8 (Azure), then the SaaS connectors A13, A15, A14, A16,
   and the rest.
5. **Scale and operations** (P11, P15, P17, P19, P20, P21, S2, S3) alongside step 4, as connector volume grows.
6. **New formats** (T1 early, since uploads bring archives; T2, T3, T4, T5 as demand appears) and P22.

## Peer defects to avoid

AssistantHub's crawler and upload code has correctness problems that a port would copy if they were not called out.
Each is addressed by an item above.

| AssistantHub behavior | Consequence | Avoided by |
|---|---|---|
| Upload and reprocess start `Task.Run` with no queue or cancellation | Documents stuck mid-pipeline after a restart; "Cancel" deletes the row while the task keeps writing vectors | P2 (durable queue); all ingestion goes through jobs |
| `ProcessUpdateAsync` looks up the existing document with `MaxResults = 1` and no filter | Crawler updates create duplicates instead of replacing | A0 keeps a `SourceItem` per key with its link id |
| `ProcessDeletionAsync` scans only the first 1000 documents | Deletions missed on larger tenants | A0 deletes by `SourceItem`, not by scanning |
| Crawler cleanup and reprocess never delete old vectors | Orphaned, still-searchable chunks | P1 |
| Enumeration baseline in JSON files on local disk | Lost with the container; unsafe with two servers | A0 stores `SourceItem` rows in the database |
| Web crawl holds every page body in memory | Memory grows with site size | A4 stores pages in the blob store as they are fetched |
| `StoreInS3 = false` silently ingests nothing | A setting that looks harmless disables a crawler | A0 has no such switch; every source ingests through links |
| Partial RecallDB batch failure still reports `Completed` | Silent data loss | P3 |
| Crawl credentials stored and returned in plaintext | Credential disclosure through the API and MCP | A0 and A20 store secrets encrypted and write-only |
| `IngestionRule.Atomization` exists but is never sent | Settings that do nothing | P20 profiles only contain settings the pipeline reads, with a test per field |
| DocumentAtom client with the default 100-second timeout; dashboard and server disagree on summarization timeout | Large or OCR-heavy files time out; confusing behavior | Existing per-stage deadlines; P16 records the resolved parameters |

Isis's weaknesses are mostly outside ingestion, but two apply: its batch upsert silently skips invalid items and stops
at the first error (A2's batch returns a result per item), and it writes the vector store before its own database row
without compensation (P1 retires old versions only after the new one is fully indexed).

## Checklist

Tick an item when its server, dashboard, SDK, MCP, Postman, documentation, and test work are all done.

| Done | ID | Item |
|:---:|---|---|
| ☐ | A1 | File upload |
| ☐ | P1 | Replace the previous version on re-ingest (RI-E) |
| ☐ | A4 | Web crawler |
| ☐ | A0 | Source and sync framework |
| ☐ | A2 | Inline content push API |
| ☐ | P3 | Completeness accounting |
| ☐ | P5 | Fetch safety |
| ☐ | P2 | Durable queue |
| ☐ | P4 | Shared retry policy and endpoint limiter |
| ☐ | P6 | Embedding model profiles (RI-Y) |
| ☐ | S1 | MCP ingestion write tools |
| ☐ | P8 | Contextual chunk header (RI-O) |
| ☐ | P9 | Enrichment switches |
| ☐ | P10 | Failure categories |
| ☐ | A3 | Scheduled link refresh |
| ☐ | A5 | Sitemap source |
| ☐ | P7 | Model-aware token budget (RI-F) |
| ☐ | A7 | S3 source |
| ☐ | A13 | SharePoint and OneDrive source |
| ☐ | P12 | Type-detection hints |
| ☐ | P16 | Ingestion observability |
| ☐ | S2 | SDK parity |
| ☐ | P14 | Conditional GET and text hash |
| ☐ | A9 | CIFS/SMB source |
| ☐ | A20 | Authenticated links |
| ☐ | P13 | Structure-aware chunking |
| ☐ | P15 | Entity merge serialization (RI-S) |
| ☐ | A12 | Git source |
| ☐ | P11 | Resume from failed stage |
| ☐ | A15 | Confluence source |
| ☐ | A14 | Google Drive source |
| ☐ | P18 | Unicode sanitizing (RI-C) |
| ☐ | A6 | RSS and Atom source |
| ☐ | P17 | Cross-link duplicates (RI-U) |
| ☐ | P21 | Webhooks |
| ☐ | T1 | Archive expansion |
| ☐ | A8 | Azure Blob source |
| ☐ | S5 | Benchmark gate in CI |
| ☐ | P19 | Fair scheduling |
| ☐ | P20 | Ingestion profiles |
| ☐ | T2 | Email files |
| ☐ | A16 | Notion source |
| ☐ | T3 | Audio and video transcription |
| ☐ | S4 | Documentation fixes |
| ☐ | S3 | Postman parity |
| ☐ | A10 | NFS source |
| ☐ | T5 | Source-code awareness |
| ☐ | A19 | SQL source |
| ☐ | P22 | Export and import |
| ☐ | A18 | IMAP source |
| ☐ | A17 | Slack source |
| ☐ | A11 | Local directory source |
| ☐ | T4 | EPUB and OpenDocument |
