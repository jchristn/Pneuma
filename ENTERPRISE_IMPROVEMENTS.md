# Enterprise improvements

This plan draws on a market scan of cognitive knowledge graph (CKG) platforms (`AVAILABLE_CKG_PLATFORMS.md`, Dell
Project Aegon, 2026-09-28). The scan scored 22 surviving platforms against an enterprise brief. The brief covered
connecting to data where it lives, an automated unstructured-to-graph pipeline, ontology and rule configurability,
substrate openness, on-prem/air-gap deployment, operability at scale, and vendor viability. Pneuma scored 3.25 against
4.10 to 4.25 for the leaders. This document lists the capabilities those platforms have that Pneuma lacks, scored for
how simple they are to integrate and how much they would add.

No code was changed to produce this plan. Items that overlap `archive/INGESTION_IMPROVEMENTS.md` or
`archive/RETRIEVAL_IMPROVEMENTS.md` carry that document's ID (for example "ING-A13" or "RI-S"). Implement each item once and
tick it in both documents.

## What the market scan teaches

1. **The market is thin at building and governing the graph, not at storing or querying it.** The scan uses a four-tier
   model: acquisition, semantics and construction, graph substrate, and consumption. Crawlers (tier 1), graph stores
   (tier 3), and chat/GraphRAG front ends (tier 4) are well served. Tier 2 is scarce: ontology management, extraction
   to the ontology, entity resolution, and rules. This is where Pneuma should invest.
2. **LLM-only classification is penalized for non-determinism.** Graphwise scores highly because its tagging is
   Lucene-based, repeatable, cheap per document, and re-indexes automatically when the taxonomy changes. The scan's
   central technical criticism of Pneuma is that prompt-based classification drifts between runs and model versions,
   and has no governance: no constraint validation, versioning, approval workflow, or taxonomy management.
3. **Permission mirroring is the most common production failure in the category.** Carrying source ACLs into
   graph-level and retrieval-level authorization is under-documented by nearly every vendor and the hardest thing to
   retrofit.
4. **Pneuma's differentiators are already rare.** The scan praises its observable pipeline, structural provenance
   (chunks link back to graph nodes), refusal on insufficient support, and the LLM-judged evaluation harness, and
   says commercial vendors should be held to the same standard. The gaps are enterprise ones: application connectors
   (not just storage), SSO, Kubernetes packaging, ontology governance, and entity resolution.
5. **Some ideas are worth borrowing from specific platforms.** Graphiti records when each fact was true. Fluree's
   provenance is cryptographically verifiable. Stardog's Voicebox and Graphwise's Graph Modeling use LLMs to help
   author the ontology. Hume and Linkurious provide investigation UIs.

### Where the scan is already out of date

The scan lists Pneuma's crawlers as web, CIFS/SMB, NFS, and S3 only. Crawl plans now also cover sitemaps, GitHub,
Azure Blob, Google Cloud Storage, and server-local folders (see `CRAWLING.md`). Community detection with LLM
community summaries already exists (`Ontology.NodeCommunitySummary`, `IGraphRepository`), so it is not listed below.
Entity matching today is an exact match on `(nodeType, canonicalName)` in `SubgraphMerger.cs`.

## Scoring

Every item is scored on two axes from 1 to 10.

- **Simplicity** is how easy the item is to integrate. 10 is a small, local change with no new dependency. 1 is a
  cross-cutting effort with new external dependencies, new entities, and a large UI.
- **Value** is the benefit to enterprise adoption. It covers closing a gap the scan scored against Pneuma,
  correctness and determinism of the graph, safety, and operability. 10 is essential; 1 is marginal.
- **Total** is Simplicity + Value (at most 20).

The table is ordered by Total, descending. Ties are broken by Simplicity.

## Ranked items

| Rank | ID | Capability | Inspired by | Simplicity | Value | Total | Also |
|---:|---|---|---|:---:|:---:|:---:|---|
| 1 | E1 | SharePoint Online / M365 crawler | Graphwise Connectors, Onyx, Sinequa | 6 | 10 | **16** | ING-A13 |
| 2 | E2 | Reproducible classification | Graphwise (determinism) | 8 | 7 | **15** | Done |
| 3 | E3 | LLM-assisted ontology authoring | Stardog Voicebox, Graphwise Graph Modeling | 8 | 7 | **15** | Done |
| 4 | E4 | Ontology constraint rules (lightweight SHACL) | TopQuadrant, eccenca, Stardog | 7 | 8 | **15** | Done |
| 5 | E5 | Deterministic taxonomy tagging before the LLM | Graphwise Semantic Analytics, Progress Semaphore | 6 | 9 | **15** | Done |
| 6 | E6 | Standard graph export and taxonomy import | metaphactory, eccenca (openness) | 8 | 6 | **14** | Done |
| 7 | E7 | Ontology versioning and approval workflow | TopQuadrant EDG, Graphwise | 7 | 7 | **14** | Done |
| 8 | E8 | OIDC single sign-on | eccenca (Keycloak), Sinequa, Onyx | 6 | 8 | **14** | |
| 9 | E9 | Entity resolution beyond exact canonical match | Quantexa, Linkurious, Cognee | 5 | 9 | **14** | RI-S, ING-P15 |
| 10 | E10 | SQL database crawler (rows as documents) | eccenca, Palantir | 7 | 6 | **13** | ING-A19 |
| 11 | E11 | Helm chart and Kubernetes packaging | Neo4j, Stardog, metaphactory | 6 | 7 | **13** | |
| 12 | E12 | Confluence, Jira, and ServiceNow crawlers | Onyx, Sinequa | 6 | 7 | **13** | ING-A15 |
| 13 | E13 | Tamper-evident provenance | Fluree | 7 | 5 | **12** | |
| 14 | E14 | Query-time inference rules | Stardog, AllegroGraph | 6 | 6 | **12** | |
| 15 | E15 | Temporal facts (valid time) | Graphiti | 5 | 7 | **12** | |
| 16 | E16 | Interactive graph explorer | Hume, Linkurious, metaphactory | 5 | 7 | **12** | |
| 17 | E17 | Declarative structured-to-graph mapping | eccenca, Palantir Pipeline Builder | 4 | 8 | **12** | |
| 18 | E18 | Source permission mirroring | Onyx, Glean, Sinequa | 3 | 9 | **12** | |
| 19 | E19 | Federated virtual graphs | Stardog, Timbr, PuppyGraph | 2 | 5 | **7** | |
| 20 | E20 | Ontology-driven actions and write-back | Palantir Foundry | 2 | 4 | **6** | |

How to read the table:

- **Status.** E2 to E7 are implemented; see `ONTOLOGY.md` and the working record in `ONTOLOGY_GOVERNANCE.md`.
- **Quick wins (E2 to E7).** Each is mostly self-contained, and together they answer the scan's governance criticism
  (ontology score 3.0). E2 and E7 reinforce each other: a classification is reproducible only if it records the
  ontology version that produced it.
- **Enterprise blockers (E1, E8, E11).** These are the concrete reasons the scan gave for Pneuma's low scores on
  connectivity (3.0), operability (2.5), and deployment (capped at 4.0).
- **Most valuable item in the lower half (E18).** Permission mirroring is expensive, but the scan names it the most
  common production failure. Sequence it right after E1 and E8, which it depends on.

## Conventions

Every item follows the conventions in `archive/INGESTION_IMPROVEMENTS.md` ("Conventions every item follows"). Those cover
`CLAUDE.md` code style, tenant-scoped entities in all four database providers with versioned migrations, write-only
encrypted secrets, and OpenAPI-described routes. Surface parity is also required: dashboards, all three SDKs, MCP,
Postman, docs, and Touchstone tests with positive and negative cases.

## Item details

### E1. SharePoint Online / M365 crawler (ING-A13)

**Why.** SharePoint is the conspicuous gap the scan names, given the size of enterprise M365 estates. Only Graphwise and
Squirro among the tier-2 vendors ship a credible first-party M365 crawler.

**Design.** A `SharePoint` crawl plan type on the existing crawl plan framework (see `CRAWLING.md` and `archive/ADDING_CRAWLERS.md`). It
authenticates with an Entra ID app registration (client credentials; the secret or certificate is write-only). It
enumerates sites, drives, and folders through Microsoft Graph, and uses `driveItem` delta queries so each run fetches
only changes since the last delta token. The version token is the item's `eTag`. It captures each item's permissions
for E18 even before E18 enforces them. Teams channel files live in SharePoint document libraries and come with no
extra work; OneDrive is the same API against a user's drive. The connectivity test reports each layer: token
acquisition, Graph reachability, site resolution, and drive read.

**Tests.** A loopback stub of the Graph endpoints (token, sites, drives, delta with `@odata.nextLink` and
`@odata.deltaLink`). Positive: first run, incremental delta, rename, delete. Negative: expired secret, throttling
with `Retry-After`, a site the app cannot read, and a delta token the server has expired (fall back to a full
enumeration).

### E2. Reproducible classification

**Why.** The scan's main technical objection is that LLM classification drifts between runs and model versions.
Pneuma can make it reproducible without giving up the LLM.

**Design.** Pin classification to temperature 0 and a fixed seed where the provider supports one. Cache each
classification result keyed by `(cell content hash, ontology version (E7), classification prompt hash, model runner
id, model name)`. Re-ingesting unchanged content then reuses the result, and a re-classification happens only when
one of those inputs changes. Record the key's components on the job so an operator can see why a cell was
re-classified. Add a drift check to the evaluation harness: re-classify a sample of cells and report the share whose
node and edge sets changed.

**Tests.** Unchanged content re-ingests with no completion calls. A changed prompt or ontology version invalidates the
cache. The drift report counts a stub model that alternates answers.

### E3. LLM-assisted ontology authoring

**Why.** Ontology authoring is the usual reason knowledge graph programs stall. Stardog's Voicebox and Graphwise's
Graph Modeling both use an LLM as a knowledge-engineering assistant with human approval.

**Design.** A `POST /v1.0/subjects/{id}/ontology/propose` route that samples a subject's cells (or a supplied
sample of documents) and asks the inference model for proposed node types, edge types, definitions, and example
instances, returned as a draft for review. The same assistant generates labels and definitions for existing types,
optionally in several languages. Nothing is applied without approval; an approved draft becomes a new ontology
version (E7).

**Tests.** A stub model returns a fixed proposal. The draft round-trips, approval creates a version, and rejection
changes nothing. A malformed model reply returns a 502 with the raw text logged.

### E4. Ontology constraint rules (lightweight SHACL)

**Why.** Pneuma has no constraint validation. An LLM can emit an edge between the wrong node types or omit a required
property, and nothing stops it reaching the graph.

**Design.** Per-ontology rules, stored in structured tables:

- Edge domain and range: which node types an edge type may connect.
- Cardinality: for example, at most one `CREATED_BY` per `Work`.
- Required properties per node type.
- Allowed value patterns.

A validation step after OntologyCanonicalization and before GraphMerge checks each candidate. It then drops,
quarantines (records the triple on the job without merging it), or coerces the candidate, per the rule's severity.
Violations count toward the job's completeness warnings. A `POST /v1.0/subjects/{id}/ontology/validate` route checks
the existing graph against the current rules and reports violations.

**Tests.** A candidate edge with a wrong range is dropped, and the job ends with warnings. A cardinality violation is
quarantined. With no rules defined, behavior is unchanged.

### E5. Deterministic taxonomy tagging before the LLM

**Why.** Graphwise's Lucene-based tagging is the scan's model for cost and determinism at scale. Matching known
concepts needs no LLM call per document.

**Design.** A subject can have a taxonomy of concepts, each with a preferred label, alternative labels, and a broader
concept (SKOS-shaped), imported through E6 or edited in the dashboard. A tagging stage runs before Classification. It
matches labels over cell text with a multi-pattern matcher (Aho-Corasick over normalized, case-folded tokens), adds
`ABOUT` edges to the matching `Topic` nodes, and passes the matches to the classifier as context. The classifier then
only has to find what the taxonomy does not already know. When a taxonomy changes, a background job re-tags existing
cells from stored cell text with no fetch and no LLM, which is Graphwise's "auto re-index on taxonomy change".

**Tests.** Known labels and alternative labels tag the right concepts. Overlapping labels prefer the longest match. A
taxonomy edit re-tags existing cells without completion calls.

### E6. Standard graph export and taxonomy import

**Why.** The scan rates Pneuma 3.5 on openness because LiteGraph is not a standard store and there is no standard
export. An export in standard formats is the cheapest way to raise that score, and it provides the import path for E5.

**Design.** `GET /v1.0/subjects/{id}/graph/export?format=jsonld|turtle|graphml` streams the subject's graph, mapping
node types to classes, edge types to properties, and provenance tags to PROV-O. `POST
/v1.0/subjects/{id}/taxonomy/import` accepts SKOS in Turtle or JSON-LD.

**Tests.** An exported graph re-parses with a standard RDF parser and has the expected triple count. A SKOS import
creates the concepts, and a malformed file returns 400 with the line number.

### E7. Ontology versioning and approval workflow

**Why.** Enterprise ontology governance needs a history, a diff, and an approval step. TopQuadrant and Graphwise are
the reference points.

**Design.** Ontology versions become records: a version number, author, status (`Draft`, `Approved`, `Retired`), and
the node types, edge types, rules (E4), and classification prompt that make up the version. A subject pins one
approved version. Promoting a draft requires the new `OntologyApprove` permission and is audited. A diff route
compares two versions. Classification records the version it used (E2), and the dashboard can list the cells
classified under an older version for re-classification.

**Tests.** A draft cannot be pinned. Approval without permission is denied and audited. Classification tags carry the
version, and the diff reports added, removed, and changed types.

### E8. OIDC single sign-on

**Why.** The scan's operability score (2.5) cites no SSO/SAML/OIDC/SCIM. Enterprise IT expects Entra ID or Keycloak.

**Design.** Per-tenant OIDC identity providers (issuer, client id, write-only client secret, scopes, and a claim to
role mapping). The dashboards' login page offers "Sign in with …" for the tenant. The server runs the authorization
code flow with PKCE, validates the ID token against the issuer's JWKS, and matches or just-in-time provisions the user
by email. It then issues the same opaque session token dashboards use today, so everything downstream of
authentication is unchanged. Group claims map to user roles. SAML and SCIM are follow-ups.

**Tests.** A loopback stub IdP (discovery, JWKS, token endpoint). Positive: a valid login, just-in-time provisioning,
and a group-to-role mapping. Negative: wrong audience, expired token, bad signature, a replayed `state`, and an email
outside the tenant's allowed domains.

### E9. Entity resolution beyond exact canonical match (RI-S, ING-P15)

**Why.** Resolution is an exact `(nodeType, canonicalName)` match today, so "IBM", "I.B.M.", and "International
Business Machines" become three nodes. Entity resolution is one of the scan's four use cases, and Quantexa wins it
outright.

**Design.**

- **Aliases.** Nodes carry aliases, and the merger matches on aliases as well as the canonical name.
- **Candidate matching.** When there is no exact match, candidates come from normalized-string similarity (Jaro-Winkler
  over a normalized form) and embedding similarity of the name plus a short context. Matches above a high threshold
  merge automatically and record the alias. Matches between thresholds go to a review queue.
- **Review queue.** The subject dashboard offers merge, split, and "not the same" decisions, each audited. "Not the
  same" is remembered so the pair is never suggested again.
- **Serialization.** Merges are serialized per canonical key (RI-S) so concurrent jobs do not create duplicates.

**Tests.** Known alias variants merge. A near-match lands in the queue. A "not the same" decision suppresses the pair.
Eight concurrent jobs that share an entity produce one node.

### E10. SQL database crawler (ING-A19)

**Design.** A `Sql` crawl plan type: a connection string (write-only), a query, a key column, a version column (for
example `updated_at`), and a row-to-document template. Each row becomes a document through the existing pipeline.
The type supports PostgreSQL, MySQL, SQL Server, and SQLite through the drivers Pneuma already carries. E17 is the
richer, graph-native alternative.

### E11. Helm chart and Kubernetes packaging

**Why.** The scan caps Pneuma's deployment score at 4.0 because Docker Compose is a single-host model.

**Design.** A Helm chart for the server, dashboards, DocumentAtom, RecallDB, and LiteGraph, with external Postgres and
object storage as the recommended production mode. The chart is the easy part. Running several server replicas safely
needs the crawl scheduler, crawl dispatcher, and link refresh service to hold a lease (leader election or a database
lease) so only one instance schedules. The ingestion queue is already safe with several workers. Document upgrade
and rollback, and add readiness and liveness probes.

### E12. Confluence, Jira, and ServiceNow crawlers (ING-A15)

**Design.** Crawl plan types on the same framework as E1, using each product's REST API with an API token (write-only).
Incremental sync uses the last-modified query each API supports. Page and issue hierarchy becomes `HAS_PART` edges,
and authors become `CREATED_BY`.

### E13. Tamper-evident provenance

**Design.** Record a SHA-256 of each ingested source's bytes on the Source node, and chain audit records (each record
stores the hash of the previous one) so tampering with history is detectable. A verification route re-hashes stored
sources and walks the chain.

### E14. Query-time inference rules

**Design.** Declarative rules attached to the ontology version: inverse edges (`CREATED_BY` ⇄ `CREATOR_OF`),
transitive edges (`HAS_PART`), and a node-type hierarchy (`Organization` ⊃ `Company`). They are evaluated during graph
traversal and neighbor expansion rather than materialized, so changing a rule needs no reprocessing.

### E15. Temporal facts (valid time)

**Why.** Graphiti's bitemporal model is, in the scan's words, "the most differentiated thing in this document". It
answers "what did we believe, and when was it true".

**Design.** Edges gain optional `validFrom` and `validTo` (when the fact was true, extracted by the classifier when the
text says so) and `observedAt` (when Pneuma learned it). A newer fact that contradicts an older one closes the older
edge's `validTo` instead of deleting it. Queries and graph routes accept an `asOf` time.

### E16. Interactive graph explorer

**Design.** A subject dashboard view built on hand-rolled SVG (no graph library, per the frontend rules). It offers
force-directed layout, click to expand neighbors, filters by node and edge type, and shortest path between two
nodes. Each node's side panel shows its sources and chunks. Hume and Linkurious set the bar; this is a smaller
investigation surface, not a competitor.

### E17. Declarative structured-to-graph mapping

**Design.** An R2RML-style mapping from table columns to node types, properties, and edges. For example, rows of
`employees` become `Person` nodes, and `dept_id` becomes `AFFILIATED_WITH` to an `Organization`. The mapping runs from
a SQL crawl plan (E10) without an LLM, so structured data enters the graph deterministically and shares entity
resolution (E9) with extracted entities.

### E18. Source permission mirroring

**Why.** The scan calls this the most common production failure in the category and the hardest thing to retrofit.

**Design.** Crawlers capture each object's ACL: users and groups from SharePoint (E1), and share ACLs from CIFS where
available. Principals are stored as tags on Source nodes and chunks (`aclPrincipals`). Retrieval adds a tag filter
for the requesting user's principals, resolved from the identity provider (E8) and cached briefly. Graph routes hide
nodes whose only provenance the user cannot read. Admin and tenant-admin bypasses are audited as they are today. An
object with no captured ACL stays visible to the tenant, with a per-plan switch to deny instead.

**Depends on.** E1 and E8.

### E19. Federated virtual graphs

Stardog and Timbr query structured sources in place instead of copying them. This conflicts with Pneuma's design, in
which the graph and RecallDB hold what was ingested. It is recorded here for completeness and is not recommended.

### E20. Ontology-driven actions and write-back

Palantir couples ontology objects to operational actions that write back to source systems. This is outside
Pneuma's scope as a knowledge platform. It is recorded here for completeness and is not recommended.

## Sequencing

1. **Governance quick wins:** E7, then E2 (which keys on the ontology version), E4, E3, and E6. These are mostly local
   changes, and they answer the scan's main technical criticism.
2. **Enterprise access:** E8, then E1, then E18 (which depends on both), and E11 alongside them.
3. **Graph quality:** E5 (after E6 provides taxonomy import) and E9.
4. **Structured data and more sources:** E10, E17, and E12.
5. **Differentiators as demand appears:** E15, E16, E14, and E13.

## Checklist

Tick an item when its server, dashboard, SDK, MCP, Postman, documentation, and test work are all done.

| Done | ID | Item |
|:---:|---|---|
| ☐ | E1 | SharePoint Online / M365 crawler |
| ☐ | E2 | Reproducible classification |
| ☐ | E3 | LLM-assisted ontology authoring |
| ☐ | E4 | Ontology constraint rules |
| ☐ | E5 | Deterministic taxonomy tagging |
| ☐ | E6 | Standard graph export and taxonomy import |
| ☐ | E7 | Ontology versioning and approval |
| ☐ | E8 | OIDC single sign-on |
| ☐ | E9 | Entity resolution |
| ☐ | E10 | SQL database crawler |
| ☐ | E11 | Helm chart and Kubernetes packaging |
| ☐ | E12 | Confluence, Jira, and ServiceNow crawlers |
| ☐ | E13 | Tamper-evident provenance |
| ☐ | E14 | Query-time inference rules |
| ☐ | E15 | Temporal facts |
| ☐ | E16 | Interactive graph explorer |
| ☐ | E17 | Declarative structured-to-graph mapping |
| ☐ | E18 | Source permission mirroring |
| ☐ | E19 | Federated virtual graphs (not recommended) |
| ☐ | E20 | Ontology-driven actions (not recommended) |
