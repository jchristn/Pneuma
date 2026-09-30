# Adding crawlers

Branch: `feature/crawlers`. Source plan: `archive/INGESTION_IMPROVEMENTS.md`. Requirements: `C:\Code\agents\requirements`.

Pneuma ingests one URL at a time. This plan adds the crawling infrastructure that AssistantHub has (scheduled
sources, a delta sync against the previous run, a web crawler, and CIFS and NFS file-share crawlers), plus the
sitemap and S3 sources and the pipeline fixes that make repeated, high-volume ingestion safe. It is the working
document for that effort: every task is a checkbox, and the progress log at the end records what was done, what was
verified, and what changed from the plan.

The work does not change the product version. `VERSIONING.md` forbids an agent from moving a version number without
explicit approval, so every change is recorded under `[Unreleased]` in `CHANGELOG.md` and the version stays as it is.
Nothing is committed or pushed without an explicit request.

## Scope

The user asked for the crawling infrastructure AssistantHub supports and for eleven items from the ranked list in
`INGESTION_IMPROVEMENTS.md`. AssistantHub's crawling infrastructure maps to four items in that list: the source and
sync framework (A0), the web crawler (A4), the CIFS/SMB crawler (A9), and the NFS crawler (A10). The eleven requested
ranks map to these items:

| Rank | ID | Item |
|---:|---|---|
| 2 | P1 | Replace the previous version on re-ingest; idempotent retries |
| 5 | A2 | Inline content push API |
| 6 | P3 | No silent partial loss: completeness accounting |
| 7 | P5 | Fetch safety: SSRF guard, size cap, TLS, politeness |
| 9 | P4 | Shared transient-retry policy and per-endpoint limiter for all model calls |
| 12 | P8 | Contextual chunk header on the embedding text |
| 14 | P10 | Structured failure categories, codes, and attempt history |
| 15 | A3 | Scheduled refresh of existing links |
| 16 | A5 | Sitemap source |
| 17 | P7 | Model-aware token budget and re-chunk on context-length errors |
| 18 | A7 | Amazon S3 and S3-compatible bucket source |

Everything else in `INGESTION_IMPROVEMENTS.md` stays out of this branch. It is listed in
[All items from INGESTION_IMPROVEMENTS.md](#all-items-from-ingestion_improvementsmd) with its status, so this document
accounts for all 53 items and nothing falls through between the two plans.

## Status legend

- `[ ]` not started
- `[~]` in progress
- `[x]` done and verified (the verification is named in the progress log)
- `[-]` dropped or deferred, with the reason beside it

## Design decisions

These decisions apply across phases. Each one records the choice and the reason, so a reviewer can disagree with a
specific decision rather than with the whole plan.

**D1. Crawl plan settings are typed DTOs stored in a key/value child table.** Each crawler type has its own settings
class (`WebCrawlSettings`, `SitemapCrawlSettings`, `S3CrawlSettings`, `CifsCrawlSettings`, `NfsCrawlSettings`). They
are persisted as rows in `crawlplansettings` (`planid`, `name`, `ordinal`, `value`), one row per scalar and one row
per list element. The alternative, one table per type, multiplies DDL across four providers for every new connector.
A JSON column would break the rule against JSON for known shapes. The key/value table keeps columns structured and
lets a new connector ship without a migration. Secrets never go in this table.

**D2. Secrets are write-only and encrypted.** Passwords, access keys, and tokens live in `crawlplansecrets`
(`planid`, `name`, `ciphertext`) encrypted with the existing `Aes256Cipher`. REST and MCP return `hasSecret: true`
instead of the value, and request capture redacts them. AssistantHub stores these in plaintext and returns them from
GET; that is the defect this avoids.

**D3. Everything becomes a link.** Pushed content, crawled pages, and files from shares and buckets all become
`SubjectLink` rows with a `SourceKind` (`Url`, `Inline`, `Crawl`) and, for crawled content, a `CrawlPlanId` and
`ExternalKey`. The pipeline, delete cascade, job views, retrieval filters, and SDKs keep working without special cases.

**D4. Content is resolved by kind, not fetched by URL.** A new `IContentResolver` replaces the direct
`IContentFetcher` call in `ContentRetrievalStage`. HTTP links go through the fetch-safety policy (P5) and the existing
fetchers. Inline content comes from the blob store. Crawled links are opened through their crawler
(`ICrawler.OpenAsync`). The resolver returns bytes plus the content type, ETag, and Last-Modified.

**D5. The delta baseline lives in the database.** AssistantHub keeps the previous enumeration in JSON files on local
disk, which is lost with the container and unsafe with two servers. Pneuma keeps one `crawlobjects` row per object
ever seen, with its version token and link id.

**D6. Libraries.** CrawlSharp 1.1.0 for the web crawler (1.0.22 at first; 1.1.0 fixed the problems Phase 8 found). Blobject.AmazonS3 6.0.0 for
buckets (Blobject 5.1.0 at first; all Blobject packages moved to 6.0.0 on 2026-09-29). Blobject.CIFS and Blobject.NFS 6.0.0 for CIFS and NFS. During Phase 8
the crawlers called OpenCIFS.Client and OpenNFS.Client 0.1.1 directly, because Blobject.CIFS 5.x could only reach port
445, which Windows' own SMB server holds on a development machine. Blobject 6.0 rebuilt CIFS and NFS on those same
clients with port settings, so the crawlers moved back to Blobject on 2026-09-29 (decided with the user) and every
bucket and share crawler now shares `BlobCrawlerBase`. GitHubCrawler 1.0.1 for GitHub repositories, and Blobject.AzureBlob, Blobject.GoogleCloud, and
Blobject.Disk 6.0.0 for Azure Blob, Google Cloud Storage, and local folders (added at the user's request during
Phase 8). Cronos 0.13.0 for cron schedules. Sitemaps are parsed with `System.Xml` directly.

**D7. Deletions are opt-in.** A crawl plan never deletes links unless `ProcessDeletions` is on, and a run that would delete
more than `MaxDeletionFraction` (default 0.2) of the plan's links is held for confirmation.

**D8. Chunk headers apply to new ingests.** `ChunkHeaders` defaults to `TitleAndHeadings` for subjects created after the
migration and `None` for existing subjects, so existing collections are not silently mixed. Re-embedding existing
subjects is P6, which is out of scope.

**D9. Names follow `AUTHENTICATION.md`.** The requirements already name `CrawlPlan` and `CrawlOperation` as
resource types and ask products to reuse them, and AssistantHub uses the same words. `INGESTION_IMPROVEMENTS.md` says
"data source" and "sync run"; on this branch those are a crawl plan and a crawl operation, and each object a plan
has seen is a crawl object. Routes use Pneuma's kebab-case style (`/v1.0/crawl-plans`).

**D10. Retries move to one handler.** The string-matching `EmbedWithRetryAsync` in `NativeSemanticProcessor` is replaced by
a `DelegatingHandler` on the shared model HTTP handler, so embeddings, completions, summaries, classification, and
reranking all retry the same way.

**D11. Warnings do not change the status.** The plan called for `CompletedWithWarnings` and `IngestedWithWarnings`.
Every consumer (dashboards, SDK status filters, the activity charts, existing integrations) treats `Completed` and
`Ingested` as success, and a new terminal status would have made each of them misread a mostly successful ingest.
Instead the status stays the same, the job carries `warnings` and `completeness`, the link carries `warningCount`, and
`hasWarnings` filters for them.

## Requirement compliance

The requirement documents govern every task below, and a few of their rules shape this work more than others. Code
follows `CODE_STYLE.md` and `BACKEND_ARCHITECTURE.md` (and never uses em-dashes, including in Postman and commit
text). New tables exist in all four providers with versioned migrations. Every route carries OpenAPI metadata and a
static resource and operation mapping, and every denial and bypass is audited. Secrets are encrypted, never returned,
and redacted from request capture. New screens use the shared `DataTable`, row action menus, purpose-built modals,
`ConfirmModal` for destructive actions, loading, empty, and error states, and i18n keys in every locale catalog. New
metrics use the `pneuma_` prefix with low-cardinality labels and get Grafana panels. Tests are Touchstone descriptors
with positive and negative cases, run by all three runners. Feature completion is followed by a simulated user
session recorded under `user-testing/`. Versions do not change.

## Phases

Each phase ends with a build and the full test run. A phase is done only when its server, database (all four
providers), dashboard, SDK, MCP, Postman, documentation, and test tasks are all checked.

### Phase 0: Branch and plan

- [x] Create and switch to `feature/crawlers`.
- [x] Move `PARTIO_REMOVAL_PLAN.md` and `CKG_IMPROVEMENTS.md` to `archive/`.
- [x] Remove em-dashes from `INGESTION_IMPROVEMENTS.md` headings (requirement in `CODE_STYLE.md` and `WRITING_DOCUMENTS.md`).
- [x] Write this plan.
- [x] Baseline: `dotnet build src/Pneuma.sln` with zero warnings and a full `Test.Automated` run, recorded in the log.

### Phase 1: Failure categories and completeness (P10, P3)

P10 comes first because P3, P4, P5, and the crawlers all report failures through it.

- [x] `IngestionFailureCategoryEnum` (`Fetch`, `Blocked`, `TooLarge`, `UnsupportedType`, `Extraction`, `NoContent`,
  `ModelUnavailable`, `ModelRejected`, `Configuration`, `Storage`, `Timeout`, `PartialLoss`, `WorkerLost`,
  `Cancelled`, `Internal`) with retry policy and remediation text per category.
- [x] `IngestionFailureClassifier` mapping exceptions to categories; the processor asks it whether to retry, and
  `ModelUnavailable` backs off four times longer.
- [x] `IngestionHardFailException` carries a category; unmapped types become `UnsupportedType` and are not retried.
- [x] Typed exceptions the classifier recognizes: `FetchBlockedException`, `ContentTooLargeException`,
  `ModelEndpointUnavailableException`, `ModelRequestRejectedException`, `PartialLossException`; `ModelResponseErrors`
  turns an unsuccessful model response into the right one.
- [x] Migration 28: `failurecategory`, `warningsjson`, and ten completeness columns on `ingestionjobs`;
  `failurecategory` and `warningcount` on `subjectlinks`; `ingestionjobattempts` table. All four providers, with
  cascade deletes on job, link, subject, and tenant deletion.
- [x] `GET /v1.0/jobs?failureCategory=&hasWarnings=` filters (400 on unknown values); job detail and the job and link
  logs return `{ job, events, attempts, remediation }` through one `IngestionJobDetailBuilder`.
- [x] P3: `IngestionCompleteness` counters filled by every stage and persisted on the job.
- [-] P3: `CompletedWithWarnings` job status and `IngestedWithWarnings` link status. Replaced by D11: the status stays
  `Completed`/`Ingested` and the job carries `warnings` (the link `warningCount`).
- [x] P3: `PartialLossPolicy` (`Warn`, `Fail`) in `IngestionSettings`. Not in `IngestionTuning` (a restart applies it).
- [x] P3: chunks without an embedding fail the attempt instead of being dropped.
- [x] P3: a failed classification call (including unparseable output) is a counted, warned batch failure instead of a
  silent empty graph, for single-call documents as well as batched ones.
- [x] Metrics: `pneuma_ingestion_failures_total{category}`, `pneuma_ingestion_partial_total{stage,reason}`.
- [x] Dashboards: admin jobs view category and warnings filters and badges; `JobDiagnostics` (category with
  remediation, warnings, completeness counters, attempts table) in the admin link log and follow-logs modals and in the
  subject dashboard's ingestion detail and link log; subject ingestion view filters and badges; i18n keys with plural
  forms.
- [x] SDKs (C#, JS, Python): `failureCategory`/`hasWarnings` filters, `FailureCategory`, `Warnings`, `Completeness`,
  `Attempts`, and `Remediation` models.
- [ ] SDK harness cases for the new filters (batched with Phase 3 and run against a local server).
- [x] MCP: `pneuma_enumerate_jobs` accepts `status` (previously documented but ignored), `failureCategory`, and
  `hasWarnings`, and each summary carries `failureCategory` and `warningCount`; `pneuma_get_job` returns the job with
  its warnings and counters. Attempts are available through REST job detail.
- [x] Postman: filter requests, a with-warnings request, a 400 negative, and updated job detail description.
- [x] Docs: failure-category table, completeness and warnings in `REST_API.md`; corrected stage list (part of S4);
  `MCP_API.md` tools and the removed `maxConcurrentTasks` override key; `TELEMETRY.md`; `CHANGELOG.md`.
- [x] Tests, positive: each category from its trigger; typed model errors from error text; clean run records matching
  counts with one successful attempt; classification failure completes with one warning on job and link.
- [x] Tests, negative: unsupported type fails once with its category; a 404 is not retried; a 503 is retried to
  `MaxAttempts` with each attempt recorded; `Fail` policy fails with `PartialLoss` after retrying; a missing vector
  fails the job with nothing indexed; unknown `failureCategory` and invalid `hasWarnings` return 400.

### Phase 2: Fetch safety (P5)

- [x] `FetchSafetyPolicy`: scheme allow-list, address guard (loopback, link-local, RFC 1918, carrier-grade NAT,
  unique-local IPv6, multicast, metadata), checked in the handler's `ConnectCallback` so every connection, including
  redirects, is validated against the address it actually connects to (DNS rebinding).
- [x] `Ingestion.FetchSafety.AllowedPrivateHosts` (host names, `*.` wildcards, IP addresses, CIDR ranges) and
  `BlockPrivateAddresses` (default true).
- [-] Per-tenant allow-list. Deferred: tenants have no settings surface yet, and an allow-list is a system
  administrator decision about the server's network position.
- [x] `MaxDownloadBytes` (default 100 MB) enforced while streaming (declared Content-Length refused before reading).
- [x] Certificate validation on by default (the browser previously ignored certificate errors);
  `AllowInvalidCertificates` logs a startup warning when on (as does turning `BlockPrivateAddresses` off).
- [x] `HostRequestLimiter` (`MaxRequestsPerHost`, default 2) shared by the HTTP and browser fetchers through the one
  policy instance the bootstrapper creates; crawlers will use the same instance.
- [x] Playwright: the page URL is resolved and checked before navigation; every request the page makes is routed
  through the policy and aborted when refused; `data:`, `blob:`, and `about:` stay local; refusals are not retried
  through the HTTP fallback.
- [x] Link submission (single and bulk) rejects other schemes, relative URLs, private IP literals, and `localhost` with
  400 and a `FetchBlocked` audit record; one unsafe URL refuses a whole bulk batch. Host names are resolved at fetch
  time, so submission stays fast.
- [x] Dashboard: settings tips for every fetch-safety field; blocked jobs and links show category `Blocked` with its
  remediation (Phase 1 components).
- [x] Config: `fetchSafety` blocks in `docker/pneuma.json`, `docker/factory/pneuma.json`, and the benchmark config (which
  allow-lists `127.0.0.1` for the corpus server).
- [x] Postman negatives (private address, unsupported scheme); docs: `README.md` "Fetch safety", `REST_API.md`
  submission rules, `CHANGELOG.md` Security.
- [x] Tests, positive: public address passes; allow-listed loopback, wildcard host, and CIDR range pass; blocking off
  allows private addresses; an allow-listed fetch returns its bytes; the per-host limiter releases waiting callers.
- [x] Tests, negative: every blocked range; other schemes and relative URLs rejected; a host resolving to a private
  address blocked; DNS rebinding blocked at connect with the site never hit; a redirect to a private host blocked with
  the target never hit; oversize response abandoned (`TooLarge`); a third request to one host waits; unsafe submissions
  return 400 and are audited.
- [ ] Invalid-certificate test. Needs a TLS stub server; deferred to Phase 10.

### Phase 3: Shared retry and endpoint limiter (P4)

- [x] `TransientRetryHandler` (408, 429, 502, 503, 504, and a 500 whose body names one of them or says the endpoint is
  at capacity or overloaded; `Retry-After` seconds or date, capped at 30 s; otherwise full jitter from 1 s doubling to
  30 s; never 400, 401, 403, 404, 422, or network errors) wrapping the shared handler in `ModelClientFactory`, so every
  PolyPrompt call retries the same way. The request body is buffered and the request cloned per attempt.
- [x] `ModelRunner.MaxRetries` (default 5, clamped 0 to 10): migration 29 in all four providers, create/update request,
  response DTO, and route mapping. `MaxConcurrentRequests` already existed but was not applied to ingestion.
- [x] `EndpointConcurrencyLimiter` (process-wide, one gate per runner id, sized by `MaxConcurrentRequests`) held around
  each attempt, so ingestion and query traffic share the limit and waits between retries do not hold a slot.
- [x] Typed outcomes: exhausted retries surface as `ModelEndpointUnavailableException` (job category `ModelUnavailable`,
  API 503 with `Retry-After: 30`); a rejected request as `ModelRequestRejectedException` (category `ModelRejected`,
  API 502). An explicit 4xx status decides before any wording in the body.
- [x] Removed the string-matching `EmbedWithRetryAsync`; `SummarizeAsync` now throws on a failed call instead of
  returning an empty summary (another silent loss).
- [x] Metrics `pneuma_model_retries_total{runner,status}`, `pneuma_model_limiter_wait_seconds{runner}`.
- [x] Dashboard: "Max Retries" on the model runner form with a tip; the concurrency tip now says it is shared by
  ingestion and chat and applies after a restart.
- [-] Retry counts on the model runner health view. Deferred: the counts live in the process metrics registry and are
  exposed through Prometheus and Grafana; the health API reads persisted check results.
- [x] SDKs: `MaxConcurrentRequests` and `MaxRetries` on the C# `ModelRunner` model (JS and Python pass runner objects
  through). Harness cases batched with Phase 1's.
- [x] Postman: a runner create example with `maxRetries` and `maxConcurrentRequests`.
- [x] Docs: `README.md` "Shared model endpoints", `REST_API.md` runner fields and 502/503 behavior, `TELEMETRY.md`,
  `CHANGELOG.md` (Changed).
- [x] Tests, positive: an embedding succeeds after two 429s in three requests; a summary succeeds after a wrapped 500;
  `Retry-After: 2` waits at least two seconds; six concurrent calls never exceed `MaxConcurrentRequests` 2 at the stub.
- [x] Tests, negative: a 400 is not retried and surfaces as a rejection; retries stop at `MaxRetries` with
  `ModelEndpointUnavailableException`; cancellation ends a 30 s `Retry-After` wait promptly; only 408, 429, 502, 503,
  504 are transient; `MaxRetries` clamps.

### Phase 4: Version replacement (P1)

- [x] `VersionRetirementService.RetireOlderVersionsAsync(job)` after Indexing: removes every other job's RecallDB chunks
  (delete by `jobId` tag) and Source and Cell nodes (found by `assertedByJob` and node type, deleted in batches). Jobs
  still queued or running are skipped.
- [-] Removing entity nodes and edges asserted by the retired job. Dropped: `IGraphRepository.DeleteByJobAsync` deletes
  every node the job created, including entity nodes a later job reused through find-by-canonical-name, which would
  break the new version. Entity nodes are kept; an entity only an old version mentioned can linger (noted for P15).
- [x] Before each retry attempt, `RemoveJobOutputAsync` clears the failed attempt's chunks and Source and Cell nodes.
- [x] `SubjectLink.CurrentJobId` (migration 30, all providers), set when a job writes a version; an early-completed
  (unchanged) job leaves it alone.
- [x] Job event "Replaced the previous version: ..."; metric `pneuma_ingestion_retired_total{kind}`.
- [x] Retirement failure completes the job with a warning and leaves both versions; the next success removes both.
- [x] Dashboards: the link log marks the run holding the live version "Current version" and older completed runs
  "Superseded", with tips (admin and subject dashboards, i18n).
- [x] SDKs: `CurrentJobId` on the C# `SubjectLink` (JS and Python pass it through). MCP `pneuma_get_link` returns the
  link record, which now carries `currentJobId`.
- [x] Postman: the "Reingest Link" request documents the semantics and has test scripts for 202 and a `job_` id.
- [x] Docs: "One live version per link" in `REST_API.md`, the re-ingest row, `TELEMETRY.md`, `CHANGELOG.md` (Fixed).
- [x] Tests, positive: re-ingesting changed content leaves only the new job's chunks and nodes and moves
  `CurrentJobId`, with a "Replaced the previous version" event; the shared entity node survives and is reused; an
  unchanged re-ingest keeps the live version; a retry after an Embedding failure leaves exactly one Source node.
- [x] Tests, negative: a failed re-ingest keeps the old version and `CurrentJobId`; a chunk-removal failure warns, keeps
  both versions, and the next ingest removes both; another link's identical content is untouched.

### Phase 5: Token budget and chunk headers (P7, P8)

- [x] P7: chunk budget from TextChunker 0.3.1, which already resolves the model's input limit and the `[CLS]`/`[SEP]`
  reserve from the model id; Pneuma now adds a 1% margin (minimum 2 tokens) through `SafetyMarginPercentage` and
  `SafetyMarginTokens`, and `ModelRunner.MaxInputTokens` (0 uses the known limit) through `EffectiveInputBudget`.
- [x] P7: a context-length rejection of a batch falls back to one-at-a-time embedding; each rejected chunk alone is
  re-chunked at 0.75, 0.5, then 0.3 of the size with its header; one rejected even at 0.3 is left out with a warning
  (a deterministic loss that a retry cannot fix, so it is a warning rather than a failed attempt).
- [x] P7: the effective chunking settings are recorded in the Chunking event; metric `pneuma_ingestion_rechunk_total{scale}`.
- [x] P8: DocumentAtom heading atoms (`HeaderLevel`) build a heading path carried on every cell (`HeadingPath`,
  `HeaderLevel`).
- [x] P8: `SemanticChunk.EmbeddingText` = header plus chunk, passed to TextChunker as `ContextPrefix` so its tokens
  come out of the budget; the stored text is taken from the chunk's source offsets and never includes the header. The
  header is cut at a word boundary to about 25% of the chunk size. Embedding and the embedding cache use the embedded
  text. Summary chunks get the title only.
- [x] P8: `Subject.ChunkHeaders` (`None`, `Title`, `TitleAndHeadings`): migration 31 defaults existing subjects to
  `None`, new subjects to `TitleAndHeadings` (decision D8). The title is the link title, else the first top-level heading.
- [x] Dashboards: runner "Max Input Tokens"; subject "Chunk Headers" in the admin and subject dashboards; Spanish keys
  added to the subject dashboard's `es` catalog for everything this branch added so far.
- [-] Chunk viewer showing the header. Dropped: the header exists only at embedding time (not stored in RecallDB or
  the chunk artifact), and storing it would duplicate text in every chunk.
- [x] SDKs: C# `Subject` gains the chunk settings (they were missing) and `ChunkHeaders`; `ModelRunner.MaxInputTokens`.
  MCP subject tools do not expose chunk settings today, so none were added.
- [x] Postman runner example with `maxInputTokens`; docs: "Chunking" in `REST_API.md`, runner fields, `TELEMETRY.md`,
  `CHANGELOG.md`.
- [x] Tests, positive: heading paths from nested heading atoms (a same-level heading replaces deeper ones); the header
  is in the embedded text of every chunk and never in the stored text; with a 100-token limit, header plus chunk stays
  within 100 WordPiece tokens; header modes and word-boundary truncation; a chunk the stub rejects as too long is split
  and the job completes.
- [x] Tests, negative: no header means no separate embedded text; a chunk rejected even at 30% is left out with a
  warning; a non-context 400 fails the job as `ModelRejected` after one attempt, without re-chunking.
- [ ] Benchmark: Meridian and pneuma-live before and after P8. Pending: the benchmark stack is stopped (another
  benchmark is using the machine); run with the restarted `round1` benchmarks.

### Phase 6: Content resolver and inline content (A2)

- [x] `IContentResolver` / `ContentResolver` per decision D4: `Url` links through the fetcher (and the fetch-safety
  policy), `Inline` links from the blob store with their declared type, `Crawl` links through an `ICrawlContentSource`
  (set in Phase 7). `ContentRetrievalStage` uses it; `IngestionProcessor.Resolver` exposes it for wiring.
- [x] `SubjectLink.SourceKind`, `ExternalKey`, `ContentType`, `SizeBytes`, `CrawlPlanId` (migration 32, all providers,
  including insert); unique index on `(tenantid, subjectid, externalkey)` (filtered to non-null keys on SQL Server);
  `ReadByExternalKeyAsync` and `EnumerateByCrawlPlanAsync`.
- [x] `POST /v1.0/subjects/{id}/content` and `/content/batch` (at most 100, a result per item, invalid items never
  stop the rest); `externalKey` upsert replaces content and queues a new job (P1 retires the old version);
  `MaxInlineContentBytes` (10 MB, 413); a declared content type skips type detection. The logic lives in
  `ContentSubmissionService`, shared by REST and MCP.
- [x] Unicode sanitizing (`TextSanitizer`, the P18 helper) for pushed content, titles, labels, and tags.
- [x] Link deletion removes the pushed content from the blob store.
- [x] Subject dashboard "Add Text" dialog (subject, title, format, content with Markdown preview, key, labels and
  tags) with English and Spanish strings.
- [-] Admin dashboard inline-link rendering. Deferred: inline links show their `pneuma-inline://` URI and work with
  every existing action; a dedicated rendering can follow the crawl-plan column in Phase 7.
- [x] SDKs `submitContent`/`submitContentBatch` (C#, JS, Python) and the new link fields on the C# model; MCP
  `pneuma_submit_content` (Subject/Write) with agent guidance in its description; Postman "Inline content" folder;
  docs (`REST_API.md`, `MCP_API.md`, `CHANGELOG.md`).
- [x] Tests, positive: Markdown creates an inline link with its content type and a job; the same key twice leaves one
  link and reports `replaced`; a batch reports each item; pushed Markdown ingests with the declared type while a
  detector that answers Unknown is never consulted; the MCP tool creates content.
- [x] Tests, negative: empty content, unsupported type, over-long key, a 101-item batch, an unknown subject (404), no
  token (401), content over the limit (413), a lone surrogate stored as U+FFFD with NULs removed, the same key on
  another subject creating a separate link, the MCP tool refusing an unknown subject.

### Phase 7: Crawl plan and sync framework (A0)

- [x] Constants and `IdGenerator`: `cpl_` (crawl plan), `cop_` (crawl operation), `cob_` (crawl object), `coo_`
  (operation object).
- [x] Models: `CrawlPlan`, `CrawlPlanTypeEnum`, `CrawlPlanStatusEnum`, `CrawlFilter`, `CrawlSchedule`,
  `CrawlScheduleTypeEnum`, `CrawlObject`, `CrawlObjectStatusEnum`, `CrawlOperation`, `CrawlOperationStatusEnum`,
  `CrawlTriggerEnum`, `CrawlOperationObject`, `CrawlActionEnum` (plus `Unchanged` and `Missing`, counted only),
  `ConnectivityResult`, `ConnectivityLayer`, `CrawledObject` (the plan's `SourceObject`), and the settings DTOs.
  `CrawlSettingAttribute` on each settings property drives the form schema, validation, and storage
  (`CrawlSettingsCodec`, decision D1).
- [x] Migration 33: `crawlplans` (with `claimtoken` and `claimexpiresutc`), `crawlplansettings`, `crawlplansecrets`,
  `crawlobjects`, `crawloperations`, `crawloperationobjects`, with tenant-leading indexes. All four providers.
  Reserved words are avoided (`plantype`, `triggeredby`, `crawlaction`, `settingvalue`).
- [x] `ICrawlPlanMethods`, `ICrawlObjectMethods`, `ICrawlOperationMethods` with implementations in all four providers
  (Postgres written, cloned to the others). Batch writes run in transactions of 200 statements.
- [x] `ICrawler` contract and `CrawlerFactory`; the type catalog (with each type's settings schema) lists registered
  crawlers only, so a type appears when its connector ships (Phase 8).
- [x] `CrawlSyncService`: enumerate, delta against crawl objects (`CrawlDeltaPlanner`, pure, shared with preview),
  create or re-ingest links, delete when enabled, retry last run's failures, hold over `MaxDeletionFraction`, finish
  the run when its jobs are terminal. A failed listing fails the run and deletes nothing.
- [x] `CrawlSchedulerService`: claim plans atomically (update, then read the token back), one operation per plan,
  claim renewal, recovery of runs whose server stopped, interval and cron schedules (Cronos, time-zone aware),
  finishing Ingesting operations, and pruning after `OperationRetentionDays`. Settings in `Crawling`
  (`SchedulerEnabled`, `SchedulerIntervalSeconds`, `MaxConcurrentOperations`, `ClaimMinutes`).
- [x] Routes, all listed in the plan, with OpenAPI metadata. Draft preview was not added (a draft can be tested;
  previews are for stored plans).
- [x] RBAC: `CrawlPlan` and `CrawlOperation` resources; reads are Read, create and replace are Write, delete is Delete,
  and test, preview, start, stop, and confirm-deletions are Execute. Denials and bypasses are audited; secret changes
  and turning off robots.txt are audited as `CrawlPlanSecurityChanged` (names only).
- [x] Request capture redacts secrets in request bodies (`JsonBodyRedactor`). This applies to every route, so login
  passwords and model-runner keys are now masked in request history too.
- [x] Cascade: deleting a subject or tenant deletes its crawl plans, objects, and operations. Deleting a plan keeps its
  links (detached) unless `deleteLinks=true`.
- [x] Metrics: `pneuma_crawl_operations_total{type,outcome}`, `pneuma_crawl_objects_total{type,action}`,
  `pneuma_crawl_operation_duration_seconds{type}`, `pneuma_crawl_bytes_total{type}`, `pneuma_crawl_running`; a root span
  per operation (`crawl <type>`) with `stage:Enumerate` and `stage:Dispatch` children.
- [x] Grafana: `assets/grafana/pneuma-crawling.json` and the factory profile copy.
- [x] Admin dashboard: Sources workspace tabs "Crawl Plans" (`CrawlPlansView`: subject filter, row action menu with
  View, Edit, View JSON, Duplicate, Start, Stop, Test, Preview, Operations, Delete; bulk delete; delete with an
  optional "also delete links") and "Crawl Operations" (`CrawlOperationsView`: plan and status filters). The
  schema-driven `CrawlPlanFormModal` shows secrets as set or not set, sends them only when typed, can clear them, and
  tests the draft before saving. `CrawlOperationModal` shows counts, per-object results by action, and the
  confirm-deletions flow. `LinksView` gained a crawl plan column and filter.
  Route inventory: `/dashboard/sources/crawl-plans`, `/dashboard/sources/crawl-operations` (first shipped under the
  Ingestion workspace; see the 2026-09-29 layout entry).
- [x] Subject dashboard: "Crawl Plans" view (`/dashboard/crawlers`, in the Sources group) with plans and their recent operations, scoped by
  subject, using the same form, test, preview, and operation modals.
- [x] i18n keys for every new string: admin `en`; subject `en` and `es`.
- [-] SDKs (C#, JS, Python): every crawl-plan and crawl-operation operation is in all three. The loopback harness
  cases run against a live server and are batched into Phase 10 with the earlier SDK cases.
- [x] MCP: `pneuma_enumerate_crawl_plans`, `pneuma_get_crawl_plan`, `pneuma_create_crawl_plan`,
  `pneuma_update_crawl_plan`, `pneuma_test_crawl_plan`, `pneuma_preview_crawl_plan`, `pneuma_start_crawl_plan`,
  `pneuma_stop_crawl_plan`, `pneuma_enumerate_crawl_operations`, `pneuma_get_crawl_operation`.
- [x] Postman: "Crawl plans" and "Crawl operations" folders with every route, examples, and negatives.
- [x] Docs: new `CRAWLING.md`; `REST_API.md`; `MCP_API.md`; `TELEMETRY.md`; `CHANGELOG.md`. `README.md` waits for the
  connectors (Phase 10), as planned.
- [x] Tests, positive (`FakeCrawler`, suite `CrawlFramework`): first run adds all; unchanged second run adds nothing;
  changed token re-ingests into the same link; deletion with deletions on; failed item retried; `Succeeded` and
  `PartiallySucceeded`; labels and tags reach chunks; filters; interval and cron next-run across DST; due plans run
  on schedule; two schedulers claim once; startup recovery (and a live claim left alone); pruning; settings codec
  round trip; subject deletion cascade.
- [x] Tests, negative (suites `CrawlFramework` and `CrawlApi`): deletions off marks `Missing`; deletion guard holds and
  confirms; a failed listing deletes nothing; secrets absent from every response and from request history, stored as
  ciphertext; draft test persists nothing; preview creates nothing (and 502 when the source fails); concurrent start
  returns 409; stop cancels and stop when idle is 409; a running plan cannot be deleted; invalid cron and short
  interval return 400; missing settings, bad ranges and choices, a changed type, an unregistered type, and a subject
  without models or collection are 400; cross-tenant reads, lists, secrets, and deletes see nothing; missing
  permission returns 403 and is audited; pushed content cannot overwrite a crawl-managed key (409); MCP tools
  create, read, test, preview, start, stop, and enumerate without returning secrets, and reject bad input.
- [-] Database contract tests for the crawl tables: the `CrawlFramework` suite runs through `TestDatabase`, so it
  runs against any provider with `PNEUMA_TEST_DB_TYPE`. The Postgres, MySQL, and SQL Server runs are in Phase 10 (the
  bench stack is shared with another task and not started here).

### Phase 8: Connectors (A4, A5, A7, A9, A10)

Each connector is a class, a settings DTO, a schema for the form, a `CRAWLING.md` section, a Postman example, and
a test suite against a loopback stub (suite `Crawlers`).

- [x] A4 Web (CrawlSharp, `WebSiteCrawler`): start URLs, depth, page cap, scope, include and exclude globs (plan
  filter), sitemap, robots (turning it off is admin-only, 403 otherwise, and audited), crawl delay, parallelism, user
  agent, JavaScript rendering, authentication (Basic, bearer, API-key header), tracking-parameter removal, URL
  normalization, P5 on every URL, content-hash versions. Not done: canonical links (CrawlSharp does not expose them).
- [x] A4 tests: page cap, scope and off-host links, parameter collapse, robots on and off, authentication (crawl, test,
  and read), private start URL refused, invalid settings, error pages not listed, redirect loop ends, redirected page
  read from its target, end to end (add all, only the changed page re-ingested, removed page Missing). Patterns and
  invalid caps are covered by the framework filter and validation tests.
- [x] A5 Sitemap (`SitemapCrawler`): sitemap and sitemap-index URLs, `robots.txt` discovery, gzip, `lastmod` as version
  token, patterns (plan filter), `MaxUrls`, safe XML parsing (DTDs refused), off-host URLs ignored.
- [x] A5 tests: index plus plain and gzipped sitemaps, `lastmod` versions on and off, the URL limit, `robots.txt`
  discovery, the host rule, a private address refused, a non-sitemap document and a DTD rejected. Oversize is the
  shared download cap (fetch-safety tests).
- [x] A7 S3 (Blobject.AmazonS3, `S3Crawler`): endpoint (path-style), region, bucket, prefix, subfolders, keys or none
  for a public bucket; ETag as version token; layered connectivity test. Not done: instance-profile credentials.
- [x] A7 tests: the shared bucket logic over a local directory (prefix, subfolders, versions, skip list, download limit,
  end to end with deletions), settings and DNS failures, and a live case against Less3 (`PNEUMA_TEST_S3_ENDPOINT`;
  passed against a temporary `jchristn77/less3:v4.0.0` container: listing, ETag change detection, reads, bad keys).
- [x] A9 CIFS (OpenCIFS, `CifsCrawler`): host, port, share, path, domain, user, password; skip list; `localhost` to
  `host.docker.internal` inside containers; layered diagnostics (settings, DNS, TCP, sign-in and share, listing).
- [x] A9 tests against an in-process OpenCIFS server on a free port: listing, skip list, versions, reads, download
  limit, subfolders, bad password and missing share at the auth step, end to end (add, change, delete).
- [x] A10 NFS (OpenNFS, `NfsCrawler`): host, port, mount port, export, path, UID and GID (AUTH_SYS); layered diagnostics.
  NFSv3 only; NFSv4 is not offered.
- [x] A10 tests against an in-process OpenNFS server on free ports: listing, versions, reads, subfolders, missing export
  at the auth step; settings (including an unsupported version) and a closed port.
- [x] A12 GitHub (GitHubCrawler 1.0.1, `GitHubRepositoryCrawler`, added at the user's request): repository URL, folder,
  access token (secret); links point at the file on GitHub; requests use the fetch-safety handler and download
  limit. Limits of the library: github.com only, default branch only, no file SHAs (each run re-reads every file;
  unchanged content ends early by content hash). Other Git remotes remain part of A12.
- [x] A8 Azure Blob (Blobject.AzureBlob, `AzureBlobCrawler`, added at the user's request): account, endpoint (Azurite
  or sovereign clouds), container, prefix, subfolders, access key (secret); ETag versions.
- [x] Google Cloud Storage (Blobject.GoogleCloud, `GoogleCloudCrawler`, added at the user's request; not an item in
  `INGESTION_IMPROVEMENTS.md`): project, bucket, prefix, subfolders, custom endpoint, service account JSON key
  (secret); ETag versions.
- [x] A11 Local folders (Blobject.Disk, `LocalFolderCrawler`, added at the user's request): off unless an administrator
  lists `Crawling.AllowedLocalRoots`; the folder must be an absolute path inside a root (checked on save through the
  new `ICrawler.CheckSettings` hook, and again on every run); no network steps in the test.
- [x] Tests: GitHub against a stub contents API (host-rewriting handler): listing with the token, folder filter, links,
  reads, missing token and repository, non-GitHub URL, end to end (second run re-reads without duplicating links).
  Local folders: listing, reads, allowed, outside, climbing, relative, and no-roots cases, and save-time refusal, end
  to end. Azure and Google Cloud: settings and DNS negatives, plus live cases that passed against temporary Azurite
  and fake-gcs-server containers (gated by `PNEUMA_TEST_AZURE_ENDPOINT` and `PNEUMA_TEST_GCS_ENDPOINT`).
- [x] OpenCIFS package: the published OpenCIFS 0.1.1 replaced the local build; the local package source, packed
  files, and cached local packages are removed. 0.1.1's directory entries carry timestamps, so the CIFS crawler no
  longer reads each file's metadata separately.
- [x] CrawlSharp 1.1.0: redirects are followed again (loops are detected), a redirected page is keyed by its final
  address so the old and new addresses collapse, credentials are limited to the start URLs' origins, and incomplete
  authentication settings are rejected when a plan is saved (`ICrawler.CheckSettings`). RestWrapper 3.3.1.

### Phase 9: Scheduled link refresh (A3)

- [x] `RefreshIntervalMinutes`, `NextRefreshUtc`, `LastRefreshUtc`, `RefreshFailures`, `SourceETag`, and
  `SourceLastModifiedUtc` on links; `DefaultRefreshIntervalMinutes` on subjects; `triggeredby` on jobs (migration 34,
  all providers). A link's null interval follows the subject default and 0 turns it off.
- [x] `LinkRefreshService` (hosted): claims due links in batches (an optimistic compare on `nextrefreshutc`, so two
  servers never check the same link), with 10% jitter; skips links owned by a crawl plan, pushed content, inactive and
  deleting links; postpones links with a pending job (Busy); queues a re-ingest with trigger `Refresh`.
- [x] Conditional GET for refresh (`If-None-Match`, `If-Modified-Since`), storing ETag and Last-Modified on the link.
  This is part of P14, pulled forward because refresh without it re-downloads every page. Checks go through the crawl
  fetch-safety policy. A failed check keeps the current version and backs off 15 minutes, doubling, capped at the
  interval.
- [x] Job `Trigger` field (`Submit`, `Reingest`, `Refresh`, `Crawl`). Deviation: no `Api` value, because REST, SDK,
  MCP, and dashboard submissions cannot be told apart on the server; all are `Submit`.
- [x] `PUT /v1.0/links/{id}` sets the interval (or `useSubjectDefault`); `POST /v1.0/links/refresh-interval` for bulk;
  `POST /v1.0/links/{id}/refresh` checks now. `refreshIntervalMinutes` on single and bulk link submission. Changing a
  subject's default reschedules the links that follow it.
- [x] Metric `pneuma_link_refresh_total{outcome}`.
- [x] Dashboards: "Next Refresh" column, a schedule dialog with presets (subject default, off, hourly, daily, weekly,
  custom), a check-now action, a bulk schedule action, and the subject default on both subject forms (admin and subject
  dashboards; en strings, es falls back).
- [x] SDKs (C#, JavaScript, Python), MCP, Postman, docs (`REST_API.md`, `MCP_API.md`, `CRAWLING.md`, `TELEMETRY.md`,
  `CHANGELOG.md`), and the `LinkRefresh` settings section in the shipped configs. Deviation: MCP has a dedicated
  `pneuma_set_link_refresh` tool (interval, subject default, and check now) rather than an interval on
  `pneuma_submit_link`, which does not exist; MCP adds content through `pneuma_submit_content`.
- [x] Tests, positive: due link checked once and queued; 304 ends early and moves the next refresh; changed ETag
  queues a re-ingest (the replacement itself is the existing re-ingest path); jitter spreads due links; API and MCP.
- [x] Tests, negative: intervals under 60 minutes or over a year rejected; failed refresh keeps the old version and
  backs off; deleted, inactive, pushed, and crawl-owned links never refreshed; a busy link postponed; two claims on
  one link, one winner. Suite `LinkRefresh`: 8 cases.

### Phase 10: Close-out

- [ ] Full build with zero warnings; full `Test.Automated`, `Test.Xunit`, `Test.Nunit` runs; database contract suites
  against Postgres (the bench stack on port 25432) as well as SQLite.
- [ ] Dashboards: `npm ci && npm run build` for admin and subject dashboards; manual check at 1280, 768, and 390 px in
  light and dark themes.
- [ ] SDK test harnesses pass for C#, JavaScript, and Python.
- [ ] Simulated user session per `SIMULATED_USER_TESTING.md` on a clean `pneuma-usertest` stack, report in
  `user-testing/<date>-crawlers.md`; findings go to the user for decisions before any fix.
- [ ] `REST_API.md`, `MCP_API.md`, `CRAWLING.md`, `TELEMETRY.md`, `README.md`, `DOCKERHUB_README.md`, and
  `CHANGELOG.md` reviewed against the code; Postman collection reviewed against the routes.
- [ ] `INGESTION_IMPROVEMENTS.md` checklist ticked for completed items; `RETRIEVAL_IMPROVEMENTS.md` ticked for RI-E,
  RI-F, RI-O.

## All items from INGESTION_IMPROVEMENTS.md

Every item from the source plan, in its rank order, with its status on this branch.

| Rank | ID | Item | Status on this branch |
|---:|---|---|---|
| 1 | A1 | File upload | Deferred |
| 2 | P1 | Replace the previous version on re-ingest | Done (Phase 4) |
| 3 | A4 | Web crawler | Done (Phase 8) |
| 4 | A0 | Source and sync framework | Done (Phase 7) |
| 5 | A2 | Inline content push API | Done (Phase 6) |
| 6 | P3 | Completeness accounting | Done (Phase 1) |
| 7 | P5 | Fetch safety | Done (Phase 2) |
| 8 | P2 | Durable queue | Deferred (crawl runs create bursts of jobs; the Phase 7 tests showed no claim races, so the existing claim holds for now) |
| 9 | P4 | Shared retry policy and endpoint limiter | Done (Phase 3) |
| 10 | P6 | Embedding model profiles | Deferred |
| 11 | S1 | MCP ingestion write tools | Partly done: `pneuma_submit_content` (Phase 6) and the ten crawl plan tools (Phase 7) |
| 12 | P8 | Contextual chunk header | Done (Phase 5) |
| 13 | P9 | Enrichment switches | Deferred |
| 14 | P10 | Failure categories | Done (Phase 1) |
| 15 | A3 | Scheduled link refresh | Phase 9 |
| 16 | A5 | Sitemap source | Done (Phase 8) |
| 17 | P7 | Model-aware token budget | Done (Phase 5) |
| 18 | A7 | S3 source | Done (Phase 8) |
| 19 | A13 | SharePoint and OneDrive source | Deferred |
| 20 | P12 | Type-detection hints | Deferred |
| 21 | P16 | Ingestion observability | Deferred (sync metrics are in Phase 7) |
| 22 | S2 | SDK parity | Partly: every new operation lands in all three SDKs |
| 23 | P14 | Conditional GET and text hash | Partly: conditional GET in Phase 9 |
| 24 | A9 | CIFS/SMB source | Done (Phase 8) |
| 25 | A20 | Authenticated links | Partly done: web crawl plan authentication (Phase 8) |
| 26 | P13 | Structure-aware chunking | Deferred |
| 27 | P15 | Entity merge serialization | Deferred |
| 28 | A12 | Git source | Partly done: GitHub repositories (Phase 8); other Git remotes deferred |
| 29 | P11 | Resume from failed stage | Deferred |
| 30 | A15 | Confluence source | Deferred |
| 31 | A14 | Google Drive source | Deferred |
| 32 | P18 | Unicode sanitizing | Partly: pushed content in Phase 6 |
| 33 | A6 | RSS and Atom source | Deferred |
| 34 | P17 | Cross-link duplicates | Deferred |
| 35 | P21 | Webhooks | Deferred |
| 36 | T1 | Archive expansion | Deferred |
| 37 | A8 | Azure Blob source | Done (Phase 8) |
| 38 | S5 | Benchmark gate in CI | Deferred |
| 39 | P19 | Fair scheduling | Deferred |
| 40 | P20 | Ingestion profiles | Deferred |
| 41 | T2 | Email files | Deferred |
| 42 | A16 | Notion source | Deferred |
| 43 | T3 | Audio and video transcription | Deferred |
| 44 | S4 | Documentation fixes | Partly: stale stage list and override key fixed in Phase 1 docs |
| 45 | S3 | Postman parity | Partly: every new route lands in Postman |
| 46 | A10 | NFS source | Done (Phase 8) |
| 47 | T5 | Source-code awareness | Deferred |
| 48 | A19 | SQL source | Deferred |
| 49 | P22 | Export and import | Deferred |
| 50 | A18 | IMAP source | Deferred |
| 51 | A17 | Slack source | Deferred |
| 52 | A11 | Local directory source | Done (Phase 8, behind `Crawling.AllowedLocalRoots`) |
| 53 | T4 | EPUB and OpenDocument | Deferred |

## Open questions

- **CIFS and NFS client library (answered 2026-09-28).** Blobject.CIFS only reaches port 445, which Windows' SMB
  server holds locally. The user chose OpenCIFS and OpenNFS clients (both MIT, both accept a port). OpenCIFS will be
  published to NuGet shortly; build against the source until then and switch to the package.
- **OpenCIFS sign-in without a domain.** The in-process OpenCIFS server refuses a session setup whose domain is empty
  when the account has a domain (`WORKGROUP`). The crawler's help text tells operators to set the domain; worth
  checking whether that is intended in OpenCIFS.

- **Blobject 6.0 for CIFS and NFS (answered 2026-09-29).** Blobject.CIFS and Blobject.NFS 6.0 are built on OpenCIFS
  and OpenNFS and add port settings. The user chose to move the CIFS and NFS crawlers back to Blobject.

## Progress log

Newest entries last. Each entry names what was done and how it was verified.

- **2026-09-28.** Created `feature/crawlers` from `5efe761`. Moved `PARTIO_REMOVAL_PLAN.md` and `CKG_IMPROVEMENTS.md` to
  `archive/` (and updated the one live link to the latter). Replaced 53 em-dashes in `INGESTION_IMPROVEMENTS.md`
  headings. Wrote this plan.
- **2026-09-28. Baseline.** `dotnet build src/Pneuma.sln` had 9 warnings (unresolved crefs, a missing param tag,
  nullable warnings in two test suites); fixed, now 0 warnings. `Test.Automated` (net10.0): 180 of 180 passed in 276 s.
- **2026-09-28. Phase 1 done** (SDK harness cases pending). New suite `IngestionReliability` (11 cases) plus the
  existing `Ingestion` and `IngestionStages` suites: 41 of 41 passed. Build 0 warnings; admin and subject dashboards
  build. Changes beyond the plan: the classifier throws on a failed or unparseable response (it used to return an
  empty graph silently); the test database now seeds model runners at `127.0.0.1:9`, so tests never reach a local
  Ollama; `Test.Automated --suite` runs named suites; `pneuma_enumerate_jobs` now honors its documented `status`
  filter; two em-dashes removed from the Postman collection; a shared `ApiClientHelper` and `StubModelServer` in
  `Test.Shared` for REST and model-endpoint tests.
- **2026-09-28. Phase 2 done.** New suite `FetchSafety` (10 cases): 10 of 10 passed. Build 0 warnings. Changes beyond
  the plan: `AuditEventTypeEnum.FetchBlocked` (and `CrawlPlanSecurityChanged` for Phase 7); `StubWebSite` test support
  (programmable pages, redirects, ETag 304s) for this and the crawler phases; `ApiClientHelper` gained
  `ExtractString` and `CreateConfiguredSubjectAsync`.
- **2026-09-28. Phase 3 done.** New suite `ModelRetry` (8 cases) plus `ExternalServices` and `IngestionReliability`:
  all passed (the existing 429 embedding test now exercises the shared handler). The first run caught a real bug in
  the new `ModelResponseErrors`: a 400 whose body mentioned "at capacity" was treated as transient; an explicit 4xx
  now decides first. Behavior change to flag: `maxConcurrentRequests` is now enforced for ingestion, so a runner at the
  default of 2 caps embedding concurrency at 2 (called out in `CHANGELOG.md`).
- **2026-09-28. Phase 4 done.** New suite `VersionReplacement` (7 cases): 7 of 7 passed. Test support gained
  `IngestionHarness` (tenant, subject, collection, link, and a job runner over the real pipeline), a failure switch on
  `FakeRecallDbClient`, and `FlakyEmbeddingSemanticProcessor`.
- **2026-09-28. Phase 5 done** (benchmark pending). New suite `ChunkContext` (8 cases) and `Chunking`: 14 of 14 passed.
  TextChunker already handled most of P7 (known model limits and the WordPiece reserve), so Pneuma adds the margin,
  the runner override, and re-chunking. The subject dashboard turned out to have a partial Spanish catalog; every key
  this branch adds now has a Spanish entry.
- **2026-09-28. Full suite after Phase 5:** 223 of 223 passed (180 at baseline plus 43 new).
- **2026-09-28. Phase 6 done.** New suite `InlineContent` (8 cases): 8 of 8 passed. The content logic moved into a
  shared `ContentSubmissionService` so the REST routes and the MCP tool cannot drift.
- **2026-09-28. Phase 7 done** (SDK harness cases and non-SQLite provider runs batched to Phase 10). New suites
  `CrawlFramework` (23 cases) and `CrawlApi` (9 cases): 32 of 32 passed. Admin and subject dashboards lint, test, and
  build. Changes beyond the plan: request capture now masks secret-named JSON values in every request body (login
  passwords and model-runner keys were stored unmasked before); `CrawlActionEnum` gained `Unchanged` and `Missing`
  (counted, not recorded per object); crawled links carry `crawl:{planId}:{key}` as their external key (hashed past
  256 characters) so two plans, or a plan and pushed content, never collide, and pushed content cannot overwrite a
  crawled link; `CrawlSchedule.IntervalMinutes` is validated (400) instead of clamped, so a too-short interval is an
  error rather than a silent change.
- **2026-09-28. Full suite after Phase 7:** 263 of 263 passed (223 after Phase 5, plus 8 inline-content and 32 crawl
  framework cases).
- **2026-09-28. Phase 8 done** (OpenCIFS package switch pending). New suite `Crawlers` (23 cases) plus `CrawlApi` (10)
  and `CrawlFramework` (23): all passed, including the live S3 case against a temporary Less3 container. Findings along
  the way: CrawlSharp sends no authentication unless `Settings.Authentication.Type` is set; its per-request pause is
  `RequestDelayMs` (default 2500 ms), not `ThrottleMs` (the 429 backoff); and it never finishes on a redirect loop when
  `FollowRedirects` is on, so the crawler turns it off (the HTTP stack still follows ordinary redirects). The first two
  also affect AssistantHub's `WebRepositoryCrawler`. The CIFS and NFS crawlers moved from Blobject to OpenCIFS and
  OpenNFS (see D6).
- **2026-09-28. Phase 8 additions** (at the user's request): GitHub (GitHubCrawler), Azure Blob, Google Cloud Storage,
  and local folders (Blobject). Suite `Crawlers` now has 29 cases. The three crawl suites together: 62 cases, 59 passed, 3 skipped
  (the gated S3, Azure, and Google Cloud live cases). Each live case passed when its server was running: S3 against
  Less3, Azure against Azurite, Google Cloud against fake-gcs-server (all temporary containers, since removed). Cleanup: the tracked root `pneuma.json` (a leftover from running the
  server from the repository root; nothing references it), an empty `src/Pneuma.Server/blobs`, Python caches, and
  about 835 MB of test databases and blobs in the temp directory were removed. The local OpenCIFS package source is
  now switched off between builds.
- **2026-09-28. Package updates.** CrawlSharp 1.1.0, RestWrapper 3.3.1, OpenCIFS 0.1.1 (published), OpenNFS 0.1.0
  (published). The crawl suites pass: 62 cases, 59 passed, 3 skipped (the gated live cases).
- **2026-09-29. OpenNFS 0.1.1.** Blobject 5.1.0, CrawlSharp 1.1.0, and OpenCIFS 0.1.1 were already the latest on NuGet.
  Crawl suites: 62 cases, 59 passed, 3 skipped (the gated live cases).
- **2026-09-29. Blobject 6.0.0.** Core, AmazonS3, AzureBlob, GoogleCloud, and Disk updated from 5.1.0; no code changes were
  needed. Full suite: 293 cases, 290 passed, 3 skipped (the gated live cases).
- **2026-09-29. Phase 9 done.** Scheduled link refresh: migration 34, `LinkRefreshService`, conditional GET, job
  `Trigger`, three routes, MCP `pneuma_set_link_refresh`, metric, dashboards, SDKs, Postman, and docs. New suite
  `LinkRefresh` (8 cases). Full build: 0 warnings. Full suite: 301 cases, 298 passed, 3 skipped (the gated live cases).
- **2026-09-29. CIFS and NFS back on Blobject 6.0.0** (the user's decision). `CifsCrawler` and `NfsCrawler` now derive
  from `BlobCrawlerBase`, which gained an optional sign-in step so a bad password or missing share still fails the
  test's "auth" step; `FileShareCrawlerBase`, the two session classes, `IFileShareSession`, and `FileShareEntry` were
  removed, and the direct OpenCIFS.Client and OpenNFS.Client references dropped (Blobject brings them). Keys and
  version tokens are unchanged, so existing links are not re-ingested; the NFS test now checks that too. The NFS mount
  port accepts 0 to ask the portmapper. Crawl suites: 62 cases, 59 passed, 3 skipped (the gated live cases).
- **2026-09-29. Dashboard layout** (the user's request: links and crawl plans are both source material and belong
  together). Admin: a new Sources workspace holds Links, Crawl Plans, and Crawl Operations; Knowledge keeps Subjects,
  Collections, and Search; Ingestion keeps Live, Queue, and Jobs. Old URLs (`/dashboard/links`,
  `/dashboard/knowledge/links`, `/dashboard/ingestion/crawl-plans`, `/dashboard/ingestion/crawl-operations`) redirect.
  Subject dashboard: the single Content group is split into Knowledge (My Subjects), Sources (Links, Crawl Plans,
  Ingestion), and Assistant (Ask through Feedback); "Crawlers" is now "Crawl Plans" to match its page title.
