# Ontologies

An ontology tells Pneuma what a subject's knowledge graph may contain: the kinds of entities (node types), the kinds
of relationships between them (edge types), the rules those elements must follow, and a taxonomy of concepts that
content is tagged with. Ontologies are governed: they are versioned, a version is approved before any subject uses it,
and an approved version never changes. This document covers the concepts, where each setting lives and why, the
permissions, and the operations. The REST contract is in [`REST_API.md`](REST_API.md#ontologies), the MCP tools in
[`MCP_API.md`](MCP_API.md), and the metrics in [`TELEMETRY.md`](TELEMETRY.md).

A subject that does not pin an ontology version behaves exactly as before: ingestion classifies content using the
`ontology.definition` prompt.

## Concepts

**Ontology.** A named container for versions, owned by a tenant.

**Version.** Numbered from 1. A version is a `Draft` (editable), `Approved` (immutable, can be pinned), or `Retired`
(immutable, can no longer be pinned). A version holds:

| Part | What it is |
|---|---|
| Node types | `{ name, description }`, for example `Person`, `Organization`. |
| Edge types | `{ name, description }`, for example `WORKS_FOR`. |
| Rules | Constraint rules; see [Rules](#rules). |
| Concepts | The taxonomy; see [Taxonomy](#taxonomy). |
| Guidance | Free text added to the definition the classifier sees. |
| Undeclared type action | What happens to an element whose type the version does not declare: `Allow` (default), `Warn`, `Drop`, or `Quarantine`. |
| Change summary | What changed and why; shown in the version list and the audit log. |

A new ontology starts with a draft that is empty, copied from a built-in template (`GET /v1.0/ontology-templates`;
`Default` holds Pneuma's built-in node and edge types), or copied from another version. A new draft of an existing
ontology copies a version (by default the newest).

**Approval.** Approving a draft checks it: names are present and unique (compared without case, spaces, or
punctuation), rules reference declared types and have the fields their type needs, patterns compile, and concept
keys are unique with any broader concept present. Every problem is listed on the version (`problems`) and in the
`400` response to an approval that fails. Saving a draft with problems is allowed; approving it is not. Contents
that can never be stored (over the size limits, duplicate rule ids) are refused on save.

**Pinning.** A subject classifies into at most one approved version (`PUT /v1.0/subjects/{id}/ontology`). Pinning
replaces the `ontology.definition` prompt for that subject with the version's rendered definition: its node and edge
types with their descriptions, its endpoint rules, and its guidance. The rendered text is always visible
(`GET /v1.0/subjects/{id}/ontology`, `GET /v1.0/ontology-versions/{id}/definition`). Pinning is audited. When the
taxonomy changes with the pin, a `Retag` operation is queued (unless `retag` is `false`). A pinned version cannot be
retired, and an ontology with a pinned version cannot be deleted.

## Where each setting lives

Every setting, prompt, and rule has one scope. Anything above subject scope has a reason.

| Thing | Scope | Why |
|---|---|---|
| Ontologies, versions, node and edge types, rules, concepts | Tenant | An ontology is an organizational asset: one approved definition reused by many subjects is the point of governance, and approval is a tenant act performed by tenant roles. Tenants never see each other's ontologies. |
| The pinned version | Subject | Each subject chooses its model. |
| Classification temperature (`classificationTemperature`, 0 to 2, default 0) | Subject | It shapes one subject's ingestion, like its chunk settings. One tenant can run audited subjects at 0 and exploratory subjects higher. |
| Classification cache switch (`classificationCacheEnabled`, default on) | Subject | Same reason. |
| Classification cache entries | Tenant | An entry is keyed by the whole request (see [Reproducible classification](#reproducible-classification)), so identical input in two subjects of one tenant gives the same answer and can share an entry. Entries never cross tenants and are deleted with the tenant. |
| Built-in templates | System, read-only | Product content in code. A tenant copies a template into its own draft; nobody edits a shared record. |
| Prompts | System default → tenant copy → subject override | See [Prompts](#prompts). |
| Violations, operations, and operation items | Subject | They describe one subject's graph. |
| Resource limits (the `Ontology` section of `pneuma.json`) | System | They protect memory, model capacity, and database space that all tenants share, so a tenant cannot raise them. |

The `Ontology` settings, with their defaults:

| Setting | Default | Meaning |
|---|---|---|
| `WorkerEnabled` | `true` | Run the background worker that performs ontology operations. |
| `WorkerPollIntervalMs` | `2000` | How often the worker looks for queued operations. |
| `MaxGraphNodes` | `100000` | The most nodes a graph export, validation, or re-tag reads. A truncated export carries the `X-Pneuma-Truncated: true` header; a truncated operation says so in its `error`. |
| `MaxViolationsPerJob` | `500` | The most violations stored per ingestion job (the rest are counted, not stored). |
| `MaxProposalSampleCells` | `50` | The most passages shown to the model when proposing a draft. |
| `MaxDriftSampleSize` | `25` | The most passages a drift check classifies (twice each). |
| `CacheRetentionDays` | `90` | Cache entries unused for this long are pruned. |

## Permissions

The `Ontology` resource type covers ontology and version routes.

| Operation | Allows |
|---|---|
| Read | List and read ontologies and versions, templates, diffs, rendered definitions, and version exports. |
| Write | Create and edit ontologies and drafts, start a draft from a version, propose a draft, import a taxonomy. |
| Delete | Delete a draft, or an ontology none of whose versions is pinned. |
| Execute | Approve and retire versions. |

Authoring and approval are separate so they can be given to different people. The built-in Editor role has Ontology
Read and Write (which includes Delete) but not Execute; the Viewer role has Read. Tenant administrators and system administrators can approve
(they bypass role checks, and the bypass is audited). A custom role can grant Ontology Execute. Existing tenants'
built-in roles receive the new permissions on the next start.

Subject-facing routes use the Subject resource. Reading a subject's ontology, violations, and operations, and
exporting its graph, need Subject Read. Pinning (which also needs Ontology Read), releasing or dismissing a
violation, starting an operation, and clearing the classification cache need Subject Update. Approvals, retirements,
and pin changes are written to the audit log (`OntologyGovernance`).

## Prompts

Every prompt the ontology features send to a model is a keyed prompt you can read and change on the Prompts page or
through `/v1.0/prompts`:

| Key | Used by | Purpose |
|---|---|---|
| `ontology.classify` | Ingestion | The classifier's task. |
| `ontology.definition` | Ingestion | The definition used when no version is pinned. |
| `ontology.classify.format` | Ingestion | The JSON the classifier must return. The parser expects this shape; a change that breaks it fails the batch with a warning on the job. |
| `taxonomy.hint` | Ingestion | The sentence that introduces taxonomy matches to the classifier. |
| `ontology.propose` | Propose | The authoring assistant's task. |
| `ontology.propose.format` | Propose | The JSON the authoring assistant must return. |

Each key resolves in three layers. The **system default** is seeded on first start so every feature works out of the
box. A **tenant copy** overrides it for every subject in the tenant: editing a system default as a tenant user saves
a tenant copy, and deleting the copy returns the tenant to the system default. Only a system administrator changes
the system default itself (they can save a tenant copy instead with `?scope=tenant`). A **subject override**
(`/v1.0/subjects/{id}/prompts/{key}`) appends to or replaces the tenant's prompt for one subject. `GET /v1.0/prompts`
lists the prompt in effect for each key, with `isSystemDefault` telling the two apart.

## Rules

Each rule has a type, the fields that type uses, an action, and an optional description.

| Type | Fields | Checks |
|---|---|---|
| `EdgeEndpoints` | `edgeType`, `fromNodeType`, `toNodeType` | An edge of this type connects only these node types. Several rules for one edge type allow several pairs. |
| `MaxOutgoing` | `nodeType`, `edgeType`, `maxCount` | A node of this type has at most this many outgoing edges of this type. |
| `RequiredField` | `nodeType`, `field` (`Content`, `Rights`, `Authority`, `CanonicalName`) | A node of this type has this field. |
| `NamePattern` | `nodeType`, `pattern` | A node of this type has a name matching this regular expression (checked with a timeout). |
| `MinConfidence` | optional `nodeType` or `edgeType` | An element's model confidence is at least `minConfidence`. |

| Action | Effect |
|---|---|
| `Warn` | Keep the element and record a violation. |
| `Drop` | Leave the element out of the graph and record a violation. |
| `Quarantine` | Hold the element out of the graph until someone releases it (it is then added) or dismisses it. |
| `Reverse` | `EdgeEndpoints` only: an edge whose endpoints match the rule backwards is flipped; any other mismatch is dropped. |

Rules run during ingestion against each document's candidate elements, after types are matched to the declared
types. Cardinality across documents can only be seen in the stored graph, so the `Validate` operation checks the
whole subject graph and records what it finds (it does not change the graph). Each job records its violation count
in `completeness.ontologyViolations`. Violations are listed with `GET /v1.0/subjects/{id}/ontology-violations`
(filter by `status`, `jobId`, or `operationId`).

## Taxonomy

A concept has a key (defaults to the preferred label), a preferred label, alternative labels, an optional broader
concept, an optional definition, the node type it becomes (default `Topic`), and a case-sensitivity switch.

Tagging is deterministic and uses no model. During ingestion every passage is matched against the pinned version's
labels on word boundaries, longest label first (case-insensitive unless the concept says otherwise). A match links
the passage to the concept's node with an `ABOUT` edge tagged `assertedBy=taxonomy` and the concept key, and links
each concept to its broader concept with a `BROADER` edge. The matches are also given to the classifier through the
`taxonomy.hint` prompt, so the model names those entities consistently. Each job records its match count in
`completeness.taxonomyMatches`. The `Retag` operation re-applies the pinned taxonomy to existing content, adding the
links that are missing and removing taxonomy links that no longer apply.

**Importing SKOS.** `POST /v1.0/ontology-versions/{id}/taxonomy/import` reads a SKOS concept scheme (Turtle or
JSON-LD) into a draft. `skos:prefLabel`, `skos:altLabel`, `skos:broader`, and `skos:definition` are read; the key is
`skos:notation`, else the concept IRI. `mode=merge` adds and updates concepts by key; `mode=replace` also removes
concepts that are not in the document. Remote JSON-LD contexts are never fetched.

## Reproducible classification

Three things make a subject's graph repeatable:

1. **Temperature.** Classification uses the subject's `classificationTemperature` (default 0).
2. **Cache.** With `classificationCacheEnabled` on, each classification call is stored under a SHA-256 of the model
   runner, the model, the temperature, the full system prompt (task, definition, output format), and the full user
   prompt (subject name, passages, taxonomy hints). Re-ingesting unchanged content reuses the stored answer; changing
   any input (a prompt, a pinned version, the model) misses. Each job records its hits in
   `completeness.classificationCacheHits`. `DELETE /v1.0/subjects/{id}/classification-cache` removes the entries a
   subject stored.
3. **Provenance.** Each job's classification provenance records the ontology version (or that the prompt definition
   was used), the prompt hashes, the model, the temperature, and the cache hits.

The `DriftCheck` operation classifies each of a sample of stored passages twice, bypassing the cache, with the
subject's current prompts, model, and temperature, and reports the share whose result changed (`driftRate`) and each
difference. It costs two model calls per passage.

## Operations

Operations run in the background on a subject and survive page reloads; one of each kind can be queued or running
per subject at a time (`409` otherwise).

| Kind | Needs | Does |
|---|---|---|
| `Validate` | A pinned version | Checks the subject's stored graph against the version's rules and records violations. |
| `Retag` | — | Re-applies the pinned taxonomy (or removes taxonomy links when none is pinned). Reports links `added` and `removed`. |
| `DriftCheck` | — | Classifies `sampleSize` passages (1 to `MaxDriftSampleSize`) twice each and reports `driftRate`. |

`GET /v1.0/ontology-operations/{id}` returns the operation and its items (one per node or passage examined). An
operation left running by a stopped server is marked failed when the server starts.

## Proposing a draft

`POST /v1.0/ontologies/{id}/propose` has the model propose node types, edge types, endpoint rules, and guidance from
a sample of a subject's content, pasted sample text, or both. The proposal is saved as a new draft, extending the
version named in `basedOnVersionId` (by default the newest); nothing is approved automatically. The task and output
format are the `ontology.propose` and `ontology.propose.format` prompts. The model is `modelRunnerId`, else the
subject's inference model.

## Export

| Route | Formats |
|---|---|
| `GET /v1.0/subjects/{id}/graph/export` | `json` (Pneuma's node and edge shape), `jsonld` and `turtle` (RDF), `graphml` (Gephi, yEd, and other graph tools). |
| `GET /v1.0/ontology-versions/{id}/export` | `turtle` or `jsonld`: node types as OWL classes, edge types as object properties with domain and range from endpoint rules, and the taxonomy as a SKOS concept scheme. |

IRIs are built from `baseIri` (default `urn:pneuma:`), which must be absolute. Graph export reads at most
`MaxGraphNodes` nodes and sets `X-Pneuma-Truncated: true` when it stopped there.

## Surfaces

- **Admin dashboard.** Knowledge → Ontologies: create, edit drafts (node types, edge types, rules, taxonomy),
  approve, retire, compare, propose, import, and export. Knowledge → Subjects → Ontology: pin, classification
  settings, violations, operations, graph export, and cache. Subject forms carry the temperature and cache switch.
  Prompts shows whether each prompt is the system default or the tenant's copy.
- **Subject dashboard.** The same views, in English and Spanish.
- **MCP.** Read ontologies, versions, a subject's ontology and violations, and start and read operations.
- **SDKs.** C#, JavaScript, and Python methods for every route.
