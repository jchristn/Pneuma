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
