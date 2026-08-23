/**
 * Hand-rolled fetch-based API client for the Pneuma backend.
 *
 * No axios: the browser `fetch` API is enough, keeps the bundle small, and
 * matches the shared reference pattern. Every authenticated call carries the
 * bearer token; non-2xx responses throw an {@link ApiError}.
 */

import { streamSse } from './sse.js';

/** Whether a RetrievalFilter object carries no predicate (so it can be omitted from a request). */
export function isEmptyFilter(f) {
  if (!f) return true;
  const len = (a) => (Array.isArray(a) ? a.length : 0);
  return !len(f.requiredLabels) && !len(f.excludedLabels) && !len(f.requiredTags) && !len(f.excludedTags);
}

export class ApiError extends Error {
  constructor(status, message, body = null) {
    super(message || `HTTP ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.body = body;
  }
}

class ApiClient {
  /**
   * @param {string} baseUrl Base URL of the Pneuma server (trailing slash optional).
   * @param {string|null} token Bearer session token.
   */
  constructor(baseUrl, token = null) {
    this.baseUrl = (baseUrl || '').replace(/\/+$/, '');
    this.token = token || null;
  }

  _headers(extra = {}) {
    const headers = { 'Content-Type': 'application/json', ...extra };
    if (this.token) headers.Authorization = `Bearer ${this.token}`;
    return headers;
  }

  async _request(method, path, { query = null, body = null, headers = {} } = {}) {
    const url = new URL(this.baseUrl + path);
    if (query) {
      for (const [key, value] of Object.entries(query)) {
        if (value !== undefined && value !== null && value !== '') {
          url.searchParams.append(key, value);
        }
      }
    }

    const init = { method, headers: this._headers(headers) };
    if (body !== null && body !== undefined) init.body = JSON.stringify(body);

    let response;
    try {
      response = await fetch(url.toString(), init);
    } catch (networkError) {
      throw new ApiError(0, networkError?.message || 'Network request failed');
    }

    if (response.status === 401) {
      window.dispatchEvent(new CustomEvent('pneuma:unauthorized'));
    }

    const text = await response.text();
    let data = null;
    if (text) {
      try {
        data = JSON.parse(text);
      } catch {
        data = text;
      }
    }

    if (!response.ok) {
      const message = data?.message || data?.error || response.statusText || `HTTP ${response.status}`;
      throw new ApiError(response.status, message, data);
    }

    return data;
  }

  // ---- Auth + health ---------------------------------------------------

  /** Exchange email/password for a session token. */
  async login(email, password) {
    return this._request('POST', '/v1.0/token', { body: { email, password } });
  }

  /** Validate the current bearer token; returns a principal summary. */
  async validateToken() {
    return this._request('GET', '/v1.0/token');
  }

  /** Revoke the current session (logout). */
  async revokeToken() {
    return this._request('DELETE', '/v1.0/token');
  }

  async health() {
    return this._request('GET', '/v1.0/api/health');
  }

  // ---- Search & Ask ----------------------------------------------------

  /**
   * Full-text search resolved to a representative set of graph nodes.
   * @returns {Promise<{query:string, results: Array<{node:object, score:number, snippet:string}>}>}
   */
  async search(q, max = 20, { subjectId = null, metadataFilter = null } = {}) {
    const query = { q, max };
    // The search route accepts an optional URL-encoded RetrievalFilter JSON in the `filter` query param.
    if (metadataFilter && !isEmptyFilter(metadataFilter)) query.filter = JSON.stringify(metadataFilter);
    const path = subjectId ? `/v1.0/subjects/${encodeURIComponent(subjectId)}/search` : '/v1.0/search';
    return this._request('GET', path, { query });
  }

  /**
   * Grounded natural-language answer over the corpus. An optional `metadataFilter` (required/excluded labels
   * and tags) scopes retrieval to documents ingested with matching labels/tags.
   * @returns {Promise<{answer:string, sources:object[], grounded:boolean}>}
   */
  async ask(question, maxResults = 10, { subjectId = null, metadataFilter = null } = {}) {
    const body = { question, maxResults };
    if (subjectId) body.subjectId = subjectId;
    if (metadataFilter && !isEmptyFilter(metadataFilter)) body.metadataFilter = metadataFilter;
    return this._request('POST', '/v1.0/query', { body });
  }

  /**
   * Grounded natural-language answer streamed over server-sent events. Invokes
   * `onEvent` for each `metadata` / `delta` / `complete` / `error` event.
   * @param {string} question
   * @param {number} maxResults
   * @param {{onEvent:(event:object)=>void, signal?:AbortSignal, subjectId?:string, metadataFilter?:object}} handlers
   */
  async askStream(question, maxResults = 10, { onEvent, signal, subjectId = null, metadataFilter = null } = {}) {
    const body = { question, maxResults };
    if (subjectId) body.subjectId = subjectId;
    if (metadataFilter && !isEmptyFilter(metadataFilter)) body.metadataFilter = metadataFilter;
    await streamSse(this.baseUrl + '/v1.0/query/stream', {
      method: 'POST',
      headers: this._headers({ Accept: 'text/event-stream' }),
      body,
      signal,
      onEvent,
    });
  }

  /**
   * Multi-turn agentic chat over the corpus, streamed over server-sent events. The model may call
   * Pneuma's read tools while answering. Invokes `onEvent` for each `delta` / `tool_call` /
   * `tool_result` / `complete` / `error` event.
   * @param {{role:string, content:string}[]} messages
   * @param {number} maxResults
   * @param {{onEvent:(event:object)=>void, signal?:AbortSignal}} handlers
   */
  async chatStream(messages, maxResults = 8, { onEvent, signal, subjectId = null, threadId = null, metadataFilter = null } = {}) {
    await streamSse(this.baseUrl + '/v1.0/chat/stream', {
      method: 'POST',
      headers: this._headers({ Accept: 'text/event-stream' }),
      body: {
        messages,
        maxResults,
        ...(subjectId ? { subjectId } : {}),
        ...(threadId ? { threadId } : {}),
        ...(metadataFilter && !isEmptyFilter(metadataFilter) ? { metadataFilter } : {}),
      },
      signal,
      onEvent,
    });
  }

  /** Submit thumbs up/down and/or a comment on a chat answer. */
  async submitFeedback(turnId, rating, comment = null) {
    return this._request('POST', '/v1.0/feedback', { body: { turnId, rating, comment } });
  }

  // ---- Conversation threads --------------------------------------------

  /** List conversation threads (most-recently-active first), optionally scoped to a subject. */
  async listThreads(subjectId = null) {
    const query = { maxResults: 1000 };
    if (subjectId) query.subjectId = subjectId;
    return this._request('GET', '/v1.0/threads', { query });
  }

  /** Read a thread and its turns (oldest first): `{ thread, turns }`. */
  async getThread(id) {
    return this._request('GET', `/v1.0/threads/${encodeURIComponent(id)}`);
  }

  /** Rename a thread. */
  async renameThread(id, title) {
    return this._request('PUT', `/v1.0/threads/${encodeURIComponent(id)}`, { body: { title } });
  }

  /** Delete a thread and cascade its turns/tool-calls/feedback. */
  async deleteThread(id) {
    return this._request('DELETE', `/v1.0/threads/${encodeURIComponent(id)}`);
  }

  /** Warm the subject's answering model (best-effort) so the first question isn't slow to first token. */
  async warmup(subjectId = null) {
    return this._request('POST', '/v1.0/warmup', { body: subjectId ? { subjectId } : {} });
  }

  // ---- Subjects --------------------------------------------------------

  /** List the subjects available to this user (tenant-scoped). */
  async getSubjects() {
    return this._request('GET', '/v1.0/subjects', { query: { maxResults: 1000 } });
  }

  /** Resolve a subject by its URL slug. */
  async getSubjectBySlug(slug) {
    return this._request('GET', `/v1.0/subjects/by-slug/${encodeURIComponent(slug)}`);
  }

  // ---- Knowledge graph -------------------------------------------------

  /** Node contents. */
  async getNode(id) {
    return this._request('GET', `/v1.0/graph/nodes/${encodeURIComponent(id)}`);
  }

  /** Adjacent nodes. */
  async getNeighbors(id) {
    return this._request('GET', `/v1.0/graph/nodes/${encodeURIComponent(id)}/neighbors`);
  }

  /** Relationships (edges) for the node. */
  async getEdges(id) {
    return this._request('GET', `/v1.0/graph/nodes/${encodeURIComponent(id)}/edges`);
  }
}

export default ApiClient;
