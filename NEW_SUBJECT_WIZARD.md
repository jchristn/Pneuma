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
`WizardQuestion`, `WizardOntologyProposal`, and so on), no `JsonElement`. Output that does not parse is retried once
with the parse error appended; a second failure returns a 502 with the reason, and the dashboard shows an empty
editable step. Ontology proposals are validated: unique type names, edge endpoints that reference declared types,
names normalized to PascalCase for nodes and UPPER_SNAKE for edges, and caps on counts and lengths.

**D4. The ontology is saved in whichever form the server supports.** Until ontology governance Phase 2 lands, commit
renders the proposal into the subject's `OntologyDefinitionPrompt` (types, descriptions, and endpoint rules as text),
which the classifier already consumes. After it lands, commit creates a tenant ontology with a Draft version from the
proposal. If the caller has Ontology Execute, the wizard offers "Approve and use it", which approves the version and
pins it. Otherwise the draft is left for an approver and the rendered text is used meanwhile. Either way the
classifier sees the same definition.

**D5. User content is data, not instructions.** The description, grounding text, and fetched page are placed in the
user message inside delimiters and the system prompt says to treat them as material about the subject. A grounding
URL goes through the fetch-safety policy and the download limit like any ingested link.

**D6. Output language follows the user.** The prompts tell the model to write in the language of the description, so
a subject described in Spanish gets Spanish questions, tagline, and answering prompt. The wizard's own UI strings are
translated through i18next as usual.

**D7. Starter questions are stored structurally.** A `subjectquestions` table (id, tenant, subject, question, kind,
position, origin of Model or User, created) rather than a JSON column, per the data-layer rules. The subject's
Questions tab edits them after creation, and the ask page shows up to four as suggestions when a conversation starts.

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
- [ ] Resolve the open questions below with the user.

### Phase 1: Generation service and prompts (W1, W2)
- [ ] Request and response DTOs for each step; `WizardSettings` (`Wizard` section: limits and timeout).
- [ ] Seeded prompts and output contracts, idempotent on first boot and on upgrade.
- [ ] `SubjectWizardService`: model resolution (request, then tenant default completion endpoint), one retry on a
  parse failure, locked-item enforcement (D2), validation (D3), grounding through the fetch-safety policy (D5).
- [ ] Metrics: `pneuma_wizard_generation_total{step,outcome}` and `pneuma_wizard_generation_duration_seconds{step}`;
  a span per generation.

### Phase 2: Commit, starter questions, bulk facts (W3, W4, W5)
- [ ] Migration: `subjectquestions` in all four providers, tenant and subject cascades.
- [ ] Commit: subject, Append prompt overrides, starter questions, ontology per D4, all or nothing.
- [ ] Starter question routes; bulk evaluation facts route.
- [ ] Ontology governance integration behind a capability check, so the wizard works before and after it lands.

### Phase 3: Dashboards (W6)
- [ ] Shared wizard steps in the subject dashboard: "New subject" opens the wizard; the existing form becomes "Create
  manually".
- [ ] Admin dashboard: the same wizard from Knowledge > Subjects; the first-run setup's subject step hands off to it.
- [ ] Step rail, lock and edit controls, regenerate with guidance, traceability warnings, prompt-versus-default view,
  review page, sources step reusing the link, text, and crawl plan surfaces, coverage check.
- [ ] Subject Questions tab; starter questions on the user dashboard ask page.
- [ ] i18n (en, es), light and dark themes, 1280, 768, and 390 px.

### Phase 4: Surfaces (W7)
- [ ] MCP tools: `pneuma_draft_subject` (runs steps 2 to 5 in one call and returns the draft) and
  `pneuma_create_subject_from_draft`, so an agent can set up a subject the same way.
- [ ] SDKs (C#, JavaScript, Python), Postman folder.
- [ ] Docs: `REST_API.md`, `MCP_API.md`, `TELEMETRY.md`, `README.md` (a short section on creating a subject with the
  wizard), and `CHANGELOG.md`.

### Phase 5: Tests and close-out
- [ ] Suite `SubjectWizard` against `StubModelServer`: each step parses a canned response; a malformed response is
  retried once and then reported; locked items survive regeneration; ontology validation rejects unknown endpoints;
  commit is all or nothing; prompts are Append overrides; grounding URLs to private addresses are refused.
- [ ] Database contract cases for `subjectquestions` in all four providers.
- [ ] Full build with zero warnings, full test run, dashboards lint and build, file-size guardrail.
- [ ] Simulated user session per `SIMULATED_USER_TESTING.md`: a first-time user creates a subject with the wizard
  against a real small model, with findings brought to the user before any fix.

## Open questions

- **Draft persistence.** D1 keeps the draft in the browser. Is resuming on another device or sharing a draft with a
  colleague something you want in the first version?
- **Where it lives.** The plan puts the wizard in both the subject and admin dashboards and hands the admin first-run
  setup over to it. Should the manual form stay as the default for admins, with the wizard as the alternative?
- **Ontology timing.** D4 ships the ontology as prompt text first and switches to governed versions when that work
  lands. Would you rather the wizard wait for governance and only ever produce versions?
- **Ask-page suggestions.** Starter questions shown to end users on the ask page are a visible product change. Include
  it, or keep starter questions internal to the wizard and evaluation?
- **Coverage check cost.** It asks every accepted question once. Is a manual start with the cost shown enough, or
  should it be capped (for example, the first eight questions)?
- **Grounding fetch.** Letting step 1 read a URL makes the drafts much better for obscure subjects but means the model
  sees fetched content before the subject exists. Keep it, or limit grounding to pasted text?

## Progress log

- **2026-09-30.** Wrote this plan.
