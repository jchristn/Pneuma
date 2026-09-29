# Crawling

A **crawl plan** keeps a subject in sync with a source: a web site, a sitemap, a GitHub repository, an S3, Azure Blob, or Google Cloud Storage bucket, a CIFS or NFS share, or a server folder.
Each run of a plan is a **crawl operation**. An operation lists the source, compares the listing with what the plan
saw before, and ingests what is new or changed. It can also delete what disappeared, but only when you turn that
on. Every object a plan has seen is a **crawl object**, linked to the subject link that holds its content. Crawled
content is ingested like any other link, so search, chat, the graph, and link deletion work the same way.

## Concepts

| Term | What it is |
|------|------------|
| Crawl plan (`cpl_`) | The source, its settings, a filter, a schedule, and what to do with added, changed, and removed objects. |
| Crawl operation (`cop_`) | One run: when it ran, what triggered it, counts per action, and how it ended. |
| Crawl object (`cob_`) | One object the plan tracks: its key, version token, link, and status (Active, Missing, Failed, Excluded). |
| Operation object (`coo_`) | What one operation did with one object, and (once its job finishes) whether it succeeded. |

Crawled links have `sourceKind: Crawl`, the plan's id in `crawlPlanId`, and an `externalKey` of the form
`crawl:{planId}:{key}`. Content pushed with the same key is refused (409), so a push cannot overwrite a crawled link.

## Plan types

`GET /v1.0/crawl-plan-types` lists the types this server supports, each with its settings schema. The dashboards
build their forms from this schema. A type is only listed once its crawler is available.

| Type | Settings property | Change detection |
|------|-------------------|------------------|
| Web | `web` | ETag or Last-Modified of each page; pages without either are re-ingested and skipped by content hash. |
| Sitemap | `sitemap` | `lastmod` of each URL (when `useLastModified` is on). |
| S3 | `s3` | Object ETag. |
| Cifs | `cifs` | Size and last-write time (SMB 2 or 3, via Blobject.CIFS). |
| Nfs | `nfs` | Size and modification time (NFSv3, via Blobject.NFS). |
| GitHub | `gitHub` | None: GitHubCrawler reports no file SHAs, so each run re-reads every file and unchanged content ends early by content hash. |
| AzureBlob | `azureBlob` | Blob ETag. |
| GoogleCloud | `googleCloud` | Object ETag. |
| LocalFolder | `localFolder` | Size and modification time. Only offered when an administrator allows folders (see below). |

## Connectors

### Web

Crawls a site with CrawlSharp from one or more `startUrls`:

- **Scope.** `scope` is `SameHost` (default), `SameRootDomain`, or `ChildPaths`. `maxDepth` (1 to 20) and `maxPages`
  (1 to 100000) bound the crawl. `followLinks` off crawls only the start URLs and, with `useSitemap`, the site's
  sitemap.
- **Politeness.** `respectRobotsTxt` is on by default. Turning it off requires a system or tenant administrator
  (403 otherwise) and is audited. `crawlDelayMs` is the pause between requests (default 500), and
  `maxParallelRequests` defaults to 4. `renderJavaScript` uses the headless browser.
- **Authentication.** `authentication` is `None`, `Basic` (`username`, `password`), `Bearer` (`bearerToken`), or
  `ApiKey` (`apiKeyHeader`, `apiKey`).
- **Keys.** Keys are normalized URLs: lower-case scheme and host, no fragment, no default port. Query parameters named
  in `dropQueryParameters` are removed (default `utm_*`, `fbclid`, `gclid`), so one page is not crawled twice.
- **Versions.** A page's version is the SHA-256 of its content (or its ETag or Last-Modified), so a page is
  re-ingested only when its content changes.
- **Safety.** Every start URL and discovered page must pass `Ingestion.FetchSafety`. A private address is refused
  unless its host is in `AllowedPrivateHosts`. Content is re-read for ingestion through the same policy with the
  plan's authentication.
- **Error pages and redirects.** Pages that return errors are not listed. Redirects are followed (at most 10 hops),
  and a redirected page is listed once, at the address its content came from; a redirect loop ends without looping.
- **Credential scope.** Credentials are sent only to the start URLs' origins, never to other sites a crawl links to.

### Sitemap

Reads `sitemapUrls`:

- **What it reads.** Plain or gzipped `urlset` sitemaps and `sitemapindex` files, nested up to 3 deep and at most
  1000 files per run. A `robots.txt` URL is read for its `Sitemap:` lines.
- **Versions.** With `useLastModified` (default), each URL's `lastmod` is its version; without it, every URL is
  re-read and unchanged content completes early by content hash.
- **Limits and safety.** `maxUrls` (default 10000) caps a run. URLs on a host other than the sitemap's are ignored, as
  the sitemap protocol requires. DTDs are refused. Every fetch passes the fetch-safety policy.

### S3

Crawls a bucket on Amazon S3 or an S3-compatible store (for example Less3 or MinIO):

- **Connection.** `endpoint` is empty for AWS, or a host and port or URL for a compatible store. It uses path-style
  requests with `useSsl`. `region` and `bucket` are required.
- **What it lists.** Objects under `prefix`; `includeSubfolders` off skips keys under deeper prefixes.
- **Credentials.** `accessKey` and `secretKey` (secret), or neither for a public bucket.
- **Versions.** An object's version is its ETag. Objects larger than `Ingestion.FetchSafety.MaxDownloadBytes` fail with
  category `TooLarge`.

### CIFS (SMB)

Crawls a Windows or Samba share over SMB 2 or 3 with Blobject.CIFS (built on OpenCIFS):

- **Connection.** `host`, `port` (default 445), and `share`. `path` limits the crawl to a folder, and
  `includeSubfolders` off lists only that folder.
- **Credentials.** `domain` (the account's domain or workgroup; some servers refuse a sign-in without it),
  `username`, and `password` (secret).
- **Versions.** A file's version is its size and last-write time.
- **Skipped files.** System and temporary files (Thumbs.db, desktop.ini, .DS_Store, `~$` lock files, `._` files,
  `.tmp`) and hidden or system folders (`.` folders, `$RECYCLE.BIN`, System Volume Information) are skipped.
- **Containers.** When Pneuma runs in a container, a `host` of `localhost` or `127.0.0.1` means the host machine
  (`host.docker.internal`).

### NFS

Crawls an NFSv3 export with Blobject.NFS (built on OpenNFS) using AUTH_SYS:

- **Connection.** `host`, `port` (default 2049), `mountPort` (default 20048, the usual fixed mountd port on Linux; 0 asks the server's portmapper on port 111),
  `export`, `path`, and `includeSubfolders`.
- **Identity.** `userId` and `groupId` are presented to the server.
- **Versions and skipped files.** The same rules as CIFS, including the container host mapping.
- **Version support.** Only NFSv3 is supported today.

### GitHub

Crawls the default branch of a github.com repository with GitHubCrawler (the GitHub contents API):

- **Settings.** `repositoryUrl` (`https://github.com/owner/repo`, with or without `.git`, or
  `git@github.com:owner/repo.git`), an optional `path` folder, and an optional `token` (secret). A token is needed for
  a private repository and raises the rate limit from 60 to 5000 requests an hour.
- **Links.** Each file's key is its raw download URL. The link points at the file on GitHub
  (`https://github.com/owner/repo/blob/branch/path`) for citations.
- **Limits.** Other branches, GitHub Enterprise hosts, and other Git servers are not supported by the library. It
  reports no file SHAs, so each run re-reads every file; unchanged files are skipped by content hash, but each still
  costs a request. Requests pass the fetch-safety connection checks and download limit.

### Azure Blob

Crawls an Azure Blob Storage container:

- **Settings.** `accountName`, `container`, an optional `prefix` and `includeSubfolders`, and `accessKey` (secret).
  `endpoint` is empty for `https://{account}.blob.core.windows.net/`; set it for sovereign clouds or the Azurite
  emulator (`http://127.0.0.1:10000/devstoreaccount1`).
- **Versions.** A blob's version is its ETag.

### Google Cloud Storage

Crawls a Google Cloud Storage bucket:

- **Settings.** `projectId`, `bucket`, an optional `prefix` and `includeSubfolders`, and `jsonCredentials`: the service
  account's JSON key file contents (secret). `endpoint` is empty for Google Cloud Storage; set it for an emulator (for
  example `http://127.0.0.1:4443/storage/v1/` for fake-gcs-server).
- **Versions.** An object's version is its ETag.

### Local folders

Crawls a folder on the Pneuma server itself, for example a volume mounted into the container:

- **Off by default.** Local folder plans are offered only when an administrator lists folders in
  `Crawling.AllowedLocalRoots` in `pneuma.json`. A plan's `folder` must be an absolute path inside one of them;
  anything else (including a path that climbs out with `..`) is refused when the plan is saved and when it runs. This
  keeps tenants from reading the server's own files.
- **Settings.** `folder` and `includeSubfolders`.
- **Versions and skipped files.** Size and modification time, with the same skip list as shares.
- **Links.** Links carry a `file://` URI. The roots do not follow symbolic links for the check, so do not place links
  to other folders inside an allowed root.

### Testing the connectors

The `Crawlers` suite tests each connector against a local server:

- **Web and sitemap:** a loopback stub site.
- **CIFS and NFS:** in-process OpenCIFS and OpenNFS servers on free ports.
- **S3:** a local directory standing in for a bucket.
- **GitHub:** a stub contents API (the tests route api.github.com and raw.githubusercontent.com to a loopback site).
- **Local folders:** temporary folders, including roots enforcement. With `PNEUMA_TEST_S3_ENDPOINT` set (plus optionally `_BUCKET`,
  `_REGION`, `_ACCESS_KEY`, `_SECRET_KEY`, `_SSL`), the S3 case also runs against a live server. For a temporary Less3,
  run `docker run -d --name less3-test -p 127.0.0.1:38810:8000 jchristn77/less3:v4.0.0`, then set
  `PNEUMA_TEST_S3_ENDPOINT=http://127.0.0.1:38810/`. The default account is `default` / `default` with bucket
  `default`.
- **Azure Blob (live):** set `PNEUMA_TEST_AZURE_ENDPOINT` (Azurite:
  `docker run -d -p 127.0.0.1:38820:10000 mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0`,
  endpoint `http://127.0.0.1:38820/devstoreaccount1`; the test creates its container).
- **Google Cloud (live):** set `PNEUMA_TEST_GCS_ENDPOINT` and `PNEUMA_TEST_GCS_KEY_FILE` (plus optionally
  `_PROJECT`, `_BUCKET`). With `fsouza/fake-gcs-server -scheme http`, create the bucket first and use a service
  account key whose `token_uri` points at a local endpoint that returns an access token.

## Settings, filter, and schedule

Only the settings object matching `type` is used. Fields are validated against the schema: required fields, numeric
ranges, and choice values. Invalid plans are rejected with 400 and every problem is listed.

**Secrets** (passwords, keys, tokens) are write-only:

- They are stored encrypted, never returned, and redacted from request history.
- `secretsSet` names the secrets that are stored.
- On a replace (`PUT`), a secret you leave out keeps its stored value, and a name in `clearSecrets` is removed.
- Setting or clearing a secret is recorded in the audit log (by name only). So is turning off `respectRobotsTxt`.

The **filter** decides which enumerated objects are kept:

- `includePatterns` and `excludePatterns` are globs matched against the object key (a URL, or a key or path).
  `*` matches any run of characters and `?` matches one character.
- `allowedContentTypes` lists the content types to keep.
- `minSizeBytes` and `maxSizeBytes` bound the object size.
- `maxObjects` caps the objects one run keeps.

Filtered-out objects are counted as skipped. A tracked object that is now filtered out keeps its link and is marked
Excluded.

The **schedule** is one of three types:

- `Manual`, the default.
- `Interval`, which runs every `intervalMinutes` (5 to 525600).
- `Cron`, which takes a five-field `cronExpression` evaluated in `timeZone` (IANA or Windows id; default UTC).

Cron times stay at the same local time across daylight saving changes. A disabled plan does not run on its schedule,
but a manual start still works.

## What a run does

1. The plan is claimed atomically in the database, so two servers never run it at once. A second start returns 409.
2. The crawler lists the source. If listing fails, the operation fails and nothing is deleted.
3. The listing is compared with the plan's crawl objects:
   - **Add**: a new object gets a link and an ingestion job (when `processAdditions` is on).
   - **Update**: an object whose version token changed is re-ingested into its existing link (when `processUpdates` is on).
   - **Retry**: an unchanged object whose last ingestion failed is re-ingested (when `retryFailedObjects` is on).
   - **Unchanged**: the object is counted and not recorded per object.
   - **Delete** or **Missing**: an object that is gone from the source has its link deleted when `processDeletions`
     is on. Otherwise it is marked Missing and its link is kept. Deletions are off by default.
4. If the deletions would exceed `maxDeletionFraction` (default 0.2) of the plan's links, they are **held**. The
   operation ends `Held` and nothing is deleted until someone calls
   `POST /v1.0/crawl-operations/{id}/confirm-deletions`.
5. The operation stays `Ingesting` until its jobs finish. It then ends `Succeeded`, `PartiallySucceeded` (some
   objects failed), `Failed`, `Cancelled`, or `Held`. The plan stays claimed until then, so the next run never
   overlaps.

Stopping a plan (`POST /v1.0/crawl-plans/{id}/stop`) cancels the listing if it is running. If the operation is
already waiting on ingestion, the stop cancels the jobs that have not started. If a server stops mid-run, its claim
lapses (`Crawling.ClaimMinutes`, default 30). Recovery then marks the operation Failed and releases the plan.
Finished operations older than the plan's `operationRetentionDays` (default 30) are pruned after each run.

Labels and tags on the plan are stamped on every link it creates, and from there on every chunk.

## Before you run a plan

- `POST /v1.0/crawl-plans/test` tests a draft's connection without saving it. Add `?fromPlanId=` to reuse a stored
  plan's secrets while editing it.
- `POST /v1.0/crawl-plans/{id}/test` tests a stored plan. Each step (settings, DNS, TCP, authentication, root
  access) reports what was checked and what to fix.
- `POST /v1.0/crawl-plans/{id}/preview` lists the source and shows what a run would add, update, retry, delete, and
  skip. It changes nothing.

## Deleting a plan

`DELETE /v1.0/crawl-plans/{id}` removes the plan with its settings, secrets, objects, and operations. You must stop a
running plan first (409 otherwise).

- With `?deleteLinks=true`, its links are deleted in the background.
- Without it, the links are kept and detached. A kept link with a web address becomes an ordinary URL link. A kept
  link from a bucket or share cannot be re-read without its plan.

Deleting a subject or tenant deletes its crawl plans.

## Permissions

Crawl plans use the `CrawlPlan` resource type and operations use `CrawlOperation`:

- Reads need Read.
- Create and replace need Write, and delete needs Delete.
- Test, preview, start, stop, and confirming deletions need Execute.

The built-in Editor role has all of these. Denials and admin bypasses are audited like every other route.

## Refreshing single links

A link submitted on its own (not by a crawl plan) can be kept current without a plan. Set its refresh interval
(0 off, or 60 to 525600 minutes) when submitting it, from the Links views, with `PUT /v1.0/links/{id}`, or with
the MCP tool `pneuma_set_link_refresh`. A link with no interval of its own follows the subject's
`defaultRefreshIntervalMinutes` (off by default). Each check is a conditional GET; only a changed page is
re-ingested. Checks go through the same fetch-safety policy as web crawls. See "Scheduled link refresh" in
`REST_API.md`.

The `LinkRefresh` section of `pneuma.json` (`linkRefresh` in camelCase configs):

| Setting | Default | Meaning |
|---------|---------|---------|
| `Enabled` | true | Check due links on this server ("check now" works either way). |
| `IntervalSeconds` | 60 | Seconds between passes. |
| `BatchSize` | 50 | Most links checked per pass. |

## Server settings

The `Crawling` section of `pneuma.json` (`crawling` in camelCase configs):

| Setting | Default | Meaning |
|---------|---------|---------|
| `SchedulerEnabled` | true | Run scheduled plans on this server (manual starts work either way). |
| `SchedulerIntervalSeconds` | 15 | Seconds between scheduler passes. |
| `MaxConcurrentOperations` | 2 | Operations this server lists and dispatches at once. |
| `ClaimMinutes` | 30 | How long a claim lasts without renewal. |
| `AllowedLocalRoots` | empty | Folders local folder plans may read. Empty turns local folder plans off. |

## Observability

The server exports these metrics:

- `pneuma_crawl_operations_total{type,outcome}`
- `pneuma_crawl_objects_total{type,action}`
- `pneuma_crawl_operation_duration_seconds{type}`
- `pneuma_crawl_bytes_total{type}`
- `pneuma_crawl_running`
- `pneuma_link_refresh_total{outcome}` (single-link refresh checks)

Each operation has a root span `crawl <type>` with `stage:Enumerate` and `stage:Dispatch` children. See
`TELEMETRY.md` and the "Pneuma Crawling" Grafana dashboard.

## API, SDKs, and MCP

The routes are in `REST_API.md` under "Crawl plans". The C#, JavaScript, and Python SDKs have a method per route.
MCP has `pneuma_enumerate_crawl_plans`, `pneuma_get_crawl_plan`, `pneuma_create_crawl_plan`,
`pneuma_update_crawl_plan`, `pneuma_test_crawl_plan`, `pneuma_preview_crawl_plan`, `pneuma_start_crawl_plan`,
`pneuma_stop_crawl_plan`, `pneuma_enumerate_crawl_operations`, and `pneuma_get_crawl_operation` (see `MCP_API.md`).
The Postman collection has "Crawl plans" and "Crawl operations" folders.
