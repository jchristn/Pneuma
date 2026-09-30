# Ontology governance

Source plan: `ENTERPRISE_IMPROVEMENTS.md`, items E2 to E7. Requirements: `C:\Code\agents\requirements`.

This is the working document for the ontology governance effort. It adds governed, versioned ontologies to Pneuma,
reproducible classification, constraint rules, LLM-assisted authoring, standard graph export and taxonomy import,
and deterministic taxonomy tagging. Every task is a checkbox, and the progress log at the end records what was done
and how it was verified. The product version does not change; every change is recorded under `[Unreleased]` in
`CHANGELOG.md`. Nothing is committed or pushed without an explicit request.

## Scope

| ID | Item | Summary |
|---|---|---|
| E7 | Ontology versioning and approval | Tenant ontologies with numbered versions (Draft, Approved, Retired), a diff, and an approval step. A subject pins one approved version. |
| E2 | Reproducible classification | Temperature per subject (default 0), a classification cache keyed on everything that shapes the answer, provenance that names the ontology version, and a drift check. |
| E4 | Constraint rules | Edge endpoints, cardinality, required fields, name patterns, and minimum confidence, each with an action (warn, drop, quarantine, or reverse). Violations are recorded and quarantined elements can be released or dismissed. A validation run checks the existing graph. |
| E3 | LLM-assisted authoring | Propose a new draft version from a sample of a subject's content. |
| E6 | Export and import | Export a subject's graph as JSON, JSON-LD, Turtle, or GraphML, and an ontology version as OWL and SKOS. Import a SKOS taxonomy into a draft. |
| E5 | Taxonomy tagging | Concepts with preferred and alternative labels and a broader concept, matched deterministically before classification, linked to cells with `ABOUT` edges, and re-tagged when the pinned version changes. |

## Scope decisions

Every stored setting, prompt, and rule has one scope. Anything above subject scope is justified here.

| Thing | Scope | Why |
|---|---|---|
| Ontology, its versions, node and edge types, rules, and taxonomy concepts | **Tenant** | An ontology is an organizational asset: the same model is reused across many subjects in a tenant, and approval is a tenant governance act performed by tenant roles. It cannot be system scope, because tenants must not see or change each other's models. It is not subject scope, because pinning the same approved version to several subjects is the point of governance (one definition, many subjects). |
| Which version a subject uses (`ontologyVersionId`) | **Subject** | Each subject chooses its model. Pinning is audited and requires an approved version. |
| Classification temperature and cache switch | **Subject** | They shape one subject's ingestion, like its chunk settings. |
| Built-in ontology template (`Default`) | **System, read-only** | It is product content in code (the built-in node and edge types). It is never stored as a shared mutable record: a tenant creates its own ontology from it and edits its copy, so no tenant can change what another tenant sees. |
| Prompts (`ontology.classify`, `ontology.classify.format`, `ontology.propose`, `ontology.propose.format`, `taxonomy.hint`) | **System default, tenant override, subject override** | The system seeds a working default so the feature works out of the box. A tenant can override any prompt for all of its subjects, and a subject can override it again (append or replace), through the existing prompt resolver. |
| Editing a system prompt | **Tenant copy unless system administrator** | Before this change, any user with prompt update permission edited the shared system row, changing behavior for every tenant. Now a tenant user's edit creates or updates a tenant copy; only a system administrator edits the system default. Resetting deletes the tenant copy. |
| Violations, operations (validate, retag, drift check), and their items | **Subject** (tenant-scoped rows) | They describe one subject's graph and ingestion. |
| Classification cache | **Tenant** | Entries are keyed by the full prompt, the model runner, the model, and the temperature, so identical input in two subjects of the same tenant gives the same result and can share an entry. Entries never cross tenants. Stored in the blob store under the tenant's prefix and removed with the tenant. |
| Resource limits (`Ontology` settings: worker poll interval, export node cap, violations kept per job, proposal sample cap, drift sample cap) | **System** (`pneuma.json`) | They protect the server's memory, model capacity, and database, which every tenant shares. A tenant cannot raise them. |

## Permissions

A new resource type, `Ontology`, is used for ontology and version routes.

| Operation | Routes |
|---|---|
| Read | List and read ontologies and versions, diff, rendered definition, export a version, templates |
| Write | Create and update ontologies and drafts, create a draft from a version or a template, propose a draft, import a taxonomy |
| Delete | Delete an ontology (no version pinned) or a draft version |
| Execute | Approve and retire a version |

Approving is a separate operation so that authoring and approval can be split between roles. The built-in Editor
role gets Ontology Read and Write, not Execute; the Viewer role gets Read. Tenant administrators and system
administrators approve (they bypass RBAC, and the bypass is audited). A custom role can grant Ontology Execute.

Subject-facing routes use the Subject resource: reading a subject's ontology, violations, and operations, and
exporting its graph, are Subject Read (export returns only what reading the graph already shows); pinning, releasing
or dismissing a violation, starting an operation, and clearing the classification cache use Subject Update (pinning
also needs Ontology Read). Approvals, retirements, and pin changes are audited.

## Design decisions

**D1. A subject without a pinned version keeps today's behavior.** Classification uses the `ontology.definition`
prompt chain as before. Pinning a version replaces that chain for the subject with the version's rendered definition:
its node and edge types with descriptions, the edge endpoint rules, and the version's guidance text. The rendered text
is shown on the subject's ontology page, so what the model sees is never hidden.

**D2. Versions are immutable once approved.** Only a draft can be edited or deleted. A new draft copies a version
(or the built-in template). Approval validates the content: unique names, rules that reference declared types, and
patterns that compile. Retiring a version that a subject pins is refused, so a pinned version is always approved.

**D3. Output contracts are prompts.** The JSON shape the classifier and the authoring assistant must return was fixed
in code. It is now the `ontology.classify.format` and `ontology.propose.format` prompts, seeded with the same text, so
an administrator can see and adjust it. The parser is unchanged, so a contract that no longer produces the expected
JSON fails the batch visibly (a warning on the job) rather than silently.

**D4. The cache key is the whole request.** A classification call is cached under a SHA-256 of the model runner id,
the model name, the temperature, the full system prompt (task, ontology, output contract), and the full user prompt
(subject name, cells, taxonomy hints). Changing any of them misses the cache. The provenance on each job records the
ontology version, prompt hashes, model, temperature, and cache hits.

**D5. Rules run where their data is.** Rules are checked against each document's candidate subgraph during ingestion
(after type canonicalization, before merge). Cardinality across documents can only be seen in the stored graph, so
the validation operation checks the whole subject graph and records what it finds. Rules act on the fields a
candidate actually has (name, canonical name, content, rights, authority, confidence), because classified nodes have
no free-form properties.

**D6. Taxonomy tagging is deterministic and additive.** Labels are matched on word boundaries (case-insensitive
unless a concept says otherwise), longest match first. A match links the cell to a concept node (type `Topic` unless
the concept says otherwise) with an `ABOUT` edge tagged `assertedBy=taxonomy` and the concept key, and the concept's
broader concept is linked with a `BROADER` edge. Matches are also given to the classifier as hints through the
`taxonomy.hint` prompt. Tagging runs inside the existing classification and relationship stages instead of new
stages, so per-stage tuning, metrics, and dashboards are unchanged.

**D7. Background work is queued.** Validation, re-tagging, and drift checks are ontology operations stored in the
database and claimed by a worker, like evaluation runs, so they survive a slow model and a page reload. An operation
left running by a stopped server is marked failed at startup.

**D8. RDF uses a library.** Turtle and JSON-LD export and SKOS import use dotNetRDF (`dotNetRdf.Core`, MIT), because a
hand-written Turtle parser is a correctness risk. GraphML and Pneuma JSON are written directly.

## Phases

### Phase 0: Plan
- [x] Write this plan.

### Phase 1: Model, storage, and settings
- [x] Enums, models, prefixes (`ont_`, `onv_`, `orl_`, `ovl_`, `oop_`; concepts are keyed by their own key, not an id), and settings.
- [x] Migration 35 in all four providers: ontologies, versions, node types, edge types, rules, concepts, concept labels, violations, operations, operation items; subject columns; job completeness columns.
- [x] Database interfaces and implementations in all four providers; tenant and subject cascades.

### Phase 2: Ontology service and routes (E7)
- [x] Ontology service: create (empty, template, copy), draft update, approve, retire, delete, diff, render definition.
- [x] Routes, OpenAPI metadata, permissions, audit, subject pinning.
- [x] Prompt scoping fix (tenant copies of system prompts) and new prompts.

### Phase 3: Pipeline (E2, E4, E5)
- [x] Classification: pinned version definition, temperature, cache, provenance, taxonomy hints.
- [x] Canonicalization and rule validation, violations, quarantine.
- [x] Taxonomy edges in relationship consolidation.
- [x] Metrics.

### Phase 4: Operations, authoring, export (E2, E3, E4, E5, E6)
- [x] Operation worker: validate, retag, drift check.
- [x] Violation release and dismiss.
- [x] Propose a draft.
- [x] Graph export, version export, SKOS import.

### Phase 5: Surfaces
- [x] MCP tools.
- [x] Admin dashboard: Ontologies tab (list, version editor, rules, taxonomy, diff, approve, propose, import, export).
- [x] Subject dashboard: Ontology view (pin, rendered definition, classification settings, violations, operations, export).
- [x] SDKs (C#, JavaScript, Python) and README examples.
- [x] Postman folder.
- [x] Docs: `ONTOLOGY.md`, `REST_API.md`, `MCP_API.md`, `README.md`, `TELEMETRY.md`, `CHANGELOG.md`.

### Phase 6: Tests and close-out
- [x] Suites: `Ontology` (service, rules, taxonomy, export, import), `OntologyPipeline` (ingestion harness), `OntologyApi` (routes, permissions, prompt scoping).
- [x] Full build with zero warnings, full test run, dashboards lint and build, file-size guardrail.

## Progress log

- **2026-09-30.** Wrote this plan.
- **2026-09-30.** Implemented E2 to E7 across backend, pipeline, worker, routes, MCP, both dashboards (subject dashboard
  in English and Spanish), the three SDKs, Postman, and docs (`ONTOLOGY.md`, `REST_API.md`, `MCP_API.md`, `README.md`,
  `TELEMETRY.md`, `CHANGELOG.md`). Verified: solution builds with 0 warnings; `Test.Automated` 328 total, 325 passed,
  0 failed, 3 skipped (new suites Ontology, OntologyPipeline, OntologyApi included); SDK harness ontology steps pass in
  C#, JavaScript, and Python; both dashboards lint, test, and build; both dashboards' API clients pass a live
  end-to-end ontology smoke test; file-size guardrail passes. Not run locally: the database contract suite on
  PostgreSQL, MySQL, and SQL Server (CI covers it). Graph export is gated by Subject Read, not Update.
