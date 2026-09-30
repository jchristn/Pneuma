/**
 * Pneuma API Client
 *
 * Hand-rolled fetch-based client for the Pneuma backend. No axios.
 * Pneuma returns flat camelCase JSON (no {success,data} envelope), so responses
 * are returned as-is. Non-2xx responses throw an ApiError carrying the parsed
 * { error, message } body when present.
 */

import { streamSse } from './sse.js';

class ApiError extends Error {
  constructor(status, message, body) {
    super(message || `HTTP ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.body = body;
  }
}

/**
 * Coerce a variety of list-response shapes into a plain array.
 * Handles: raw arrays, { objects }, { items }, { data }, { subjects }, { links }, { jobs }.
 */
export function asArray(resp, ...keys) {
  if (Array.isArray(resp)) return resp;
  if (!resp || typeof resp !== 'object') return [];
  for (const key of [...keys, 'objects', 'items', 'data', 'results']) {
    if (Array.isArray(resp[key])) return resp[key];
  }
  return [];
}

/** Whether a RetrievalFilter object carries no predicate (so it can be omitted from a request). */
export function isEmptyFilter(f) {
  if (!f) return true;
  const len = (a) => (Array.isArray(a) ? a.length : 0);
  return !len(f.requiredLabels) && !len(f.excludedLabels) && !len(f.requiredTags) && !len(f.excludedTags);
}

class ApiClient {
  constructor(baseUrl, token = null) {
    this.baseUrl = (baseUrl || '').replace(/\/+$/, '');
    this.token = token;
  }

  _headers(extra = {}) {
    const headers = { 'Content-Type': 'application/json', ...extra };
    if (this.token) headers['Authorization'] = `Bearer ${this.token}`;
    return headers;
  }

  _buildUrl(path, query) {
    const url = new URL(this.baseUrl + path);
    if (query) {
      for (const [k, v] of Object.entries(query)) {
        if (v !== undefined && v !== null && v !== '') {
          url.searchParams.append(k, v);
        }
      }
    }
    return url.toString();
  }

  // ------------------------------------------------------ Ontologies
  listOntologyTemplates() {
    return this._request('GET', '/v1.0/ontology-templates');
  }

  listOntologies() {
    return this._request('GET', '/v1.0/ontologies', { query: { maxResults: 1000 } });
  }

  createOntology(body) {
    return this._request('POST', '/v1.0/ontologies', { body });
  }

  getOntology(id) {
    return this._request('GET', `/v1.0/ontologies/${encodeURIComponent(id)}`);
  }

  updateOntology(id, body) {
    return this._request('PUT', `/v1.0/ontologies/${encodeURIComponent(id)}`, { body });
  }

  deleteOntology(id) {
    return this._request('DELETE', `/v1.0/ontologies/${encodeURIComponent(id)}`);
  }

  createOntologyDraft(id, basedOnVersionId = null) {
    return this._request('POST', `/v1.0/ontologies/${encodeURIComponent(id)}/versions`, { body: { basedOnVersionId } });
  }

  proposeOntology(id, body) {
    return this._request('POST', `/v1.0/ontologies/${encodeURIComponent(id)}/propose`, { body });
  }

  getOntologyVersion(versionId) {
    return this._request('GET', `/v1.0/ontology-versions/${encodeURIComponent(versionId)}`);
  }

  updateOntologyVersion(versionId, body) {
    return this._request('PUT', `/v1.0/ontology-versions/${encodeURIComponent(versionId)}`, { body });
  }

  deleteOntologyVersion(versionId) {
    return this._request('DELETE', `/v1.0/ontology-versions/${encodeURIComponent(versionId)}`);
  }

  approveOntologyVersion(versionId, changeSummary = null) {
    return this._request('POST', `/v1.0/ontology-versions/${encodeURIComponent(versionId)}/approve`, { body: { changeSummary } });
  }

  retireOntologyVersion(versionId) {
    return this._request('POST', `/v1.0/ontology-versions/${encodeURIComponent(versionId)}/retire`);
  }

  diffOntologyVersion(versionId, against = null) {
    return this._request('GET', `/v1.0/ontology-versions/${encodeURIComponent(versionId)}/diff`, { query: { against } });
  }

  getOntologyDefinition(versionId) {
    return this._request('GET', `/v1.0/ontology-versions/${encodeURIComponent(versionId)}/definition`);
  }

  // Export and import move raw documents (Turtle, JSON-LD, GraphML), so they bypass the JSON request helper.
  async exportOntologyVersion(versionId, format = 'turtle') {
    return this._raw('GET', `/v1.0/ontology-versions/${encodeURIComponent(versionId)}/export`, { format });
  }

  async importTaxonomy(versionId, document, format = 'turtle', mode = 'merge') {
    const text = await this._raw('POST', `/v1.0/ontology-versions/${encodeURIComponent(versionId)}/taxonomy/import`, { format, mode },
      document, format === 'jsonld' ? 'application/ld+json' : 'text/turtle');
    return text ? JSON.parse(text) : null;
  }

  getSubjectOntology(subjectId) {
    return this._request('GET', `/v1.0/subjects/${encodeURIComponent(subjectId)}/ontology`);
  }

  setSubjectOntology(subjectId, ontologyVersionId, retag = true) {
    return this._request('PUT', `/v1.0/subjects/${encodeURIComponent(subjectId)}/ontology`, { body: { ontologyVersionId, retag } });
  }

  listOntologyViolations(subjectId, status = null) {
    return this._request('GET', `/v1.0/subjects/${encodeURIComponent(subjectId)}/ontology-violations`, { query: { status, maxResults: 1000 } });
  }

  releaseOntologyViolation(id) {
    return this._request('POST', `/v1.0/ontology-violations/${encodeURIComponent(id)}/release`);
  }

  dismissOntologyViolation(id) {
    return this._request('POST', `/v1.0/ontology-violations/${encodeURIComponent(id)}/dismiss`);
  }

  listOntologyOperations(subjectId) {
    return this._request('GET', `/v1.0/subjects/${encodeURIComponent(subjectId)}/ontology-operations`, { query: { maxResults: 100 } });
  }

  startOntologyOperation(subjectId, kind, sampleSize = 10) {
    return this._request('POST', `/v1.0/subjects/${encodeURIComponent(subjectId)}/ontology-operations`, { body: { kind, sampleSize } });
  }

  getOntologyOperation(id) {
    return this._request('GET', `/v1.0/ontology-operations/${encodeURIComponent(id)}`);
  }

  async exportSubjectGraph(subjectId, format = 'json') {
    return this._raw('GET', `/v1.0/subjects/${encodeURIComponent(subjectId)}/graph/export`, { format });
  }

  clearClassificationCache(subjectId) {
    return this._request('DELETE', `/v1.0/subjects/${encodeURIComponent(subjectId)}/classification-cache`);
  }

  async _raw(method, path, query = null, text = null, contentType = null) {
    const headers = this._headers(contentType ? { 'Content-Type': contentType } : {});
    const response = await fetch(this._buildUrl(path, query), { method, headers, body: text });
    if (response.status === 401) window.dispatchEvent(new CustomEvent('auth:unauthorized'));
    const body = await response.text().catch(() => '');
    if (!response.ok) {
      let parsed = null;
      try { parsed = body ? JSON.parse(body) : null; } catch { /* not json */ }
      throw new ApiError(response.status, (parsed && (parsed.message || parsed.error)) || response.statusText, parsed);
    }
    return body;
  }

  // ------------------------------------------------------ Crawl plans and operations
  listCrawlPlanTypes() {
    return this._request('GET', '/v1.0/crawl-plan-types');
  }

  listCrawlPlans(subjectId = null, query = null) {
    const q = { maxResults: 1000, ...(subjectId ? { subjectId } : {}), ...(query || {}) };
    return this._request('GET', '/v1.0/crawl-plans', { query: q });
  }

  getCrawlPlan(id) {
    return this._request('GET', `/v1.0/crawl-plans/${encodeURIComponent(id)}`);
  }

  createCrawlPlan(subjectId, body) {
    return this._request('POST', `/v1.0/subjects/${encodeURIComponent(subjectId)}/crawl-plans`, { body });
  }

  updateCrawlPlan(id, body) {
    return this._request('PUT', `/v1.0/crawl-plans/${encodeURIComponent(id)}`, { body });
  }

  deleteCrawlPlan(id, deleteLinks = false) {
    return this._request('DELETE', `/v1.0/crawl-plans/${encodeURIComponent(id)}`, { query: deleteLinks ? { deleteLinks: 'true' } : null });
  }

  testCrawlPlanDraft(body, fromPlanId = null) {
    return this._request('POST', '/v1.0/crawl-plans/test', { body, query: fromPlanId ? { fromPlanId } : null });
  }

  testCrawlPlan(id) {
    return this._request('POST', `/v1.0/crawl-plans/${encodeURIComponent(id)}/test`);
  }

  previewCrawlPlan(id) {
    return this._request('POST', `/v1.0/crawl-plans/${encodeURIComponent(id)}/preview`);
  }

  startCrawlPlan(id) {
    return this._request('POST', `/v1.0/crawl-plans/${encodeURIComponent(id)}/start`);
  }

  stopCrawlPlan(id) {
    return this._request('POST', `/v1.0/crawl-plans/${encodeURIComponent(id)}/stop`);
  }

  listCrawlPlanObjects(id, status = null) {
    return this._request('GET', `/v1.0/crawl-plans/${encodeURIComponent(id)}/objects`, { query: { maxResults: 1000, ...(status ? { status } : {}) } });
  }

  listCrawlOperations(planId = null, status = null) {
    const q = { maxResults: 1000, ...(planId ? { planId } : {}), ...(status ? { status } : {}) };
    return this._request('GET', '/v1.0/crawl-operations', { query: q });
  }

  getCrawlOperation(id) {
    return this._request('GET', `/v1.0/crawl-operations/${encodeURIComponent(id)}`);
  }

  listCrawlOperationObjects(id, action = null) {
    return this._request('GET', `/v1.0/crawl-operations/${encodeURIComponent(id)}/objects`, { query: { maxResults: 1000, ...(action ? { action } : {}) } });
  }

  confirmCrawlDeletions(id) {
    return this._request('POST', `/v1.0/crawl-operations/${encodeURIComponent(id)}/confirm-deletions`);
  }

  async _request(method, path, { query = null, body = null, headers = {}, signal } = {}) {
    const url = this._buildUrl(path, query);
    const init = { method, headers: this._headers(headers), signal };
    if (body !== null && body !== undefined) init.body = JSON.stringify(body);

    const response = await fetch(url, init);

    if (response.status === 401) {
      window.dispatchEvent(new CustomEvent('auth:unauthorized'));
    }

    if (response.status === 204) return null;

    const text = await response.text();
    let parsed = null;
    if (text) {
      try {
        parsed = JSON.parse(text);
      } catch {
        parsed = text;
      }
    }

    if (!response.ok) {
      const message =
        (parsed && typeof parsed === 'object' && (parsed.message || parsed.error)) ||
        (typeof parsed === 'string' && parsed) ||
        response.statusText;
      throw new ApiError(response.status, message, parsed);
    }

    return parsed;
  }

  get(path, options = {}) {
    return this._request('GET', path, options);
  }
  post(path, body, options = {}) {
    return this._request('POST', path, { ...options, body });
  }
  put(path, body, options = {}) {
    return this._request('PUT', path, { ...options, body });
  }
  delete(path, options = {}) {
    return this._request('DELETE', path, options);
  }

  // ------------------------------------------------------------------
  // Auth + health
  // ------------------------------------------------------------------

  async login(email, password, tenantId = null) {
    const body = { email, password };
    if (tenantId) body.tenantId = tenantId;
    return this._request('POST', '/v1.0/token', { body });
  }

  async validateToken() {
    return this._request('GET', '/v1.0/token');
  }

  async tokenDetails() {
    return this._request('GET', '/v1.0/token/details');
  }

  async logout() {
    return this._request('DELETE', '/v1.0/token');
  }

  async health() {
    return this._request('GET', '/v1.0/api/health');
  }

  // ------------------------------------------------------------------
  // Subjects
  // ------------------------------------------------------------------

  async getSubjects(options = {}) {
    return this._request('GET', '/v1.0/subjects', options);
  }
  async getSubject(id, options = {}) {
    return this._request('GET', `/v1.0/subjects/${encodeURIComponent(id)}`, options);
  }
  async createSubject(payload) {
    return this._request('POST', '/v1.0/subjects', { body: payload });
  }
  async updateSubject(id, payload) {
    return this._request('PUT', `/v1.0/subjects/${encodeURIComponent(id)}`, { body: payload });
  }
  async deleteSubject(id) {
    return this._request('DELETE', `/v1.0/subjects/${encodeURIComponent(id)}`);
  }

  // ------------------------------------------------------------------
  // Content links & ingestion
  // ------------------------------------------------------------------

  async getLinks(options = {}) {
    return this._request('GET', '/v1.0/links', options);
  }
  async getLink(id, options = {}) {
    return this._request('GET', `/v1.0/links/${encodeURIComponent(id)}`, options);
  }
  async deleteLink(id) {
    return this._request('DELETE', `/v1.0/links/${encodeURIComponent(id)}`);
  }
  // Bulk delete: one request marks every listed link for background cascade deletion (no per-link fan-out).
  async bulkDeleteLinks(ids) {
    return this._request('POST', '/v1.0/links/delete', { body: { ids } });
  }
  async getSubjectLinks(subjectId, options = {}) {
    return this._request('GET', `/v1.0/subjects/${encodeURIComponent(subjectId)}/links`, options);
  }
  // Per-step ingestion log for a content link: one entry per ingestion run,
  // each carrying its job plus an ordered list of stage events.
  async getLinkIngestionLog(linkId, options = {}) {
    return this._request('GET', `/v1.0/links/${encodeURIComponent(linkId)}/log`, options);
  }
  // Ingestion endpoints (embedding + completion models) available for link submission.
  async listIngestionEndpoints(options = {}) {
    return this._request('GET', '/v1.0/ingestion/endpoints', options);
  }
  // Vector collections (RecallDB) available as ingestion + search targets.
  async listCollections(options = {}) {
    return this._request('GET', '/v1.0/collections', options);
  }
  // System-default ingestion concurrency (IngestionTuning). Used to show the current default as the
  // placeholder for each per-subject concurrency override. May be admin-only server-side.
  async getIngestionSettings(options = {}) {
    return this._request('GET', '/v1.0/settings/ingestion', options);
  }
  // Submitting a link enqueues an ingestion job server-side. The backend requires an embedding
  // endpoint, a completion endpoint, and a target collection.
  // The subject owns its embedding/inference models and collection, so submission carries only url/title
  // plus optional labels/tags to attach to every produced chunk (for later retrieval scoping).
  async submitLink(subjectId, { url, title, labels, tags, refreshIntervalMinutes }) {
    const body = { url };
    if (title) body.title = title;
    if (refreshIntervalMinutes !== undefined && refreshIntervalMinutes !== null) body.refreshIntervalMinutes = refreshIntervalMinutes;
    if (labels && labels.length) body.labels = labels;
    if (tags && Object.keys(tags).length) body.tags = tags;
    return this._request('POST', `/v1.0/subjects/${encodeURIComponent(subjectId)}/links`, { body });
  }
  // Push content (text, Markdown, HTML, or JSON) into a subject; an externalKey replaces earlier content with the same key.
  async submitContent(subjectId, { title, content, contentType, externalKey, labels, tags }) {
    const body = { content, contentType };
    if (title) body.title = title;
    if (externalKey) body.externalKey = externalKey;
    if (labels && labels.length) body.labels = labels;
    if (tags && Object.keys(tags).length) body.tags = tags;
    return this._request('POST', `/v1.0/subjects/${encodeURIComponent(subjectId)}/content`, { body });
  }
  // Bulk submit multiple links for a subject in a single request; labels/tags apply to every URL.
  async bulkSubmitLinks(subjectId, { urls, labels, tags }) {
    const body = { urls };
    if (labels && labels.length) body.labels = labels;
    if (tags && Object.keys(tags).length) body.tags = tags;
    return this._request('POST', `/v1.0/subjects/${encodeURIComponent(subjectId)}/links/bulk`, { body });
  }
  // Reingest a single link: queues a FRESH ingestion job (a full re-run), even if the link has no prior job.
  // Returns 202 Accepted with the created job.
  async reingestLink(id) {
    return this._request('POST', `/v1.0/links/${encodeURIComponent(id)}/reingest`, {});
  }
  // Bulk reingest: queues a fresh ingestion job per link. Returns 202 Accepted with { queued, skipped }.
  async bulkReingestLinks(ids) {
    return this._request('POST', '/v1.0/links/reingest', { body: { ids } });
  }
  // Set a link's scheduled refresh. body: { refreshIntervalMinutes } (0 off, 60 to 525600) or { useSubjectDefault: true }.
  async setLinkRefresh(id, body) {
    return this._request('PUT', `/v1.0/links/${encodeURIComponent(id)}`, { body });
  }
  // Set the scheduled refresh of several links. Returns { updated, skipped } (skipped lists ids that cannot be refreshed).
  async bulkSetLinkRefresh(ids, body) {
    return this._request('POST', '/v1.0/links/refresh-interval', { body: { ...body, ids } });
  }
  // Check a link for changes now (conditional GET). Returns { outcome, jobId, message, nextRefreshUtc }.
  async refreshLinkNow(id) {
    return this._request('POST', `/v1.0/links/${encodeURIComponent(id)}/refresh`, {});
  }

  // ------------------------------------------------------------- New subject wizard
  // What the wizard can do for this caller: ontology modes, limits, and whether a completion model exists.
  wizardOptions() {
    return this._request('GET', '/v1.0/subject-wizard/options');
  }

  // Draft one step: brief, questions, ontology, prompts, or sources. body: { draft, modelRunnerId, guidance, mode, count }.
  // Returns { value, model, modelRunnerId, elapsedMs, warnings, groundingExcerpt }.
  wizardGenerate(step, body) {
    return this._request('POST', `/v1.0/subject-wizard/${encodeURIComponent(step)}`, { body });
  }

  // Same as wizardGenerate, streamed: onEvent receives { type: 'progress', phase, attempt, characters, elapsedMs, message }
  // while the model works, then { type: 'complete', result } or { type: 'error', statusCode, message }.
  wizardGenerateStream(step, body, { onEvent, signal } = {}) {
    return streamSse(`${this.baseUrl}/v1.0/subject-wizard/${encodeURIComponent(step)}/stream`, {
      method: 'POST',
      headers: this._headers({ Accept: 'text/event-stream' }),
      body,
      signal,
      onEvent
    });
  }

  // Clean up a draft ontology and render it as the classifier will see it (no model call).
  wizardRenderOntology(draft) {
    return this._request('POST', '/v1.0/subject-wizard/render-ontology', { body: { draft } });
  }

  // Create the subject, its starter questions, and its ontology from a finished draft.
  wizardCommit(body) {
    return this._request('POST', '/v1.0/subject-wizard/commit', { body });
  }

  // The model endpoints, collections, and global prompts the wizard offers or shows.
  wizardModelRunners() {
    return this._request('GET', '/v1.0/model-runners', { query: { maxResults: 1000 } });
  }

  wizardCollections() {
    return this._request('GET', '/v1.0/collections', { query: { maxResults: 1000 } });
  }

  wizardPrompts() {
    return this._request('GET', '/v1.0/prompts', { query: { maxResults: 1000 } });
  }

  // Content for a newly created subject: many URLs, pasted text, and the subject's links (for progress).
  wizardAddLinks(subjectId, urls) {
    return this._request('POST', `/v1.0/subjects/${encodeURIComponent(subjectId)}/links/bulk`, { body: { urls } });
  }

  wizardAddText(subjectId, title, content) {
    return this._request('POST', `/v1.0/subjects/${encodeURIComponent(subjectId)}/content`, { body: { title, content, contentType: 'text/plain' } });
  }

  wizardSubjectLinks(subjectId) {
    return this._request('GET', `/v1.0/subjects/${encodeURIComponent(subjectId)}/links`, { query: { maxResults: 1000 } });
  }

  // Ask one question against one subject (the coverage check).
  wizardAsk(subjectId, question) {
    return this._request('POST', '/v1.0/query', { body: { question, subjectId, maxResults: 10 } });
  }

  // A subject's starter questions, and replacing them. questions: [{ question, kind }].
  getSubjectQuestions(subjectId) {
    return this._request('GET', `/v1.0/subjects/${encodeURIComponent(subjectId)}/questions`);
  }

  setSubjectQuestions(subjectId, questions) {
    return this._request('PUT', `/v1.0/subjects/${encodeURIComponent(subjectId)}/questions`, { body: { questions } });
  }

  // Create up to 100 evaluation facts at once. facts: [{ subjectId, question, expectedAnswer, category }].
  bulkCreateEvalFacts(facts) {
    return this._request('POST', '/v1.0/eval/facts/bulk', { body: { facts } });
  }

  // ------------------------------------------------------------------
  // Prompts (global + per-subject)
  // ------------------------------------------------------------------

  // Global prompt catalog. Returns an array (or enumeration) of { id, key, name, description, content }.
  async getPrompts(options = {}) {
    return this._request('GET', '/v1.0/prompts', options);
  }
  // Update a global prompt by id. body carries the full prompt (key/description/content).
  async updatePrompt(id, payload) {
    return this._request('PUT', `/v1.0/prompts/${encodeURIComponent(id)}`, { body: payload });
  }
  // Per-subject prompt catalog: every prompt key with its effective content, the global default,
  // any subject override, the resolved source ("Global"|"SubjectOverride"), and the override merge mode.
  async getSubjectPrompts(subjectId) {
    return this._request('GET', `/v1.0/subjects/${encodeURIComponent(subjectId)}/prompts`);
  }
  // Set (or clear) a subject-level override for one prompt key. body: { content, mergeMode }.
  // An empty content clears the override so the subject reverts to the global default.
  async updateSubjectPrompt(subjectId, key, body) {
    return this._request('PUT', `/v1.0/subjects/${encodeURIComponent(subjectId)}/prompts/${encodeURIComponent(key)}`, { body });
  }
  // Remove a subject-level override for one prompt key, reverting to the global default.
  async deleteSubjectPrompt(subjectId, key) {
    return this._request('DELETE', `/v1.0/subjects/${encodeURIComponent(subjectId)}/prompts/${encodeURIComponent(key)}`);
  }

  // Ingestion jobs
  async getJobs({ status, ...options } = {}) {
    return this._request('GET', '/v1.0/jobs', { ...options, query: { status } });
  }
  async getJob(id, options = {}) {
    return this._request('GET', `/v1.0/jobs/${encodeURIComponent(id)}`, options);
  }
  async restartJob(id) {
    return this._request('POST', `/v1.0/jobs/${encodeURIComponent(id)}/restart`, {});
  }

  // ------------------------------------------------------------------
  // Request history
  // ------------------------------------------------------------------

  async getRequestHistory(query = {}, options = {}) {
    return this._request('GET', '/v1.0/api/request-history', { ...options, query });
  }
  async getRequestHistorySummary(query = {}, options = {}) {
    return this._request('GET', '/v1.0/api/request-history/summary', { ...options, query });
  }
  // Time-bucketed ingestion activity, broken down by pipeline stage.
  // query: { fromUtc, toUtc, bucketMinutes, subjectId }
  async getIngestionSummary(query = {}, options = {}) {
    return this._request('GET', '/v1.0/jobs/summary', { ...options, query });
  }
  async getRequestHistoryEntry(id, options = {}) {
    return this._request('GET', `/v1.0/api/request-history/${encodeURIComponent(id)}`, options);
  }
  async deleteRequestHistoryEntry(id) {
    return this._request('DELETE', `/v1.0/api/request-history/${encodeURIComponent(id)}`);
  }
  async bulkDeleteRequestHistory(query = {}) {
    return this._request('DELETE', '/v1.0/api/request-history', { query });
  }

  // ------------------------------------------------------------------
  // OpenAPI (API Explorer)
  // ------------------------------------------------------------------

  async getOpenApiSpec() {
    return this._request('GET', '/openapi.json');
  }

  /** Grounded natural-language answer over the corpus (non-streaming). */
  async ask(question, maxResults = 10) {
    return this._request('POST', '/v1.0/query', { body: { question, maxResults } });
  }

  /** Grounded answer streamed over server-sent events (metadata/delta/complete/error). */
  async askStream(question, maxResults = 10, { onEvent, signal } = {}) {
    await streamSse(this.baseUrl + '/v1.0/query/stream', {
      method: 'POST',
      headers: this._headers({ Accept: 'text/event-stream' }),
      body: { question, maxResults },
      signal,
      onEvent,
    });
  }

  /**
   * Multi-turn agentic chat over the corpus, streamed over server-sent events. The model may call
   * Pneuma's read tools while answering. Invokes `onEvent` for each `delta` / `tool_call` /
   * `tool_result` / `complete` / `error` event. `messages` is an array of `{ role, content }` turns.
   */
  async chatStream(messages, maxResults = 8, { onEvent, signal, subjectId, threadId, metadataFilter = null } = {}) {
    await streamSse(this.baseUrl + '/v1.0/chat/stream', {
      method: 'POST',
      headers: this._headers({ Accept: 'text/event-stream' }),
      body: {
        messages,
        maxResults,
        subjectId: subjectId || null,
        threadId: threadId || null,
        ...(metadataFilter && !isEmptyFilter(metadataFilter) ? { metadataFilter } : {}),
      },
      signal,
      onEvent,
    });
  }

  getAnalytics(subjectId = null, days = 30) {
    return this._request('GET', '/v1.0/analytics', { query: { days, ...(subjectId ? { subjectId } : {}) } });
  }

  evalListFacts(subjectId) {
    return this._request('GET', '/v1.0/eval/facts', { query: { subjectId } });
  }
  evalCreateFact(fact) {
    return this._request('POST', '/v1.0/eval/facts', { body: fact });
  }
  evalDeleteFact(id) {
    return this._request('DELETE', `/v1.0/eval/facts/${encodeURIComponent(id)}`);
  }
  evalListRuns(subjectId = null) {
    return this._request('GET', '/v1.0/eval/runs', { query: subjectId ? { subjectId } : {} });
  }
  evalStartRun(subjectId, category = null) {
    return this._request('POST', '/v1.0/eval/runs', { body: { subjectId, category } });
  }
  evalGetRun(id) {
    return this._request('GET', `/v1.0/eval/runs/${encodeURIComponent(id)}`);
  }
  evalDeleteRun(id) {
    return this._request('DELETE', `/v1.0/eval/runs/${encodeURIComponent(id)}`);
  }
  // Bulk delete: one request deletes every listed evaluation run and its results server-side.
  evalBulkDeleteRuns(ids) {
    return this._request('POST', '/v1.0/eval/runs/delete', { body: { ids } });
  }
  /** Cancel a queued/running eval run. Idempotent; returns the (possibly Cancelled) run. */
  evalCancelRun(id) {
    return this._request('POST', `/v1.0/eval/runs/${encodeURIComponent(id)}/cancel`);
  }
  /**
   * Live SSE progress for an eval run. Invokes `onEvent` for each
   * `metadata` / `result` / `progress` / `complete` / `error` event.
   */
  evalRunStream(id, { onEvent, signal } = {}) {
    return streamSse(this.baseUrl + '/v1.0/eval/runs/' + encodeURIComponent(id) + '/stream', {
      method: 'GET',
      headers: this._headers({ Accept: 'text/event-stream' }),
      signal,
      onEvent,
    });
  }

  listThreads(subjectId = null) {
    return this._request('GET', '/v1.0/threads', { query: { maxResults: 1000, ...(subjectId ? { subjectId } : {}) } });
  }

  // Read a thread and its turns (oldest first): `{ thread, turns }`.
  getThread(id) {
    return this._request('GET', `/v1.0/threads/${encodeURIComponent(id)}`);
  }

  renameThread(id, title) {
    return this._request('PUT', `/v1.0/threads/${encodeURIComponent(id)}`, { body: { title } });
  }

  deleteThread(id) {
    return this._request('DELETE', `/v1.0/threads/${encodeURIComponent(id)}`);
  }

  // Warm the subject's answering model (best-effort) so the first question isn't slow to first token.
  warmup(subjectId = null) {
    return this._request('POST', '/v1.0/warmup', { body: subjectId ? { subjectId } : {} });
  }

  /**
   * Execute an arbitrary request built by the API Explorer, returning the raw
   * Response so the caller can inspect status, headers, and streaming bodies.
   */
  async executeExplorer({ method, path, query, headers, body }) {
    const url = this._buildUrl(path, query);
    return fetch(url, {
      method,
      headers: this._headers(headers || {}),
      body: body !== null && body !== undefined && body !== '' ? body : undefined
    });
  }

  // ---- Chat history + feedback ----------------------------------------

  listHistory(subjectId = null) {
    return this._request('GET', '/v1.0/history', { query: { maxResults: 1000, ...(subjectId ? { subjectId } : {}) } });
  }

  getHistoryTurn(id) {
    return this._request('GET', `/v1.0/history/${encodeURIComponent(id)}`);
  }

  listFeedback(subjectId = null) {
    return this._request('GET', '/v1.0/feedback', { query: { maxResults: 1000, ...(subjectId ? { subjectId } : {}) } });
  }

  submitFeedback(turnId, rating, comment = null) {
    return this._request('POST', '/v1.0/feedback', { body: { turnId, rating, comment } });
  }
}

export default ApiClient;
export { ApiError };
