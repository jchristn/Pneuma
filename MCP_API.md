# Pneuma MCP API

Pneuma exposes a Model Context Protocol (MCP) endpoint so agents can drive the platform with the same credentials, permissions, and audit trail as the REST API. The endpoint is served **in-process by the Pneuma host** — there is no separate MCP process, no second auth model, and no separate port. It speaks JSON-RPC 2.0 over `POST /mcp`.

## Authentication

MCP calls authenticate exactly like REST calls. Present a bearer session token or an `access_`/`secret_` API-key credential in the standard headers; the request passes through Pneuma's `AuthenticateRequest` hook, and each tool authorizes against `AuthorizationService` for the resource and operation it touches. A call without a valid principal is rejected with `401` before any tool runs, and denials and system-admin bypasses are audited the same way REST denials are.

## Methods

The endpoint implements the core MCP method set:

- `initialize` — returns the protocol version, server info, and the `tools` capability.
- `tools/list` — returns the available tools with their JSON-Schema input contracts.
- `tools/call` — invokes a tool by name with an `arguments` object.
- `ping` / `notifications/initialized` — liveness and the post-initialize notification.

A `tools/call` result wraps the tool's output as MCP content: `{ "content": [ { "type": "text", "text": "<JSON>" } ], "isError": false }`, where the text is the tool's JSON payload.

## Enumerating objects

This is the one rule an agent must internalize before reading data: **Pneuma never returns an unbounded collection.** There is no "list everything" tool. Every collection is retrieved through paired tools — an `pneuma_enumerate_*` tool that returns small summaries a page at a time, and an `pneuma_get_*` tool that returns one full object by id.

An enumeration tool takes an `EnumerationQuery` — `maxResults`, `skip`, `order` (`asc`/`desc`), and an optional `search` — and returns an `EnumerationResult`:

```json
{
  "success": true,
  "maxResults": 50,
  "skip": 0,
  "totalRecords": 214,
  "recordsRemaining": 164,
  "endOfResults": false,
  "objects": [ { "id": "sub_...", "displayName": "...", "type": "...", "active": true } ]
}
```

To read a full collection, page it to completion:

1. Call the enumerate tool with `skip: 0`. The **first** result's `totalRecords` is the exact number of records that exist — read it once and you know how far you have to go.
2. Advance `skip` by your page size and call again.
3. Stop when `endOfResults` is `true` (equivalently, when `recordsRemaining` reaches `0`).

The objects in an enumeration are deliberately small — an id, a name, a type, a status flag. They are enough to decide what you want, not to work with. When you need the whole object — a subject's full record, a node's content, a job's log, the raw source bytes, atoms, chunks, vectors, or a subgraph — fetch it individually with the matching `pneuma_get_*` tool. Keeping enumeration payloads small is what lets an agent scan thousands of records without drowning a context window.

A worked example, reading every subject in pages of 50:

```
tools/call pneuma_enumerate_subjects { "maxResults": 50, "skip": 0 }   → totalRecords 214, endOfResults false
tools/call pneuma_enumerate_subjects { "maxResults": 50, "skip": 50 }  → endOfResults false
tools/call pneuma_enumerate_subjects { "maxResults": 50, "skip": 100 } → endOfResults false
tools/call pneuma_enumerate_subjects { "maxResults": 50, "skip": 150 } → endOfResults false
tools/call pneuma_enumerate_subjects { "maxResults": 50, "skip": 200 } → endOfResults true
tools/call pneuma_get_subject { "id": "sub_abc123" }                   → the full subject
```

Every `pneuma_enumerate_*` tool follows this exact protocol — `pneuma_enumerate_subjects`, `pneuma_enumerate_jobs`, `pneuma_enumerate_links`, `pneuma_enumerate_threads`, `pneuma_enumerate_feedback`, `pneuma_enumerate_eval_runs`, `pneuma_enumerate_eval_facts`, `pneuma_enumerate_request_history`, and `pneuma_enumerate_model_runner_health` all take `maxResults`/`skip`/`order`, return the `EnumerationResult` envelope (`totalRecords`/`recordsRemaining`/`endOfResults`) with small summaries, and pair with a matching `pneuma_get_*` tool for the full object. Start at `skip: 0` and advance `skip` by your page size until `endOfResults` is `true`.

## Tools

The current tool set is small and growing; `pneuma_capabilities` and `tools/list` are always the source of truth for what is available.

| Tool | Purpose | Auth |
|------|---------|------|
| `pneuma_capabilities` | Describe the platform and restate the paging protocol. | Any authenticated principal |
| `pneuma_enumerate_subjects` | Page subject summaries (`EnumerationQuery` in, `EnumerationResult` out). | Subject / Read |
| `pneuma_get_subject` | Fetch one full subject by id. | Subject / Read |
| `pneuma_create_subject` | Create a subject (`displayName` required; `type`, `description`, `tagline`, `urlSlug`, `thinkingEnabled`, `systemPrompt`, `ontologyClassifyPrompt`, `ontologyDefinitionPrompt`, `historyRetentionDays`, `embeddingModel`, `inferenceModel`, `collection`, `rerankingModel`, `promptRewriteModel`, `rerankingPrompt`, `promptRewritePrompt` optional). `embeddingModel`/`inferenceModel`/`collection` are required before the subject can ingest or answer; `rerankingModel`/`promptRewriteModel` are optional (steps skipped when null). Prompt fields default sensibly. A unique slug is auto-generated when omitted; an explicit slug clash is rejected. | Subject / Create |
| `pneuma_update_subject` | Update a subject (`id` required; only supplied fields change — including the models, collection, and rerank/rewrite prompts; a changed `urlSlug` must stay unique). | Subject / Update |
| `pneuma_enumerate_jobs` | Page ingestion-job summaries (id, status, stage, source url). | IngestionJob / Read |
| `pneuma_get_job` | Fetch one full ingestion job by id. | IngestionJob / Read |
| `pneuma_ingestion_summary` | Summarize ingestion activity over time, broken down by pipeline stage: fixed-width time buckets with per-stage event counts plus overall per-stage totals. Optional `subjectId`, `fromUtc`/`toUtc` window, and `bucketMinutes` (1–1440, default 15). | IngestionJob / Read |
| `pneuma_enumerate_links` | Page content-link summaries (id, url, title, status). | Subject / Read |
| `pneuma_get_link` | Fetch one full content link by id. | Subject / Read |
| `pneuma_search` | Full-text search the corpus (RecallDB) against the resolved default collection; bounded, ranked node summaries (a top-N query, not an enumeration). Accepts an optional `metadataFilter` to scope retrieval to specific labels/tags. | GraphNode / Read |
| `pneuma_get_node` | Fetch one full knowledge-graph node by id. | GraphNode / Read |
| `pneuma_get_neighbors` | Fetch a node's adjacent nodes as a bounded set of summaries. | GraphNode / Read |
| `pneuma_query` | Ask a grounded question; returns a cited answer, supporting sources, and an `insufficientSupport` flag. Accepts an optional `metadataFilter` to scope retrieval to specific labels/tags. | GraphNode / Read |
| `pneuma_get_history_turn` | Fetch one chat turn with its feedback, tool-call trace, and per-stage performance telemetry (`id` required). | Subject / Read |
| `pneuma_enumerate_threads` | Enumerate conversation thread summaries (paged; `EnumerationResult` out), optional `subjectId`. Use `pneuma_get_thread` for a thread's full turns. | Subject / Read |
| `pneuma_get_thread` | Fetch one conversation thread with its ordered turns (`id` required). | Subject / Read |
| `pneuma_delete_thread` | Delete a conversation thread and cascade its turns + tool-call trace (`id` required). Irreversible. | Subject / Delete |
| `pneuma_enumerate_feedback` | Enumerate chat-feedback summaries (paged; `EnumerationResult` out), optional `subjectId`. | Subject / Read |
| `pneuma_analytics` | Per-subject chat analytics over a window (`subjectId?`, `days?` default 30): volume, latency percentiles, per-stage timing, feedback. | Subject / Read |
| `pneuma_enumerate_eval_runs` | Enumerate RAG evaluation-run summaries (paged; `EnumerationResult` out), optional `subjectId`. Use `pneuma_get_eval_run` for a run's full results. | Subject / Read |
| `pneuma_get_eval_run` | Fetch one evaluation run with its per-fact results (`id` required). | Subject / Read |
| `pneuma_enumerate_eval_facts` | Enumerate a subject's ground-truth evaluation facts (paged; `EnumerationResult` out; `subjectId` required). | Subject / Read |
| `pneuma_create_eval_fact` | Create a ground-truth fact (`subjectId`, `question`, `expectedAnswer` required; `category?`). | Subject / Update |
| `pneuma_delete_eval_fact` | Delete an evaluation fact by id (`id` required). | Subject / Delete |
| `pneuma_start_eval_run` | Queue an evaluation run for a subject (`subjectId` required; `category?`). Returns the `Pending` run immediately; poll `pneuma_get_eval_run` for progress. | Subject / Update |
| `pneuma_cancel_eval_run` | Cancel a queued or running evaluation run (`id` required). Idempotent on a finished run. | Subject / Update |
| `pneuma_delete_eval_run` | Delete an evaluation run and its per-fact results (`id` required). Irreversible. | Subject / Delete |
| `pneuma_distinct_labels` | Return a subject's distinct retrieval labels (`subjectId` required) — a bounded aggregate for building a `metadataFilter`, not an enumeration. | Subject / Read |
| `pneuma_distinct_tags` | Return a subject's distinct retrieval tag keys and values (`subjectId` required) — a bounded aggregate for building a `metadataFilter`, not an enumeration. | Subject / Read |
| `pneuma_enumerate_request_history` | Enumerate captured request-history summaries (paged; `EnumerationResult` out). Optional `tenantId`, `userId`, `method`, `pathContains`, `statusCode` filters. Use `pneuma_get_request_history` for a full entry. | System administrator only |
| `pneuma_get_request_history` | Fetch one captured request-history entry with full detail (`id` required; secrets redacted at capture). | System administrator only |
| `pneuma_request_history_summary` | Summarize request history over an optional filter (`tenantId?`, `method?`, `pathContains?`, `statusCode?`): totals, status-code breakdown, latency. | System administrator only |
| `pneuma_get_settings` | Return the server settings with every secret field redacted. | System administrator only |
| `pneuma_enumerate_model_runner_health` | Enumerate model-endpoint health summaries (embedding + completion; paged; `EnumerationResult` out). | ModelRunner / Read |
| `pneuma_get_model_runner_health` | Fetch the health of one model endpoint by id (uptime, latency, last status; `id` required). | ModelRunner / Read |

Further tools (ingest a link) follow the same contract as they land. The grounded-answer logic is shared with the REST `/v1.0/query` endpoint, so the two surfaces cannot drift.

## Retrieval scoping (`metadataFilter`)

`pneuma_search` and `pneuma_query` accept an optional `metadataFilter` object that narrows retrieval to chunks carrying specific labels and tags:

```json
{
  "requiredLabels": ["string"],
  "excludedLabels": ["string"],
  "requiredTags": [ { "key": "string", "condition": "Equals", "value": "string" } ],
  "excludedTags": [ { "key": "string", "condition": "Contains", "value": "string" } ]
}
```

A chunk is eligible only when it carries **every** required label and satisfies **every** required tag condition, and **none** of the excluded labels or tag conditions match. `condition` is one of `Equals`, `NotEquals`, `Contains`, `StartsWith`, `EndsWith`, `GreaterThan`, `LessThan`, `IsNull`, `IsNotNull` (`value` is ignored for `IsNull`/`IsNotNull`). Discover the valid label and tag values for a subject with `pneuma_distinct_labels` and `pneuma_distinct_tags` before building a filter.

## Streaming

`pneuma_query` accepts `stream: true`. When set, the `tools/call` responds as a **Server-Sent Events** stream (Streamable-HTTP style): `metadata` and `delta` events carry the answer as it is generated, and the **final** SSE event is the JSON-RPC result message. Without `stream`, the same call returns a single JSON response.

## Transport note

MCP shares the Pneuma host and its port; it does not run as a separate container or on a distinct port. Point your MCP client at the Pneuma base URL with the path `/mcp`.
