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

## Tools

The current tool set is small and growing; `pneuma_capabilities` and `tools/list` are always the source of truth for what is available.

| Tool | Purpose | Auth |
|------|---------|------|
| `pneuma_capabilities` | Describe the platform and restate the paging protocol. | Any authenticated principal |
| `pneuma_enumerate_subjects` | Page subject summaries (`EnumerationQuery` in, `EnumerationResult` out). | Subject / Read |
| `pneuma_get_subject` | Fetch one full subject by id. | Subject / Read |
| `pneuma_create_subject` | Create a subject (`displayName` required; `type`, `description`, `tagline`, `urlSlug`, `thinkingEnabled`, `systemPrompt`, `ontologyClassifyPrompt`, `ontologyDefinitionPrompt`, `historyRetentionDays` optional). `tagline` is the ask-page subtitle shown in the user dashboard and defaults to the built-in label when omitted. A unique slug is auto-generated when omitted; an explicit slug clash is rejected. | Subject / Create |
| `pneuma_update_subject` | Update a subject (`id` required; only supplied fields change — including `tagline`; a changed `urlSlug` must stay unique). | Subject / Update |
| `pneuma_enumerate_jobs` | Page ingestion-job summaries (id, status, stage, source url). | IngestionJob / Read |
| `pneuma_get_job` | Fetch one full ingestion job by id. | IngestionJob / Read |
| `pneuma_ingestion_summary` | Summarize ingestion activity over time, broken down by pipeline stage: fixed-width time buckets with per-stage event counts plus overall per-stage totals. Optional `subjectId`, `fromUtc`/`toUtc` window, and `bucketMinutes` (1–1440, default 15). | IngestionJob / Read |
| `pneuma_enumerate_links` | Page content-link summaries (id, url, title, status). | Subject / Read |
| `pneuma_get_link` | Fetch one full content link by id. | Subject / Read |
| `pneuma_search` | Full-text search the corpus (RecallDB) against the resolved default collection; bounded, ranked node summaries (a top-N query, not an enumeration). | GraphNode / Read |
| `pneuma_get_node` | Fetch one full knowledge-graph node by id. | GraphNode / Read |
| `pneuma_get_neighbors` | Fetch a node's adjacent nodes as a bounded set of summaries. | GraphNode / Read |
| `pneuma_query` | Ask a grounded question; returns a cited answer, supporting sources, and an `insufficientSupport` flag. | GraphNode / Read |

Further tools (ingest a link) follow the same contract as they land. The grounded-answer logic is shared with the REST `/v1.0/query` endpoint, so the two surfaces cannot drift.

## Streaming

`pneuma_query` accepts `stream: true`. When set, the `tools/call` responds as a **Server-Sent Events** stream (Streamable-HTTP style): `metadata` and `delta` events carry the answer as it is generated, and the **final** SSE event is the JSON-RPC result message. Without `stream`, the same call returns a single JSON response.

## Transport note

MCP shares the Pneuma host and its port; it does not run as a separate container or on a distinct port. Point your MCP client at the Pneuma base URL with the path `/mcp`.
