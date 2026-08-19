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
  // Submitting a link enqueues an ingestion job server-side. The backend requires an embedding
  // endpoint, a completion endpoint, and a target collection.
  async submitLink(subjectId, { url, title, embeddingEndpointId, completionEndpointId, collectionId }) {
    const body = { url };
    if (title) body.title = title;
    if (embeddingEndpointId) body.embeddingEndpointId = embeddingEndpointId;
    if (completionEndpointId) body.completionEndpointId = completionEndpointId;
    if (collectionId) body.collectionId = collectionId;
    return this._request('POST', `/v1.0/subjects/${encodeURIComponent(subjectId)}/links`, { body });
  }
  // Bulk submit multiple links for a subject in a single request.
  async bulkSubmitLinks(subjectId, { urls, embeddingEndpointId, completionEndpointId, collectionId }) {
    const body = { urls, embeddingEndpointId, completionEndpointId, collectionId };
    return this._request('POST', `/v1.0/subjects/${encodeURIComponent(subjectId)}/links/bulk`, { body });
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
}

export default ApiClient;
export { ApiError };
