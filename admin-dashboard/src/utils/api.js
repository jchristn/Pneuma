/**
 * Hand-rolled fetch-based API client for the Pneuma backend.
 * No axios: keeps the bundle small and gives the API Explorer raw Response access.
 */
import { streamSse } from './sse.js';

/** Whether a RetrievalFilter object carries no predicate (so it can be omitted from a request). */
export function isEmptyFilter(f) {
  if (!f) return true;
  const len = (a) => (Array.isArray(a) ? a.length : 0);
  return !len(f.requiredLabels) && !len(f.excludedLabels) && !len(f.requiredTags) && !len(f.excludedTags);
}

export class ApiError extends Error {
  constructor(status, body, parsed) {
    super(parsed?.message || parsed?.error || `HTTP ${status}`);
    this.status = status;
    this.body = body;
    this.parsed = parsed || null;
  }
}

class ApiClient {
  constructor(baseUrl, token = null) {
    this.baseUrl = (baseUrl || '').replace(/\/+$/, '');
    this.token = token;
  }

  setToken(token) {
    this.token = token;
  }

  _headers(extra = {}) {
    const headers = { 'Content-Type': 'application/json', ...extra };
    if (this.token) headers['Authorization'] = `Bearer ${this.token}`;
    return headers;
  }

  buildQuery(params) {
    if (!params) return '';
    const query = new URLSearchParams();
    Object.entries(params).forEach(([k, v]) => {
      if (v !== undefined && v !== null && v !== '') query.append(k, v);
    });
    const s = query.toString();
    return s ? `?${s}` : '';
  }

  async _request(method, path, { query = null, body = null, headers = {} } = {}) {
    const url = `${this.baseUrl}${path}${this.buildQuery(query)}`;
    const init = { method, headers: this._headers(headers) };
    if (body !== null && body !== undefined) init.body = JSON.stringify(body);

    const response = await fetch(url, init);

    if (response.status === 401) {
      window.dispatchEvent(new CustomEvent('auth:unauthorized'));
    }

    if (!response.ok) {
      const text = await response.text().catch(() => '');
      let parsed = null;
      try { parsed = text ? JSON.parse(text) : null; } catch { /* not json */ }
      throw new ApiError(response.status, text, parsed);
    }

    if (response.status === 204) return null;
    const text = await response.text();
    if (!text) return null;
    try { return JSON.parse(text); } catch { return text; }
  }

  // ---------------------------------------------------------------- Auth
  async login(email, password, tenantId) {
    return this._request('POST', '/v1.0/token', {
      body: tenantId ? { email, password, tenantId } : { email, password }
    });
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

  async getOpenApiSpec() {
    return this._request('GET', '/openapi.json');
  }

  /** Grounded natural-language answer over the corpus (non-streaming). */
  async ask(question, maxResults = 10) {
    return this._request('POST', '/v1.0/query', { body: { question, maxResults } });
  }

  /**
   * Grounded answer streamed over server-sent events. Invokes `onEvent` for each
   * `metadata` / `delta` / `complete` / `error` event.
   */
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

  // ------------------------------------------------------ Request history
  getRequestHistory(filters = {}) {
    return this._request('GET', '/v1.0/api/request-history', { query: filters });
  }

  getRequestHistorySummary(filters = {}) {
    return this._request('GET', '/v1.0/api/request-history/summary', { query: filters });
  }

  // Time-bucketed ingestion activity, broken down by pipeline stage.
  // filters: { fromUtc, toUtc, bucketMinutes, subjectId }
  getIngestionSummary(filters = {}) {
    return this._request('GET', '/v1.0/jobs/summary', { query: filters });
  }

  getRequestHistoryEntry(id) {
    return this._request('GET', `/v1.0/api/request-history/${encodeURIComponent(id)}`);
  }

  deleteRequestHistoryEntry(id) {
    return this._request('DELETE', `/v1.0/api/request-history/${encodeURIComponent(id)}`);
  }

  deleteRequestHistoryBulk(filters = {}) {
    return this._request('DELETE', '/v1.0/api/request-history', { query: filters });
  }

  // --------------------------------------------------- Generic REST helpers
  list(resource, query = null) {
    return this._request('GET', `/v1.0/${resource}`, { query });
  }

  get(resource, id) {
    return this._request('GET', `/v1.0/${resource}/${encodeURIComponent(id)}`);
  }

  create(resource, body) {
    return this._request('POST', `/v1.0/${resource}`, { body });
  }

  update(resource, id, body) {
    return this._request('PUT', `/v1.0/${resource}/${encodeURIComponent(id)}`, { body });
  }

  remove(resource, id) {
    return this._request('DELETE', `/v1.0/${resource}/${encodeURIComponent(id)}`);
  }

  // Bulk delete: one request performs (or enqueues) the cascade for every id server-side, so the browser
  // never fans out one DELETE per row. Backed by POST /v1.0/{resource}/delete.
  bulkRemove(resource, ids) {
    return this._request('POST', `/v1.0/${resource}/delete`, { body: { ids } });
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
    const response = await fetch(`${this.baseUrl}${path}${this.buildQuery(query)}`, { method, headers, body: text });
    if (response.status === 401) window.dispatchEvent(new CustomEvent('auth:unauthorized'));
    const body = await response.text().catch(() => '');
    if (!response.ok) {
      let parsed = null;
      try { parsed = body ? JSON.parse(body) : null; } catch { /* not json */ }
      throw new ApiError(response.status, body, parsed);
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

  // ------------------------------------------------------------- Jobs
  listJobs(status, query = null) {
    const q = { ...(status ? { status } : {}), ...(query || {}) };
    return this._request('GET', '/v1.0/jobs', { query: Object.keys(q).length ? q : null });
  }

  // Live ingestion snapshot: jobs running a stage, waiting for a per-stage slot, or queued to start.
  getIngestionLive(subjectId = null) {
    return this._request('GET', '/v1.0/jobs/live', { query: subjectId ? { subjectId } : null });
  }

  getJob(id) {
    return this._request('GET', `/v1.0/jobs/${encodeURIComponent(id)}`);
  }

  restartJob(id) {
    return this._request('POST', `/v1.0/jobs/${encodeURIComponent(id)}/restart`);
  }

  stopJob(id) {
    return this._request('POST', `/v1.0/jobs/${encodeURIComponent(id)}/stop`);
  }

  // Delete a job and cascade its processing log, graph nodes/edges, and indexed documents.
  deleteJob(id) {
    return this._request('DELETE', `/v1.0/jobs/${encodeURIComponent(id)}`);
  }

  // Bulk delete: one request cascades every listed job server-side (no per-job fan-out).
  bulkDeleteJobs(ids) {
    return this._request('POST', '/v1.0/jobs/delete', { body: { ids } });
  }

  // Live per-stage log for a single job ({ job, events }); poll for a follow-logs view.
  getJobLog(id) {
    return this._request('GET', `/v1.0/jobs/${encodeURIComponent(id)}/log`);
  }

  // ------------------------------------------------------------- Links
  // Per-step ingestion log for a content link: one entry per ingestion run,
  // each carrying its job plus an ordered list of stage events.
  getLinkIngestionLog(linkId) {
    return this._request('GET', `/v1.0/links/${encodeURIComponent(linkId)}/log`);
  }

  // Fetch a stored pipeline artifact for a link as parsed JSON.
  // kind ∈ { atoms, chunks, vectors, subgraph }. Throws ApiError with status 404
  // when the stage hasn't produced output for this link yet.
  getLinkArtifact(id, kind) {
    return this._request('GET', `/v1.0/links/${encodeURIComponent(id)}/${kind}`);
  }

  /**
   * Fetch the raw crawled source document for a link. The content type is
   * whatever was originally crawled (HTML, PDF, binary, …), so the body is
   * returned as a Blob for opening or downloading rather than parsed as JSON.
   * Returns { blob, contentType }. Throws ApiError with status 404 when the
   * source artifact isn't present yet.
   */
  async getLinkSource(id) {
    const url = `${this.baseUrl}/v1.0/links/${encodeURIComponent(id)}/source`;
    const headers = {};
    if (this.token) headers['Authorization'] = `Bearer ${this.token}`;
    const response = await fetch(url, { method: 'GET', headers });

    if (response.status === 401) {
      window.dispatchEvent(new CustomEvent('auth:unauthorized'));
    }

    if (!response.ok) {
      const text = await response.text().catch(() => '');
      let parsed = null;
      try { parsed = text ? JSON.parse(text) : null; } catch { /* not json */ }
      throw new ApiError(response.status, text, parsed);
    }

    const blob = await response.blob();
    return { blob, contentType: response.headers.get('Content-Type') || blob.type || 'application/octet-stream' };
  }

  // Available embedding/completion model endpoints.
  // Shape: { embedding: [{id,name,model,provider,active}], completion: [{...}] }.
  listIngestionEndpoints() {
    return this._request('GET', '/v1.0/ingestion/endpoints');
  }

  // Search a subject's ingested documents (RecallDB), paginated and ranked by score.
  // Returns an EnumerationResult of { documentId, score, snippet, linkId, linkUrl, linkTitle, nodeId }.
  searchSubjectDocuments(subjectId, query, { maxResults = 20, skip = 0 } = {}) {
    return this._request('GET', `/v1.0/subjects/${encodeURIComponent(subjectId)}/search`, {
      query: { q: query, maxResults, skip }
    });
  }

  // Warm the subject's embedding model ahead of a search so the first query does not pay the model's
  // cold-load cost. Returns { success, modelId, modelName, ready, elapsedMs }.
  warmSubjectEmbedding(subjectId) {
    return this._request('POST', `/v1.0/subjects/${encodeURIComponent(subjectId)}/search/warmup`);
  }

  // Vector collections (RecallDB). Returns an EnumerationResult of { id, name, description, dimensionality, active }.
  listCollections() {
    return this._request('GET', '/v1.0/collections');
  }

  // Create a vector collection. body: { name, description?, dimensionality }.
  createCollection(body) {
    return this._request('PUT', '/v1.0/collections', { body });
  }

  // Delete a vector collection and all of its documents.
  deleteCollection(id) {
    return this._request('DELETE', `/v1.0/collections/${encodeURIComponent(id)}`);
  }

  // Health of every model endpoint (deduplicated by base URL). Array of health status objects.
  getModelRunnerHealth() {
    return this._request('GET', '/v1.0/model-runners/health');
  }

  // Health of a single model endpoint by id.
  getModelRunnerHealthById(id) {
    return this._request('GET', `/v1.0/model-runners/${encodeURIComponent(id)}/health`);
  }

  // Actively validate a model endpoint end to end (completion + tool calling, or embedding). Returns a
  // validation result with per-check outcomes.
  validateModelRunner(id, type) {
    const q = type ? `?type=${encodeURIComponent(type)}` : '';
    return this._request('POST', `/v1.0/model-runners/${encodeURIComponent(id)}/validate${q}`);
  }

  // Run a single health probe against a model endpoint immediately (no request body). Returns the endpoint
  // health object (same shape as getModelRunnerHealthById). Throws ApiError with status 400 when health
  // checks are disabled for the endpoint, or 404 when the endpoint is not found.
  runModelEndpointHealthCheck(id) {
    return this._request('POST', `/v1.0/model-runners/${encodeURIComponent(id)}/health/check`);
  }

  // Enqueue ingestion for many URLs at once for a single subject.
  bulkSubmitLinks(subjectId, body) {
    return this._request('POST', `/v1.0/subjects/${encodeURIComponent(subjectId)}/links/bulk`, { body });
  }

  // Reingest a single link: queues a FRESH ingestion job (a full re-run), even if the link has no prior job.
  // Returns 202 Accepted with the created job.
  reingestLink(id) {
    return this._request('POST', `/v1.0/links/${encodeURIComponent(id)}/reingest`);
  }

  // Bulk reingest: queues a fresh ingestion job per link. Returns 202 Accepted with { queued, skipped }.
  bulkReingestLinks(ids) {
    return this._request('POST', '/v1.0/links/reingest', { body: { ids } });
  }

  // Set a link's scheduled refresh. body: { refreshIntervalMinutes } (0 off, 60 to 525600) or { useSubjectDefault: true }.
  setLinkRefresh(id, body) {
    return this._request('PUT', `/v1.0/links/${encodeURIComponent(id)}`, { body });
  }

  // Set the scheduled refresh of several links. Returns { updated, skipped } (skipped lists ids that cannot be refreshed).
  bulkSetLinkRefresh(ids, body) {
    return this._request('POST', '/v1.0/links/refresh-interval', { body: { ...body, ids } });
  }

  // Check a link for changes now (conditional GET). Returns { outcome, jobId, message, nextRefreshUtc }.
  refreshLinkNow(id) {
    return this._request('POST', `/v1.0/links/${encodeURIComponent(id)}/refresh`);
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

  // ------------------------------------------------------------- Prompts
  // Per-subject prompt catalog: every prompt key with its effective content, the global
  // default, any subject override, the resolved source, and the override merge mode.
  getSubjectPrompts(subjectId) {
    return this._request('GET', `/v1.0/subjects/${encodeURIComponent(subjectId)}/prompts`);
  }

  // Set (or clear) a subject-level override for one prompt key. body: { content, mergeMode }.
  // An empty content clears the override so the subject reverts to the global default.
  updateSubjectPrompt(subjectId, key, body) {
    return this._request('PUT', `/v1.0/subjects/${encodeURIComponent(subjectId)}/prompts/${encodeURIComponent(key)}`, { body });
  }

  // Remove a subject-level override for one prompt key, reverting to the global default.
  deleteSubjectPrompt(subjectId, key) {
    return this._request('DELETE', `/v1.0/subjects/${encodeURIComponent(subjectId)}/prompts/${encodeURIComponent(key)}`);
  }

  // -------------------------------------------------------- Assignments
  listAssignments(userId) {
    return this._request('GET', '/v1.0/assignments', { query: { userId } });
  }

  // ------------------------------------------------------------- Audit
  listAudit(query = null) {
    return this._request('GET', '/v1.0/audit', { query: query || { maxResults: 100, order: 'desc' } });
  }

  // ------------------------------------------------------------- Settings
  getSettings() {
    return this._request('GET', '/v1.0/settings');
  }

  updateSettings(settings) {
    return this._request('PUT', '/v1.0/settings', { body: settings });
  }

  // Dashboard-tunable ingestion concurrency (system defaults). Returns an IngestionTuning object with
  // per-stage caps plus the job pool, summarization, and timeout knobs. Admin-only.
  getIngestionSettings() {
    return this._request('GET', '/v1.0/settings/ingestion');
  }

  // Persist and apply (live, no restart) the ingestion concurrency defaults. body is the same shape
  // returned by getIngestionSettings().
  updateIngestionSettings(body) {
    return this._request('PUT', '/v1.0/settings/ingestion', { body });
  }

  /**
   * Execute an arbitrary request from the API Explorer. Returns the raw Response
   * so the caller can inspect status, headers, and streaming bodies.
   */
  executeExplorer({ method, path, query, headers, body }) {
    const url = `${this.baseUrl}${path}${this.buildQuery(query)}`;
    const init = { method, headers: this._headers(headers || {}) };
    if (body !== null && body !== undefined && body !== '' && method !== 'GET' && method !== 'HEAD') {
      init.body = body;
    }
    return fetch(url, init);
  }

  // ---- Conversation threads -------------------------------------------

  listThreads(subjectId = null) {
    return this._request('GET', '/v1.0/threads', { query: { maxResults: 1000, ...(subjectId ? { subjectId } : {}) } });
  }

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

  // ---- Chat history + feedback ----------------------------------------

  listHistory(subjectId = null) {
    return this._request('GET', '/v1.0/history', { query: { maxResults: 1000, ...(subjectId ? { subjectId } : {}) } });
  }

  getHistoryTurn(id) {
    return this._request('GET', `/v1.0/history/${encodeURIComponent(id)}`);
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

  listFeedback(subjectId = null) {
    return this._request('GET', '/v1.0/feedback', { query: { maxResults: 1000, ...(subjectId ? { subjectId } : {}) } });
  }

  submitFeedback(turnId, rating, comment = null) {
    return this._request('POST', '/v1.0/feedback', { body: { turnId, rating, comment } });
  }
}

/**
 * Normalize a list response into { items, totalCount }. Supports plain arrays,
 * the Pneuma EnumerationResult envelope ({ objects, totalRecords, ... }), and other
 * common wrapper shapes (items / data / Data / results).
 */
export function normalizeList(response) {
  if (Array.isArray(response)) {
    return { items: response, totalCount: response.length };
  }
  if (response && typeof response === 'object') {
    const items = response.objects || response.Objects ||
      response.items || response.Items || response.data || response.Data ||
      response.results || response.Results || [];
    const totalCount = response.totalRecords ?? response.TotalRecords ??
      response.totalCount ?? response.TotalCount ??
      response.total ?? response.Total ?? (Array.isArray(items) ? items.length : 0);
    return { items: Array.isArray(items) ? items : [], totalCount };
  }
  return { items: [], totalCount: 0 };
}

export default ApiClient;
