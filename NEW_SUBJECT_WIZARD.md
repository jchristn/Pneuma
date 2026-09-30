# New subject wizard

Requirements: `C:\Code\agents\requirements`. Related plan: `ONTOLOGY_GOVERNANCE.md` (in progress).

This is the working plan for a guided way to create a subject. Today a subject is a long form: a name, a type, three
models, a collection, chunk settings, and five prompts that most people leave at their defaults because they do not
know what to write. The defaults are domain-neutral on purpose, so a subject about a jazz musician, a medical device
company, and a city council all get the same ontology ("Person, Organization, Work, Event, Place, Theme") and the same
answering voice. The results are acceptable and forgettable.

The wizard turns that around. The user says, in a sentence or two, what the subject is and who will ask about it. A
configured inference model does the rest of the first draft: a short brief, the questions people are likely to ask,
an ontology that can answer those questions, and prompts that fit the domain. At every step the user can accept,
edit, delete, lock, or regenerate what the model wrote, or type their own. Then the wizard helps them add content and
checks whether the content answers the questions they started from.

The product version does not change. Every change is recorded under `[Unreleased]` in `CHANGELOG.md`. Nothing is
committed or pushed without an explicit request.

## Principles

**Ask little, infer a lot.** The only required input is a description. The name, type, tagline, audience, questions,
ontology, and prompts are all drafted by the model. Models, the collection, and chunking get sensible defaults and
live behind an "Advanced" disclosure; a user who never opens it still ends up with a working subject.

**Questions drive everything downstream.** Example questions are the spine of the wizard. The ontology exists to
answer them, the classification prompt tells the model what to extract so they can be answered, and the coverage check
at the end asks them for real. When the user edits the questions, the later steps are regenerated from the edited set,
so the user steers the whole subject by steering one list they can easily judge.

**The user edits; the model proposes.** Nothing the model writes is saved until the user reaches the review step, and
anything the user touched is never overwritten by a regeneration. Every step shows what the model proposed, lets the
user change it in place, and offers "regenerate the rest" with optional guidance ("more about his early career",
"fewer questions about pricing").

**Nothing is hidden.** The final prompts, the rendered ontology, and the questions are shown in full before the
subject is created, next to the defaults they replace. The wizard's own prompts are ordinary seeded prompts that an
administrator can read and override.

## What exists today

The pieces below are already in place and the wizard builds on them rather than replacing them.

| Capability | Where | Notes |
|---|---|---|
| Subject model with prompt fields | `Pneuma.Core/Models/Subject.cs` | `SystemPrompt`, `OntologyClassifyPrompt`, `OntologyDefinitionPrompt`, `RerankingPrompt`, `PromptRewritePrompt`, `Tagline`, `Description`, `Type`. |
| Prompt resolution and overrides | `PromptResolver.cs`, `SubjectPromptRoutes.cs` | Global prompt, then a subject override (Append or Replace), then the legacy subject column. `PUT /v1.0/subjects/{id}/prompts/{key}`. |
| Model calls | `ModelClientFactory.Create(...).ChatAsync(...)` | Used by classification, answering, and `OntologyProposer`. There is no route that runs an arbitrary completion for a dashboard. |
| Ontology as prompt text | `ClassificationSetupBuilder.cs` | Without a pinned version, the `ontology.definition` chain plus the subject's `OntologyDefinitionPrompt` is the ontology. |
| Structured ontologies (in progress) | `ONTOLOGY_GOVERNANCE.md`, `Pneuma.Core/Ontologies/` | Tenant ontologies with Draft, Approved, and Retired versions; a subject pins one approved version. `OntologyProposer` drafts a version from a subject's content. No routes yet. |
| Evaluation facts | `EvalFact`, `/v1.0/eval/facts` | Question plus expected answer per subject, added one at a time. No bulk create. |
| Ingestion entry points | `SubjectLinkRoutes.cs`, `CrawlPlanRoutes.cs` | Single and bulk links, pushed text, crawl plans with test and preview. |
| First-run setup wizard | `admin-dashboard/src/components/SetupWizard.jsx` | Models, a bare subject (name, type, description), one link, and a progress view. Sets no prompts. |
| Starter questions on the ask page | none | The user dashboard shows only the subject name and tagline. |

## The flow

The wizard is a modal, full-height on desktop and full-screen at 390 px, with a step rail on the left and a single
"Continue" action. Each step can be revisited from the rail. Steps 2 to 5 each make one model call, show a spinner with
a cancel button while it runs, and fall back to an empty editable list if the call fails, so a slow or broken model
never strands the user.

### 1. Describe

One text box: "What is this subject, and who will ask about it?" A placeholder shows an example ("The life and work of
the saxophonist Charlie Parker, for music students and jazz fans"). Two optional inputs sit below it, collapsed by
default: a URL or pasted text the model can read for grounding, and the "Advanced" settings (inference model, embedding
model, collection, chunking). The inference model defaults to the tenant's first active completion endpoint and the
collection to the tenant's default collection, so most users never open this.

If no completion endpoint exists, the step says so and links to Model Endpoints instead of failing later.

### 2. Brief

The model returns a brief: display name, type (free text, such as "Musician" or "Medical device company"), a
one-paragraph description, an ask-page tagline, the intended audience, and the tone answers should take. The user
sees it as a small editable form. Most people will glance at it and continue; the point of the step is to catch a
misunderstanding (the model thought "Mercury" meant the planet) before it shapes everything else.

### 3. Example questions

The model proposes twelve questions, each tagged with a kind: fact, relationship, timeline, comparison, reasoning, or
overview. Kinds matter because they tell the ontology step what structure is needed; a subject whose questions are all
"who worked with whom" needs rich relationship types, while one full of "how did X change over time" needs events and
dates.

The user can edit any question in place, delete it, add their own, drag to reorder, and lock the ones they like. "More
questions" asks for additional questions unlike the current ones; "Regenerate the rest" replaces every unlocked,
unedited question and accepts a line of guidance. The step asks for at least five questions before continuing, since
fewer gives the ontology step too little to work with.

### 4. Ontology

From the brief and the accepted questions, the model proposes node types and edge types, each with a one-line
description, and for every type the questions it serves. Edge types declare their endpoint types. The user edits the
lists the same way as the questions, and the step shows two warnings as they work: questions that no type serves, and
types that serve no question. Those warnings are the most useful feedback in the wizard, because they make the link
between "what people will ask" and "what the graph records" concrete.

The proposal starts from the built-in types (Person, Organization, Work, Event, Place, Topic, and the rest) and keeps
the ones that fit, so a subject's graph stays compatible with the rest of the product. The model may rename or add
types but is told to reuse built-in names where the meaning is the same.

### 5. Prompts

The model drafts the subject's additions to four prompts, each shown next to the global default it extends:

- **Answering (`SystemPrompt`):** voice, audience, what to do when the archive does not support an answer, and any
  domain conventions (for example, cite album and year).
- **Classification (`OntologyClassifyPrompt`):** what to extract from this kind of content, with domain hints drawn
  from the questions (instrument names are Works, not Topics; session dates matter).
- **Query rewriting (`PromptRewritePrompt`):** domain synonyms and nicknames ("Bird" is Charlie Parker).
- **Reranking (`RerankingPrompt`):** what makes a passage relevant for this audience.

Each prompt has "Use the default instead" and "Regenerate" controls. Prompts are saved as Append overrides, so the
global prompt and its output contract stay in force and the subject only adds to them. The Replace mode stays
available on the subject's Prompts page for experts, but the wizard never uses it.

### 6. Review and create

One page shows everything: the brief, the questions, the rendered ontology exactly as the classifier will see it, and
each prompt. "Create subject" commits it all in one request. If anything fails, nothing is created.

### 7. Add content

The subject exists now, and the wizard moves to sources. The model suggests where content for this kind of subject
usually lives ("a discography site, liner notes, interviews; a sitemap would pick up a whole site"), shown as plain
suggestions rather than actions. Below them are the actual choices, reusing existing surfaces:

- **Links:** paste one or many URLs, with the optional refresh interval.
- **Text:** paste or type content (the content push API).
- **A crawl plan:** opens the existing crawl plan form for this subject, with its connectivity test and preview.

After submitting, the step shows ingestion progress per link (the existing ingestion log polling). The user can close
the wizard at any point; ingestion continues in the background.

### 8. Coverage check

Once the first content has finished ingesting, the wizard asks the accepted questions against the subject through the
normal query path and shows, for each one, the answer, its sources, and whether it had enough support. Unanswered
questions point back to step 7 ("add content about his time in Kansas City") or to the ontology if a relationship is
missing. Answers the user confirms can be saved as evaluation facts in one action, which gives the subject a regression
baseline on day one.

The check spends model calls, so it runs only when the user starts it, with the question count and endpoint shown on
the button.

## Scope

| ID | Item | Summary |
|---|---|---|
| W1 | Wizard generation service | Server-side, stateless calls that draft the brief, questions, ontology, prompts, and source suggestions from a subject draft. |
| W2 | Wizard prompts | Seeded, overridable prompts for each step plus their JSON output contracts. |
| W3 | Commit | One request that creates the subject, its prompt overrides, its starter questions, and its ontology. |
| W4 | Starter questions | A per-subject list of questions, shown as suggestions on the ask page and usable by the coverage check. |
| W5 | Bulk evaluation facts | Create many evaluation facts at once (for the coverage check). |
| W6 | Dashboards | The wizard in the subject and admin dashboards, and in the admin first-run setup. |
| W7 | Surfaces | MCP, SDKs, Postman, docs, metrics, tests. |

## Scope decisions

| Thing | Scope | Why |
|---|---|---|
| Wizard prompts (`wizard.brief`, `wizard.questions`, `wizard.ontology`, `wizard.prompts`, `wizard.sources`, and a `.format` contract for each) | **System default, tenant override, subject not applicable** | They run before the subject exists, so a subject override has nothing to attach to. A tenant can tune them for its domain through the existing prompt scoping. |
| The draft being edited | **Browser (see D1)** | It belongs to one user in one session until committed. |
| Starter questions | **Subject** | They describe one subject. |
| Which inference model drafts | **Request** | The user picks it in step 1; it defaults to the subject's inference model once chosen. |
| Generation limits (questions per call, prompt length caps, call timeout, sample text cap) | **System** (`pneuma.json`, `Wizard` section) | They protect model capacity shared by every tenant. |

## Design decisions

**D1. The draft lives in the browser; generation is stateless.** Each step's endpoint receives the draft so far and
returns a proposal. Nothing is stored until commit. The dashboard keeps the draft in `sessionStorage` so a reload does
not lose work. A server-side draft store would let a user resume on another device, but it adds a table, a cleanup
job, and a half-created state to reason about, and the wizard takes minutes, not days. Revisit if users ask for it.

**D2. Every generation takes the user's edits as constraints.** A request carries the items the user locked or edited,
and the prompt tells the model to keep them verbatim and fill in around them. The server enforces it too: locked items
from the request are copied into the response unchanged, whatever the model returned. Regeneration can therefore
never destroy the user's work.

**D3. Model output is parsed into typed DTOs and validated.** Each step has a response class (`WizardBrief`,
`WizardQuestion`, `WizardOntologyOutput`, and so on), no `JsonElement`. Output that does not parse is retried once
with the parse error appended; a second failure returns a 502 with the reason, and the dashboard shows an empty
editable step. Ontology proposals are validated: unique type names, edge endpoints that reference declared types,
names normalized to PascalCase for nodes and UPPER_SNAKE for edges, and caps on counts and lengths.

**D4. The ontology is saved as a governed version when the caller may, and as prompt text always.** Ontology governance
landed before the wizard was built, so commit offers three modes: **Approve** (the default for callers with Ontology
Execute) creates a tenant ontology, approves its first version, and pins it to the subject; **Draft** (Ontology Write)
creates the ontology and leaves the version for an approver; **Prompt** creates no ontology. In every mode the rendered
ontology is also stored as the subject's `OntologyDefinitionPrompt`, so a subject waiting for approval (or later
unpinned) still classifies with the drafted types. A mode the caller may not use is lowered with a warning rather than
failing the commit. Creation, approval, and pinning are audited like the ontology routes.

**D5. User content is data, not instructions.** The description, grounding text, and fetched page are placed in the
user message inside delimiters and the system prompt says to treat them as material about the subject. A grounding
URL goes through the fetch-safety policy and the download limit like any ingested link.

**D6. Output language follows the user.** The prompts tell the model to write in the language of the description, so
a subject described in Spanish gets Spanish questions, tagline, and answering prompt. The wizard's own UI strings are
translated through i18next as usual.

**D8. Commit undoes itself on failure.** The data layer has no cross-table transaction, so commit creates the subject
first and, if storing its questions or its ontology fails, removes the subject, its questions, and any ontology it
created before reporting the error. The questions themselves are replaced in one transaction.

**D9. Prompt additions use the subject's own prompt columns.** The subject's `SystemPrompt`, `OntologyClassifyPrompt`,
`PromptRewritePrompt`, and `RerankingPrompt` are always appended to the global prompts by the prompt resolver, so the
wizard writes its additions there rather than creating per-subject overrides. They show up in the subject form and on
the subject's Prompts page like any hand-written addition.

**D7. Starter questions are stored structurally.** A `subjectquestions` table (id, tenant, subject, question, kind,
position, origin of Model or User, created) rather than a JSON column, per the data-layer rules. A "Starter Questions"
row action on the Subjects pages edits them after creation, and the ask page shows up to four as suggestions when a
conversation starts.

## Example

A user types: "Charlie Parker, the bebop saxophonist. For music students writing papers."

The brief comes back as display name "Charlie Parker", type "Musician", tagline "Ask about Bird's music, collaborators,
and legacy", audience "music students", tone "precise, cites recordings and dates". Questions include "Who played on
the 1945 Savoy sessions?" (relationship), "How did his style change after 1947?" (timeline), and "What is the chord
structure of Ko-Ko based on?" (fact). The ontology keeps Person, Work, Event, and Place, adds Recording, Session, and
Label, and adds edges such as PERFORMED_ON (Person to Recording), RECORDED_AT (Recording to Session), and BASED_ON
(Work to Work). The warning list flags that nothing serves "How was he received by critics?", so the user adds a Review
type, or deletes the question. The classification prompt tells the model that tune titles are Works, albums are
Recordings, and nicknames ("Bird", "Yardbird") refer to Charlie Parker. The user pastes three discography URLs, waits
for ingestion, runs the coverage check, and saves the seven answered questions as evaluation facts.

## Routes

| Method | Path | Purpose |
|---|---|---|
| POST | `/v1.0/subject-wizard/brief` | Draft the brief from the description (and optional grounding). |
| POST | `/v1.0/subject-wizard/questions` | Draft or extend example questions (`mode`: `replace-unlocked` or `more`). |
| POST | `/v1.0/subject-wizard/ontology` | Draft node and edge types from the brief and questions, with question traceability. |
| POST | `/v1.0/subject-wizard/prompts` | Draft the four prompt additions. |
| POST | `/v1.0/subject-wizard/sources` | Suggest where content for this subject might come from. |
| POST | `/v1.0/subject-wizard/commit` | Create the subject, prompt overrides, starter questions, and ontology in one request. |
| GET, PUT | `/v1.0/subjects/{id}/questions` | Read and replace a subject's starter questions. |
| POST | `/v1.0/eval/facts/bulk` | Create up to 100 evaluation facts. |

All wizard routes need Subject Write. Commit additionally needs Ontology Write (and Execute to approve and pin) once
governance lands. Every generation route takes the inference model id and an optional `guidance` string, and returns
the proposal plus the model and elapsed time so the dashboard can show what produced it.

## Phases

### Phase 0: Plan
- [x] Write this plan.
- [x] Resolve the open questions below with the user.

### Phase 1: Generation service and prompts (W1, W2)
- [x] Request and response DTOs for each step; `WizardSettings` (`Wizard` section: limits and timeout).
- [x] Seeded prompts and output contracts, idempotent on first boot and on upgrade.
- [x] `SubjectWizardService`: model resolution (request, then tenant default completion endpoint), one retry on a
  parse failure, locked-item enforcement (D2), validation (D3), grounding through the fetch-safety policy (D5).
- [x] Metrics: `pneuma_wizard_generation_total{step,outcome}` and `pneuma_wizard_generation_duration_seconds{step}`;
  a span per generation.

### Phase 2: Commit, starter questions, bulk facts (W3, W4, W5)
- [x] Migration: `subjectquestions` in all four providers, tenant and subject cascades.
- [x] Commit: subject, prompt additions (D9), starter questions, ontology per D4, removed again on failure (D8).
- [x] Starter question routes; bulk evaluation facts route.
- [x] Ontology governance integration: modes offered by the caller's permissions, lowered with a warning (D4).

### Phase 3: Dashboards (W6)
- [x] Shared wizard steps in the subject dashboard: "New subject" opens the wizard; the existing form becomes "Create
  manually".
- [x] Admin dashboard: the same wizard from Knowledge > Subjects; the first-run setup's subject step offers it.
- [x] Step rail, lock and edit controls, regenerate with guidance, traceability warnings, prompt-versus-default view,
  review page, sources step reusing the link, text, and crawl plan surfaces, coverage check.
- [x] Starter Questions row action on both Subjects pages; starter questions on the user dashboard ask page.
- [x] i18n (en, es), light and dark themes, 1280, 768, and 390 px.

### Phase 4: Surfaces (W7)
- [x] MCP tools: `pneuma_draft_subject` (runs steps 2 to 5 in one call and returns the draft) and
  `pneuma_create_subject_from_draft`, so an agent can set up a subject the same way.
- [x] SDKs (C#, JavaScript, Python), Postman folder.
- [x] Docs: `REST_API.md`, `MCP_API.md`, `TELEMETRY.md`, `README.md` (a short section on creating a subject with the
  wizard), and `CHANGELOG.md`.

### Phase 5: Tests and close-out
- [x] Suite `SubjectWizard` against `StubModelServer`: each step parses a canned response; a malformed response is
  retried once and then reported; locked items survive regeneration; ontology validation rejects unknown endpoints;
  commit is all or nothing; prompts are Append overrides; grounding URLs to private addresses are refused.
- [x] Database contract case for `subjectquestions` (runs against each provider in CI, like the other suites).
- [x] Full build with zero warnings, full test run, dashboards lint and build, file-size guardrail.
- [ ] Simulated user session per `SIMULATED_USER_TESTING.md`: a first-time user creates a subject with the wizard
  against a real small model, with findings brought to the user before any fix.

## Open questions

- **Draft persistence (answered 2026-09-30).** The user confirmed the draft lives in the browser (D1).
- **Where it lives (decided while building; easy to change).** "+ New Subject" opens the wizard in both dashboards and
  the manual form moved to a secondary "Create manually" button. The admin first-run setup offers the wizard at its
  subject step rather than replacing that step outright, so a first-time admin can still type a subject by hand.
- **Ontology timing (settled by events).** Ontology governance landed first, so the wizard produces governed versions
  from the start (D4).
- **Ask-page suggestions (included).** Up to four starter questions show as suggestions on the ask page. Delete a
  subject's starter questions to hide them.
- **Coverage check cost (capped).** The check runs only when started, shows the count on the button, and asks at most
  `Wizard.CoverageMaxQuestions` (default 12) questions.
- **Grounding fetch (kept).** Step 1 can read a URL, through the fetch-safety policy and the download limit; pasted
  text works as well. Turning it off is a constructor switch on the routes if an operator asks for it.

## Progress log

- **2026-09-30.** Wrote this plan.
- **2026-09-30. Built** (the user confirmed the browser-held draft; the other open questions were decided as recorded
  above). Server: migration 36 (`subjectquestions`), `SubjectWizardService` (drafting, one retry, locked items kept,
  ontology clean-up), `SubjectWizardCommitService` (commit with removal on failure), `SubjectCreation` (the create rules
  shared with `SubjectRoutes`), routes under `/v1.0/subject-wizard` plus render-ontology, starter question routes, bulk
  evaluation facts, ten seeded `wizard.*` prompts, the `Wizard` settings section, and `WizardMetrics`. MCP tools
  `pneuma_draft_subject` and `pneuma_create_subject_from_draft`. Dashboards: the eight-step wizard in the subject and
  admin dashboards (shared components, own `sw-` styles mapped to each theme, draft in `sessionStorage`), Starter
  Questions modals, the setup hand-off, and ask-page suggestions in the user dashboard; en and es strings in their own
  `wizard` modules. SDKs, Postman, configs, and docs. Suite `SubjectWizard`: 11 cases, all passing.
- **2026-09-30. Verified in a browser** against a temporary server (SQLite, the bench RecallDB, LiteGraph, and
  DocumentAtom in their own tenant) and the local gemma3:4b model. The subject dashboard wizard ran end to end: brief
  28 s, questions 228 s, regenerating kept the locked and the edited question, ontology 280 s (7 node and 4 relationship
  types), prompts 198 s, commit 1 s (ontology approved and pinned), pasted text ingested, and the coverage check
  answered from it. Screens checked at 1280, 768, and 390 px in light and dark; the admin Subjects page, wizard, and
  Starter Questions modal, and the user dashboard's ask-page suggestions were checked too, with no page errors. Fixes
  from the run: the model timeout default went from 180 to 300 s (the ontology step timed out on the 4B model); empty
  question and ontology steps now say "Draft" and explain themselves; the ontology prompt now says types are kinds of
  things, never the subject or one specific person or work (the 4B model made "CharlieParker" a type); "Create subject"
  moved to the footer; the ontology table columns and the add-question row were resized; the coverage check stops when
  the wizard closes; the rail is left-aligned in the admin dashboard. Full suite before the fixes: 339 cases, 336
  passed, 3 skipped; the SubjectWizard suite passes after them. The simulated user session (Phase 5) is still to do.
- **2026-09-30. Model choice, several links, and progress** (the user's requests after trying the images). The first
  step now picks the drafting model and takes up to `Wizard.MaxGroundingUrls` (5) reference links; each later step has
  its own model picker next to its Regenerate button; the model that answers for the subject stays in Advanced and
  defaults to the drafting model. Drafting steps stream progress from new `/v1.0/subject-wizard/{step}/stream` routes
  (`WizardModelCaller` streams the reply and times out only after `TimeoutSeconds` without output, or three times that in
  all; `WizardGroundingReader` reads the pages). The dashboards show a progress panel (phase chips, live timer, characters
  written, attempt, model, and the step's last time), each finished step's time in the rail, and the total model time in
  the header. Found while checking: the model pickers never listed endpoints (the model-runner list returns `type`, not
  `capabilities`); fixed. A 12,000-character Wikipedia excerpt stalled gemma3:4b, so `MaxGroundingCharacters` now
  defaults to 6000. Suite SubjectWizard: 12 cases.
