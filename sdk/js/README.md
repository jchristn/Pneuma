# Pneuma JavaScript SDK

A dependency-free JavaScript SDK for the [Pneuma REST API](../../REST_API.md).

Built on the Node.js built-in `fetch` — no runtime dependencies. Ships as an ES
module.

## Requirements

- Node.js 18.0.0 or higher (native `fetch`, ES modules)

## Install

This package has no runtime dependencies. Copy the `sdk/js` directory into your
project, or reference it locally:

```bash
npm install /path/to/Pneuma/sdk/js
```

Then import it:

```js
import { PneumaClient, PneumaError } from 'pneuma-sdk';
```

Or import directly from source without installing:

```js
import { PneumaClient, PneumaError } from './sdk/js/src/index.js';
```

## Usage

```js
import { PneumaClient, PneumaError } from 'pneuma-sdk';

// Loopback host names are normalized to 127.0.0.1 automatically.
const client = new PneumaClient('http://127.0.0.1:8080');

// Log in — the token is stored on the client and sent on subsequent calls.
await client.login('admin@pneuma', 'password');

// Health check (no auth required).
const health = await client.health();
console.log(health.status);

// Work with subjects and links.
const subject = await client.createSubject({
    displayName: 'Jane Doe',
    type: 'Person',
    description: 'Singer-songwriter'
});

// Pick ingestion endpoints (embedding + completion) to process the link.
const endpoints = await client.listIngestionEndpoints();
const embeddingEndpointId = endpoints.embedding[0].id;
const completionEndpointId = endpoints.completion[0].id;

await client.submitLink(subject.id, {
    url: 'https://example.com/article',
    title: 'Overview article',
    embeddingEndpointId,
    completionEndpointId,
    // Optional operator-supplied metadata that rides along with every chunk.
    labels: ['live', '1965'],
    tags: { source: 'official', rights: 'cleared' }
});

// Or submit several URLs at once.
const bulk = await client.submitLinks(subject.id, {
    urls: ['https://example.com/a', 'https://example.com/b'],
    embeddingEndpointId,
    completionEndpointId
});
console.log(`created ${bulk.created} links`);

// List endpoints return a paginated envelope — read `.objects` for the records.
const page = await client.listSubjects({ maxResults: 50, skip: 0, order: 'desc', search: 'jane' });
console.log(page.totalRecords, page.objects.length);
for (const c of page.objects) {
    console.log(c.displayName);
}

// Ingestion jobs (status filter first, then pagination options).
const jobs = await client.listJobs('Failed', { maxResults: 25 });
console.log(jobs.objects);

// Search and grounded ask.
const results = await client.search('acoustic guitar', 20);
const answer = await client.query({ question: 'Who plays guitar?', maxResults: 5 });
console.log(answer.answer);

// Grounded ask scoped to one subject and filtered by ingestion labels/tags.
const scoped = await client.query({
    question: 'What themes recur across the live recordings?',
    maxResults: 10,
    subjectId: subject.id,
    metadataFilter: {
        requiredLabels: ['live'],
        excludedLabels: ['bootleg'],
        requiredTags: [{ key: 'rights', condition: 'Equals', value: 'cleared' }]
    }
});
console.log(scoped.answer);
```

### Pagination

Every list (GET-all) method returns an `EnumerationResult` envelope rather than a
bare array:

```json
{
  "success": true,
  "maxResults": 100,
  "skip": 0,
  "totalRecords": 42,
  "recordsRemaining": 0,
  "endOfResults": true,
  "objects": [ /* records */ ]
}
```

The records live in `.objects`; `.totalRecords`, `.recordsRemaining`, and
`.endOfResults` support paging. Each list method accepts an optional options
object `{ maxResults, skip, order, search }`:

- `maxResults` — page size (default 100, clamped 1..1000 server-side)
- `skip` — number of records to skip (default 0)
- `order` — `'asc'` or `'desc'` (default `'desc'`)
- `search` — substring filter

Methods that also take a scope argument (e.g. `listUsers(tenantId, options)`,
`listAssignments(userId, options)`, `listJobs(status, options)`,
`listAudit(tenantId, options)`, `listSubjectLinks(subjectId, options)`) accept
it before the options object.

Request-history methods (`listRequestHistory`, `requestHistorySummary`) keep
their own paging shape, and graph `getNeighbors`/`getEdges` still return bare
arrays.

### Settings (system-admin only)

```js
// Read settings — secret fields come back masked as "********".
const { settings, meta } = await client.getSettings();
console.log(meta.sections, meta.secretFields);

// Update settings. Leaving a secret field as "********" preserves the stored value.
const result = await client.updateSettings({ ...settings, someFlag: true });
if (result.restartRequired) {
    console.log('Restart required:', result.message);
}
```

### Error handling

Any non-2xx response throws an `PneumaError` carrying the HTTP `status` and the
parsed response `body`:

```js
try {
    await client.getSubject('does-not-exist');
} catch (err) {
    if (err instanceof PneumaError) {
        console.error(err.status, err.body);
    } else {
        throw err;
    }
}
```

Successful responses return the parsed JSON body, or `null` for `204 No Content`.

## Constructor

- `new PneumaClient(baseUrl = 'http://127.0.0.1:8080', token = null)`
  - `baseUrl` — server base URL; `localhost` is rewritten to `127.0.0.1`.
  - `token` — optional pre-existing session token.

## Methods

### System
- `health()`

### Tokens / auth
- `login(email, password, tenantId?)` — POSTs `/v1.0/token`, stores the token
- `validateToken()`
- `tokenDetails()`
- `logout()` — revokes and clears the stored token

List methods (below) return an `EnumerationResult` envelope — read `.objects`.
See [Pagination](#pagination). Each accepts an optional `{ maxResults, skip,
order, search }` options object as its last argument.

### Tenants
- `listTenants(options?)`, `createTenant(tenant)`, `getTenant(id)`, `updateTenant(id, tenant)`, `deleteTenant(id)`

### Users
- `listUsers(tenantId?, options?)`, `createUser(user)`, `getUser(id)`, `updateUser(id, user)`, `deleteUser(id)`

### Credentials
- `listCredentials(options?)`, `createCredential(credential)`, `getCredential(id)`, `deleteCredential(id)`

### Roles / Permissions / Assignments (RBAC)
- `listRoles(options?)`, `createRole(role)`, `getRole(id)`, `updateRole(id, role)`, `deleteRole(id)`
- `listPermissions(options?)`, `createPermission(p)`, `getPermission(id)`, `updatePermission(id, p)`, `deletePermission(id)`
- `listAssignments(userId, options?)`, `createAssignment(a)`, `deleteAssignment(id)`

### Audit
- `listAudit(tenantId?, options?)`

### Subjects
- `listSubjects(options?)`, `createSubject(subject)`, `getSubject(id)`, `updateSubject(id, subject)`, `deleteSubject(id)`

### Content links & ingestion
- `submitLink(subjectId, { url, title?, labels?, tags? })`
  - `labels` — optional array of plain strings attached to every chunk this link produces
  - `tags` — optional `{ key: value }` map attached to every chunk this link produces
  - both round-trip on the returned link and can later scope retrieval (see `query`)
- `submitLinks(subjectId, { urls, labels?, tags? })` — `labels`/`tags` apply to every URL in the batch
- `listIngestionEndpoints()`
- `listSubjectLinks(subjectId, options?)`
- `listLinks(options?)`, `getLink(id)`, `deleteLink(id)`

### Jobs
- `listJobs(status?, options?)`, `getJob(id)`, `restartJob(id)`

### Model runners
- `listModelRunners(options?)`, `createModelRunner(runner)`, `getModelRunner(id)`, `updateModelRunner(id, runner)`, `deleteModelRunner(id)`

### Prompts
- `listPrompts(options?)`, `createPrompt(p)`, `getPrompt(id)`, `updatePrompt(id, p)`, `deletePrompt(id)`

### Settings (system-admin only)
- `getSettings()` — GET `/v1.0/settings`; secrets masked as `"********"`
- `updateSettings(settings)` — PUT `/v1.0/settings`; unchanged masked secrets are preserved

### Request history
- `listRequestHistory(filters?)`, `requestHistorySummary(params?)`, `getRequestHistory(id)`, `deleteRequestHistory(id)`, `deleteRequestHistoryBulk(filters?)`

### Knowledge graph
- `getNode(id)`, `getNeighbors(id)`, `getEdges(id)`

### Search & ask
- `search(q, max = 20)`
- `query({ question, maxResults?, subjectId?, metadataFilter? })`
  - `subjectId` — optional; scopes retrieval to a single subject
  - `metadataFilter` — optional facet filter restricting retrieval to documents ingested with
    matching labels/tags, of shape `{ requiredLabels?, excludedLabels?, requiredTags?, excludedTags? }`.
    Each tag entry is `{ key, condition, value }` where `condition` is one of `Equals` (default),
    `NotEquals`, `Contains`, `StartsWith`, `EndsWith`, `GreaterThan`, `LessThan`, `IsNull`,
    `IsNotNull` (`value` is ignored for `IsNull`/`IsNotNull`). A chunk is eligible only when it
    carries every required label and satisfies every required tag condition, and no excluded label
    or tag condition matches. Merged with the subject's default filter — it narrows, never widens.

## Test harness

The harness runs a live smoke test against a server. It prints `PASS`/`FAIL`
per step and exits non-zero on failure. If the server is unreachable it prints
`SKIPPED` and exits 0.

```bash
npm test
# or
node test/harness.mjs [baseUrl] [email] [password]
```

Defaults: `http://127.0.0.1:8080`, `admin@pneuma`, `password`.

## License

MIT
