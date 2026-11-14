# File Upload (Drag-and-Drop Ingestion)

**Target release:** `0.1.0`.
**Status:** Done (2026-11-13). Archived.

Operators can drag files (PDF, Word, Excel, PowerPoint, text, Markdown, HTML, JSON, CSV, images, and any other type DocumentAtom detects) onto the admin or subject dashboard to ingest them into a subject. The same capability is exposed through the REST API, the three SDKs, and the Postman collection.

---

## How to use this document

Every task is a checkbox: `- [ ]` not started, `- [x]` done. When a task spawns follow-ups, nest them beneath it.

---

## 1. Locked decisions

- [x] **Reuse the inline-content pipeline.** An uploaded file becomes a `SubjectLink` with `sourceKind: Inline`. Its bytes are stored in the blob store under `inline/{tenantId}/{linkId}`, exactly like pushed text. `ContentResolver` already reads inline links from the blob store, and a null declared type makes `TypeDetectionStage` ask DocumentAtom, so binary files go through the normal pipeline unchanged.
- [x] **Raw body, one file per request.** `POST /v1.0/subjects/{subjectId}/files` with the file bytes as the body and metadata in the query string. Watson does not parse multipart, and one file per request gives per-file progress, retries, and errors. The dashboards upload several files concurrently.
- [x] **Reject unsupported files at upload time.** When a DocumentAtom client is configured, the server detects the type before storing anything and returns **415** for `Unknown`. If DocumentAtom is unreachable at upload time the upload is accepted and the job's TypeDetection stage decides (detection at upload is a gate, not a dependency).
- [x] **`FileName` column on links.** A nullable `filename` column (migration 37 on all four providers). The title defaults to the file name.
- [x] **Readable URI.** Uploaded links carry `pneuma-inline://{linkId}/{url-escaped filename}` so job logs, the live pipeline view, and chat citations show the file name with no extra server fields. The blob key is still derived from the link id only.
- [x] **Size limit.** New `Ingestion.MaxUploadBytes`, default 100 MB (104857600), clamped to [1024, 1073741824]. Checked against `Content-Length` before the body is read (Watson's own `MaxRequestBodySize` is unlimited by default).
- [x] **Storage follows the server's configuration (revised after review).** `Integrations.Blob.Provider` is `Disk` (files under `Integrations.Blob.Directory`) or `S3` (objects in `S3.Buckets.Blobs`, `pneuma-blobs`, using the `S3` section's connection). It applies to the whole blob store (uploads, pushed text, job source copies, classification cache) with no fallback between providers. The Docker configurations use `S3` (Less3). The first cut kept everything on disk; that was revised because the stack already runs S3-compatible storage.
- [x] **Chunked uploads are supported (revised after review).** A body sent with `Transfer-Encoding: chunked` is read chunk by chunk and refused with 413 as soon as it passes `Ingestion.MaxUploadBytes`; a declared `Content-Length` over the limit is refused before reading. The SDKs stream bodies of unknown length chunked.
- [x] **Replacement semantics match text push.** The same `externalKey` overwrites the blob in place and queues a new job; the previous version stays searchable until the new one is indexed.
- [x] **Scope.** Admin and subject dashboards. The user dashboard and MCP are out of scope (§9).

---

## 2. API contract

### `POST /v1.0/subjects/{subjectId}/files`

| Part | Value |
|---|---|
| Body | Raw file bytes (required, non-empty). |
| `Content-Type` header | The file's MIME type. `application/octet-stream` or missing means "infer from the file name extension". Parameters (`; charset=...`) are ignored. |
| `filename` (query, required) | Original file name, at most 256 characters. Path segments are stripped (`a/b/c.pdf` → `c.pdf`). |
| `title` (query) | Display title; defaults to the file name. |
| `externalKey` (query) | Upsert key within the subject, at most 256 characters. |
| `labels` (query, repeatable) | Each occurrence is one label. |
| `tag.{key}` (query) | One tag per parameter, e.g. `tag.team=finance`. |

Responses: **201** created / **200** replaced → `ContentSubmitResult` `{ index, statusCode, replaced, link, jobId }`. **400** missing file name, empty body, key too long. **404** unknown subject. **409** keyed content being deleted or owned by a crawl plan. **413** over `Ingestion.MaxUploadBytes`. **415** DocumentAtom does not recognize the file. **501** no blob store. Same subject preconditions as `/content` (models and collection required). Requires Subject Write.

Text-family files (`text/plain`, `text/markdown`, `text/html`, `application/json`, by MIME type or by `.txt`/`.md`/`.markdown`/`.htm`/`.html`/`.json` extension) keep a declared document type and skip detection, like pushed text. All other files are detected.

### `GET /v1.0/ingestion/upload-settings`

→ `UploadSettings` `{ maxUploadBytes, maxFileNameLength, supportedTypes: [{ documentType, extensions: [], mimeTypes: [] }] }`. `supportedTypes` is advisory (what the dashboards pre-check); DocumentAtom's detection is authoritative.

### `GET /v1.0/links/{id}/source` (changed)

For an inline link (pushed text or an upload) the original bytes are served from the blob store with the stored content type and `Content-Disposition: inline; filename="..."` (`?download=true` makes it `attachment`). Other links keep using the S3 source artifact. The S3 copy written during ContentRetrieval now records the link's content type.

### `SubjectLink.fileName` (new)

Null for URL, crawled, and pushed-text links.

---

## 3. Backend tasks

- [x] `SubjectLink.FileName` property (XML doc).
- [x] Migration 37 `Add link file name` on Sqlite, Mysql, Postgresql, SqlServer; `filename` in insert, update, and map for every provider.
- [x] `IngestionSettings.MaxUploadBytes` (backing field, clamp).
- [x] `Pneuma.Core.Helpers.UploadFileTypes`: extension → MIME map, text-family detection, advisory supported-type list, file-name normalization.
- [x] `InlineContentKeys.UriFor(linkId, fileName)` overload.
- [x] `Pneuma.Core.Requests.SubmitFileRequest` (typed DTO: file name, content type, title, external key, labels, tags).
- [x] `Pneuma.Core.Responses.UploadSettings` and `UploadSupportedType`.
- [x] `ContentSubmissionService`: extract the shared store/upsert/create core; add `SubmitFileAsync(userId, subject, SubmitFileRequest, byte[] data, token)`; optional `IAtomizer` for upload-time detection (415 on `Unknown`, accept on detector failure).
- [x] `SubjectLinkRoutes`: `POST /v1.0/subjects/{subjectId}/files`, `GET /v1.0/ingestion/upload-settings`, inline `/source` from the blob store with content type and disposition; OpenAPI metadata with an `application/octet-stream` binary body and documented query parameters.
- [x] `PneumaServer.Atomizer` (settable, optional) passed to the routes; `Bootstrapper` sets it to the DocumentAtom client.
- [x] `ContentRetrievalStage`: pass the link's content type to `PutSourceAsync`.
- [x] `RequestHistoryCaptureService`: for non-text request content types store `[binary N bytes]` instead of decoding the body.
- [x] `docker/pneuma.json` and `docker/factory` config: `maxUploadBytes`.

## 4. Tests (`Test.Shared/Suites/FileUploadSuite.cs`, registered in `PneumaSuites`)

- [x] Route: upload Markdown with a file name → 201, `fileName`, readable URI, title defaults to file name, job queued, labels/tags applied.
- [x] Route: `application/octet-stream` with a `.md` name infers `text/markdown`.
- [x] Route: same `externalKey` twice → 201 then 200 `replaced`.
- [x] Route: missing file name, empty body → 400; unknown subject → 404; no token → 401.
- [x] Route: `/source` returns the original bytes with the content type and a `filename` disposition; `?download=true` → attachment.
- [x] Route: `/ingestion/upload-settings` returns the limit and types.
- [x] Route: the request-history entry for an upload holds the binary placeholder, not the bytes.
- [x] Service: over `MaxUploadBytes` → 413; detector `Unknown` → 415 and nothing stored; detector throws → accepted.
- [x] Pipeline: an uploaded binary file runs type detection (fake atomizer reports `Pdf`) and completes with `DocumentType` `Pdf`.
- [x] Helpers: `UploadFileTypes` normalization and inference.
- [x] Database contract: `FileName` round-trips (shared suites, all providers).

## 5. Frontend tasks (admin + subject dashboards)

- [x] `ApiClient.uploadFile(subjectId, file, opts, onProgress, signal)` via `XMLHttpRequest` (upload progress), `getUploadSettings()`, `downloadLinkSource(link)`.
- [x] `utils/files.js`: byte formatting, extension checks, link display name (`fileName || title || url`), inline detection.
- [x] `FileUploadModal`: subject picker, drop zone + browse (keyboard accessible), labels/tags, "replace files with the same name" (`externalKey = filename`), per-file queue (name, size, progress, status, error, retry, remove), 3 concurrent uploads, client-side size/type pre-check from upload settings, confirm before closing with uploads in flight.
- [x] Page-level drag overlay on `LinksView`: dropping files opens the modal with them queued.
- [x] Links table: Source column renders uploaded/pushed links as a name with a kind badge (no broken `pneuma-inline://` anchor); new optional columns Kind, Type, Size.
- [x] Link detail: file name, content type, size, external key; "Download original" for inline links. Admin "View source" downloads non-text sources instead of rendering them as text.
- [x] Confirm modals use the display name.
- [x] i18n (en + es where the dashboard ships es), light/dark styles, 1280/768/390 layouts.

## 6. SDK tasks

- [x] C#: `SubjectLink.FileName`; `UploadFileOptions`, `UploadSettings`, `UploadSupportedType`; `UploadFileAsync(Stream|byte[])`, `UploadFileFromPathAsync`, `GetUploadSettingsAsync`, `DownloadLinkSourceAsync`; tests.
- [x] Python: `upload_file`, `get_upload_settings`, `download_link_source`; tests.
- [x] JS: `uploadFile`, `getUploadSettings`, `downloadLinkSource`; tests.
- [x] SDK READMEs.

## 7. Documentation and Postman

- [x] `REST_API.md`: the upload route, upload settings, `/source` change, `fileName`, request-history rule, `Ingestion.MaxUploadBytes`.
- [x] `README.md`: feature mention and a `curl --data-binary` example.
- [x] `CHANGELOG.md`.
- [x] Postman: Upload File (Markdown), Upload File (replace, 200), Upload File (missing filename, 400), Upload Settings, with tests; fixture files in `postman/fixtures/`.

## 8. Release

- [x] `dotnet build src/Pneuma.sln`, `Test.Automated` green.
- [x] `npm run build` (and tests where present) in each dashboard; SDK tests.
- [x] Move this file to `archive/`, commit, push, `build-all.sh v0.1.0` (images built after the push).

## 9. Verification notes

- Review follow-up: `S3BlobStore` and `BlobStoreFactory` are tested against an in-process S3 stand-in (`StubS3Server`), including an end-to-end upload with the S3 provider; chunked uploads (full read, 413 mid-stream, history placeholder) are tested through the real server.

- Backend: `Test.Automated` (net10.0 and net8.0) passes, including the new `FileUpload` suite (route, service, pipeline, helper, request-history, and OpenAPI cases) and `Database.SubjectLink_FileNameRoundTrips` (SQLite; the other providers run the same contract when `PNEUMA_TEST_DB_TYPE` points at them).
- Dashboards: `eslint`, `vitest` (`utils/files.test.js`), and `vite build` pass for admin and subject. The drag-and-drop UI was not exercised in a browser.
- SDKs: the C# SDK builds; the JS (`test/upload.test.mjs`) and Python (`tests/test_upload.py`) upload helpers are tested offline against a loopback server. The live smoke harnesses (including the new C# upload steps) need a running Pneuma stack and were not run.

## 10. Out of scope / follow-ups

- Streaming request bodies into the blob store (`IBlobStore` stream overloads) to avoid holding large uploads in memory.
- Versioned blob keys so a failed replacement keeps the previous file for reingest.
- Per-tenant upload quota.
- MCP `pneuma_upload_file` tool and user-dashboard uploads.
