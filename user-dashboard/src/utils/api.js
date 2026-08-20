/**
 * Hand-rolled fetch-based API client for the Pneuma backend.
 *
 * No axios: the browser `fetch` API is enough, keeps the bundle small, and
 * matches the shared reference pattern. Every authenticated call carries the
 * bearer token; non-2xx responses throw an {@link ApiError}.
 */

import { streamSse } from './sse.js';

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
  async search(q, max = 20) {
    return this._request('GET', '/v1.0/search', { query: { q, max } });
  }

  /**
   * Grounded natural-language answer over the corpus.
   * @returns {Promise<{answer:string, sources:object[], grounded:boolean}>}
   */
  async ask(question, maxResults = 10) {
    return this._request('POST', '/v1.0/query', { body: { question, maxResults } });
  }

  /**
   * Grounded natural-language answer streamed over server-sent events. Invokes
   * `onEvent` for each `metadata` / `delta` / `complete` / `error` event.
   * @param {string} question
   * @param {number} maxResults
   * @param {{onEvent:(event:object)=>void, signal?:AbortSignal}} handlers
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
   * `tool_result` / `complete` / `error` event.
   * @param {{role:string, content:string}[]} messages
   * @param {number} maxResults
   * @param {{onEvent:(event:object)=>void, signal?:AbortSignal}} handlers
   */
  async chatStream(messages, maxResults = 8, { onEvent, signal } = {}) {
    await streamSse(this.baseUrl + '/v1.0/chat/stream', {
      method: 'POST',
      headers: this._headers({ Accept: 'text/event-stream' }),
      body: { messages, maxResults },
      signal,
      onEvent,
    });
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
