/**
 * Pneuma JavaScript SDK
 *
 * A dependency-free SDK for the Pneuma REST API, built on the Node.js built-in
 * `fetch` (Node 18+). Exposes an {@link PneumaClient} class covering the full API
 * surface described in REST_API.md, and an {@link PneumaError} thrown on non-2xx
 * responses.
 *
 * @module pneuma-sdk
 */

/**
 * Error thrown when the Pneuma API returns a non-2xx status code.
 */
export class PneumaError extends Error {
    /**
     * @param {number} status - HTTP status code
     * @param {*} body - Parsed response body (object, string, or null)
     */
    constructor(status, body) {
        const code = body && typeof body === 'object' ? body.error : undefined;
        const text = body && typeof body === 'object' ? body.message : undefined;
        super(text || code || `Pneuma request failed with status ${status}`);
        this.name = 'PneumaError';
        /** @type {number} HTTP status code */
        this.status = status;
        /** @type {*} Parsed response body */
        this.body = body;
    }
}

/**
 * Normalize a base URL, forcing loopback host names to 127.0.0.1 and trimming
 * any trailing slashes.
 * @param {string} baseUrl
 * @returns {string}
 */
function normalizeBaseUrl(baseUrl) {
    let url = (baseUrl || 'http://127.0.0.1:8080').trim();
    // Force loopback host names to 127.0.0.1 (never "localhost").
    url = url.replace(/^(https?:\/\/)localhost(?=[:/]|$)/i, '$1127.0.0.1');
    return url.replace(/\/+$/, '');
}

/**
 * Build a query string from a plain object, skipping null/undefined values.
 * @param {object} [params]
 * @returns {string} Query string beginning with '?' or empty string.
 */
function buildQuery(params) {
    if (!params) return '';
    const usp = new URLSearchParams();
    for (const [key, value] of Object.entries(params)) {
        if (value === null || value === undefined) continue;
        usp.append(key, String(value));
    }
    const s = usp.toString();
    return s ? `?${s}` : '';
}

/**
 * Client for the Pneuma REST API.
 *
 * All authenticated calls send `Authorization: Bearer <token>`. Obtain a token
 * with {@link PneumaClient#login}. Methods return the parsed JSON body (or `null`
 * for 204 No Content) and throw {@link PneumaError} on any non-2xx response.
 */
export class PneumaClient {
    /**
     * @param {string} [baseUrl='http://127.0.0.1:8080'] - Server base URL. Loopback
     *   host names are normalized to 127.0.0.1.
     * @param {string} [token] - Optional pre-existing session token.
     */
    constructor(baseUrl = 'http://127.0.0.1:8080', token = null) {
        /** @type {string} */
        this.baseUrl = normalizeBaseUrl(baseUrl);
        /** @type {string|null} */
        this.token = token || null;
    }

    /**
     * Perform an HTTP request against the API.
     * @param {string} method - HTTP method
     * @param {string} path - Path beginning with '/'
     * @param {object} [options]
     * @param {*} [options.body] - JSON-serializable request body
     * @param {object} [options.query] - Query parameters
     * @param {boolean} [options.auth=true] - Whether to send the bearer token
     * @param {object} [options.headers] - Additional headers
     * @returns {Promise<*>} Parsed JSON body, or null for 204/empty responses.
     * @throws {PneumaError} On any non-2xx status.
     */
    async request(method, path, options = {}) {
        const { body, query, auth = true, headers = {} } = options;
        const url = `${this.baseUrl}${path}${buildQuery(query)}`;

        const finalHeaders = { Accept: 'application/json', ...headers };
        if (auth && this.token) {
            finalHeaders.Authorization = `Bearer ${this.token}`;
        }

        const init = { method, headers: finalHeaders };
        if (body !== undefined && body !== null) {
            finalHeaders['Content-Type'] = 'application/json';
            init.body = JSON.stringify(body);
        }

        const response = await fetch(url, init);

        // Parse the body once. 204 (or empty) yields null.
        let parsed = null;
        if (response.status !== 204) {
            const text = await response.text();
            if (text) {
                try {
                    parsed = JSON.parse(text);
                } catch {
                    parsed = text;
                }
            }
        }

        if (!response.ok) {
            throw new PneumaError(response.status, parsed);
        }
        return parsed;
    }

    // ==================== System ====================

    /**
     * Health check.
     * @returns {Promise<object>} `{ status, serviceName, version, timeUtc }`
     */
    health() {
        return this.request('GET', '/v1.0/api/health', { auth: false });
    }

    // ==================== Tokens ====================

    /**
     * Create a session token and store it on the client.
     * @param {string} email
     * @param {string} password
     * @param {string} [tenantId]
     * @returns {Promise<object>} Token response including `token`, `userId`, etc.
     */
    async login(email, password, tenantId) {
        const body = { email, password };
        if (tenantId !== undefined && tenantId !== null) body.tenantId = tenantId;
        const result = await this.request('POST', '/v1.0/token', { body, auth: false });
        if (result && result.token) {
            this.token = result.token;
        }
        return result;
    }

    /**
     * Validate the current token; returns the principal summary.
     * @returns {Promise<object>}
     */
    validateToken() {
        return this.request('GET', '/v1.0/token');
    }

    /**
     * Retrieve the decoded authentication context for the current token.
     * @returns {Promise<object>}
     */
    tokenDetails() {
        return this.request('GET', '/v1.0/token/details');
    }

    /**
     * Revoke the current session (logout) and clear the stored token.
     * @returns {Promise<null>}
     */
    async logout() {
        const result = await this.request('DELETE', '/v1.0/token');
        this.token = null;
        return result;
    }

    // ==================== Tenants ====================

    /**
     * List tenants (paginated).
     * @param {object} [options] `{ maxResults, skip, order, search }`
     * @returns {Promise<object>} EnumerationResult envelope; records are in `.objects`.
     */
    listTenants(options = {}) {
        return this.request('GET', '/v1.0/tenants', { query: options });
    }

    /** @param {object} tenant @returns {Promise<object>} */
    createTenant(tenant) {
        return this.request('POST', '/v1.0/tenants', { body: tenant });
    }

    /** @param {string} id @returns {Promise<object>} */
    getTenant(id) {
        return this.request('GET', `/v1.0/tenants/${encodeURIComponent(id)}`);
    }

    /** @param {string} id @param {object} tenant @returns {Promise<object>} */
    updateTenant(id, tenant) {
        return this.request('PUT', `/v1.0/tenants/${encodeURIComponent(id)}`, { body: tenant });
    }

    /** @param {string} id @returns {Promise<null>} */
    deleteTenant(id) {
        return this.request('DELETE', `/v1.0/tenants/${encodeURIComponent(id)}`);
    }

    // ==================== Users ====================

    /**
     * List users (paginated).
     * @param {string} [tenantId] admin-only tenant scope
     * @param {object} [options] `{ maxResults, skip, order, search }`
     * @returns {Promise<object>} EnumerationResult envelope; records are in `.objects`.
     */
    listUsers(tenantId, options = {}) {
        return this.request('GET', '/v1.0/users', { query: { tenantId, ...options } });
    }

    /** @param {object} user CreateUserRequest @returns {Promise<object>} */
    createUser(user) {
        return this.request('POST', '/v1.0/users', { body: user });
    }

    /** @param {string} id @returns {Promise<object>} */
    getUser(id) {
        return this.request('GET', `/v1.0/users/${encodeURIComponent(id)}`);
    }

    /** @param {string} id @param {object} user @returns {Promise<object>} */
    updateUser(id, user) {
        return this.request('PUT', `/v1.0/users/${encodeURIComponent(id)}`, { body: user });
    }

    /** @param {string} id @returns {Promise<null>} */
    deleteUser(id) {
        return this.request('DELETE', `/v1.0/users/${encodeURIComponent(id)}`);
    }

    // ==================== Credentials ====================

    /**
     * List credentials (paginated).
     * @param {object} [options] `{ maxResults, skip, order, search }`
     * @returns {Promise<object>} EnumerationResult envelope; records are in `.objects`.
     */
    listCredentials(options = {}) {
        return this.request('GET', '/v1.0/credentials', { query: options });
    }

    /**
     * Create a credential. The raw `secretKey` is returned only once.
     * @param {object} credential `{ name, userId?, expiresUtc? }`
     * @returns {Promise<object>}
     */
    createCredential(credential) {
        return this.request('POST', '/v1.0/credentials', { body: credential });
    }

    /** @param {string} id @returns {Promise<object>} */
    getCredential(id) {
        return this.request('GET', `/v1.0/credentials/${encodeURIComponent(id)}`);
    }

    /** @param {string} id @returns {Promise<null>} */
    deleteCredential(id) {
        return this.request('DELETE', `/v1.0/credentials/${encodeURIComponent(id)}`);
    }

    // ==================== Roles (RBAC) ====================

    /**
     * List roles (paginated).
     * @param {object} [options] `{ maxResults, skip, order, search }`
     * @returns {Promise<object>} EnumerationResult envelope; records are in `.objects`.
     */
    listRoles(options = {}) {
        return this.request('GET', '/v1.0/roles', { query: options });
    }

    /** @param {object} role @returns {Promise<object>} */
    createRole(role) {
        return this.request('POST', '/v1.0/roles', { body: role });
    }

    /** @param {string} id @returns {Promise<object>} */
    getRole(id) {
        return this.request('GET', `/v1.0/roles/${encodeURIComponent(id)}`);
    }

    /** @param {string} id @param {object} role @returns {Promise<object>} */
    updateRole(id, role) {
        return this.request('PUT', `/v1.0/roles/${encodeURIComponent(id)}`, { body: role });
    }

    /** @param {string} id @returns {Promise<null>} */
    deleteRole(id) {
        return this.request('DELETE', `/v1.0/roles/${encodeURIComponent(id)}`);
    }

    // ==================== Permissions (RBAC) ====================

    /**
     * List permissions (paginated).
     * @param {object} [options] `{ maxResults, skip, order, search }`
     * @returns {Promise<object>} EnumerationResult envelope; records are in `.objects`.
     */
    listPermissions(options = {}) {
        return this.request('GET', '/v1.0/permissions', { query: options });
    }

    /** @param {object} permission @returns {Promise<object>} */
    createPermission(permission) {
        return this.request('POST', '/v1.0/permissions', { body: permission });
    }

    /** @param {string} id @returns {Promise<object>} */
    getPermission(id) {
        return this.request('GET', `/v1.0/permissions/${encodeURIComponent(id)}`);
    }

    /** @param {string} id @param {object} permission @returns {Promise<object>} */
    updatePermission(id, permission) {
        return this.request('PUT', `/v1.0/permissions/${encodeURIComponent(id)}`, { body: permission });
    }

    /** @param {string} id @returns {Promise<null>} */
    deletePermission(id) {
        return this.request('DELETE', `/v1.0/permissions/${encodeURIComponent(id)}`);
    }

    // ==================== Assignments (RBAC) ====================

    /**
     * List role assignments for a user (paginated).
     * @param {string} userId
     * @param {object} [options] `{ maxResults, skip, order, search }`
     * @returns {Promise<object>} EnumerationResult envelope; records are in `.objects`.
     */
    listAssignments(userId, options = {}) {
        return this.request('GET', '/v1.0/assignments', { query: { userId, ...options } });
    }

    /** @param {object} assignment @returns {Promise<object>} */
    createAssignment(assignment) {
        return this.request('POST', '/v1.0/assignments', { body: assignment });
    }

    /** @param {string} id @returns {Promise<null>} */
    deleteAssignment(id) {
        return this.request('DELETE', `/v1.0/assignments/${encodeURIComponent(id)}`);
    }

    // ==================== Audit ====================

    /**
     * Recent security events (paginated).
     * @param {string} [tenantId] admin-only tenant scope
     * @param {object} [options] `{ maxResults, skip, order, search }`
     * @returns {Promise<object>} EnumerationResult envelope; records are in `.objects`.
     */
    listAudit(tenantId, options = {}) {
        return this.request('GET', '/v1.0/audit', { query: { tenantId, ...options } });
    }

    // ==================== Subjects ====================

    /**
     * List subjects (paginated).
     * @param {object} [options] `{ maxResults, skip, order, search }`
     * @returns {Promise<object>} EnumerationResult envelope; records are in `.objects`.
     */
    listSubjects(options = {}) {
        return this.request('GET', '/v1.0/subjects', { query: options });
    }

    /** @param {object} subject @returns {Promise<object>} */
    createSubject(subject) {
        return this.request('POST', '/v1.0/subjects', { body: subject });
    }

    /** @param {string} id @returns {Promise<object>} */
    getSubject(id) {
        return this.request('GET', `/v1.0/subjects/${encodeURIComponent(id)}`);
    }

    /** @param {string} id @param {object} subject @returns {Promise<object>} */
    updateSubject(id, subject) {
        return this.request('PUT', `/v1.0/subjects/${encodeURIComponent(id)}`, { body: subject });
    }

    /** @param {string} id @returns {Promise<null>} */
    deleteSubject(id) {
        return this.request('DELETE', `/v1.0/subjects/${encodeURIComponent(id)}`);
    }

    // ==================== Content Links & Ingestion ====================

    /**
     * Submit a link for a subject, enqueuing an ingestion job.
     * @param {string} subjectId
     * @param {{ url: string, title?: string, embeddingEndpointId: string, completionEndpointId: string }} link
     * @returns {Promise<object>}
     */
    submitLink(subjectId, link) {
        return this.request('POST', `/v1.0/subjects/${encodeURIComponent(subjectId)}/links`, { body: link });
    }

    /**
     * Submit multiple links for a subject in a single call, enqueuing one ingestion job per URL.
     * @param {string} subjectId
     * @param {{ urls: string[], embeddingEndpointId: string, completionEndpointId: string }} body
     * @returns {Promise<{ created: number, links: object[] }>}
     */
    submitLinks(subjectId, body) {
        return this.request('POST', `/v1.0/subjects/${encodeURIComponent(subjectId)}/links/bulk`, { body });
    }

    /**
     * List the Partio endpoints available for ingestion, grouped by usage.
     * @returns {Promise<{ embedding: object[], completion: object[] }>}
     */
    listIngestionEndpoints() {
        return this.request('GET', '/v1.0/ingestion/endpoints');
    }

    /**
     * List a subject's links (paginated).
     * @param {string} subjectId
     * @param {object} [options] `{ maxResults, skip, order, search }`
     * @returns {Promise<object>} EnumerationResult envelope; records are in `.objects`.
     */
    listSubjectLinks(subjectId, options = {}) {
        return this.request('GET', `/v1.0/subjects/${encodeURIComponent(subjectId)}/links`, { query: options });
    }

    /**
     * List all links (paginated).
     * @param {object} [options] `{ maxResults, skip, order, search }`
     * @returns {Promise<object>} EnumerationResult envelope; records are in `.objects`.
     */
    listLinks(options = {}) {
        return this.request('GET', '/v1.0/links', { query: options });
    }

    /** @param {string} id @returns {Promise<object>} */
    getLink(id) {
        return this.request('GET', `/v1.0/links/${encodeURIComponent(id)}`);
    }

    /**
     * Get a link's per-step ingestion log (one entry per ingestion run, each with its events).
     * @param {string} id @returns {Promise<object[]>}
     */
    getLinkIngestionLog(id) {
        return this.request('GET', `/v1.0/links/${encodeURIComponent(id)}/log`);
    }

    /**
     * Get a link's stored DocumentAtom semantic cells (atoms) pipeline artifact.
     * Rejects with a 404 error if that stage hasn't produced output yet.
     * @param {string} id @returns {Promise<object>}
     */
    getLinkAtoms(id) {
        return this.request('GET', `/v1.0/links/${encodeURIComponent(id)}/atoms`);
    }

    /**
     * Get a link's stored Partio chunks pipeline artifact.
     * Rejects with a 404 error if that stage hasn't produced output yet.
     * @param {string} id @returns {Promise<object>}
     */
    getLinkChunks(id) {
        return this.request('GET', `/v1.0/links/${encodeURIComponent(id)}/chunks`);
    }

    /**
     * Get a link's stored Partio embeddings (vectors) pipeline artifact.
     * Rejects with a 404 error if that stage hasn't produced output yet.
     * @param {string} id @returns {Promise<object>}
     */
    getLinkVectors(id) {
        return this.request('GET', `/v1.0/links/${encodeURIComponent(id)}/vectors`);
    }

    /**
     * Get a link's stored candidate subgraph pipeline artifact.
     * Rejects with a 404 error if that stage hasn't produced output yet.
     * @param {string} id @returns {Promise<object>}
     */
    getLinkSubgraph(id) {
        return this.request('GET', `/v1.0/links/${encodeURIComponent(id)}/subgraph`);
    }

    /** @param {string} id @returns {Promise<null>} */
    deleteLink(id) {
        return this.request('DELETE', `/v1.0/links/${encodeURIComponent(id)}`);
    }

    // ==================== Jobs ====================

    /**
     * List ingestion jobs (paginated).
     * @param {string} [status] optional status filter
     * @param {object} [options] `{ maxResults, skip, order, search }`
     * @returns {Promise<object>} EnumerationResult envelope; records are in `.objects`.
     */
    listJobs(status, options = {}) {
        return this.request('GET', '/v1.0/jobs', { query: { status, ...options } });
    }

    /**
     * Job detail with per-stage events.
     * @param {string} id
     * @returns {Promise<{ job: object, events: object[] }>}
     */
    getJob(id) {
        return this.request('GET', `/v1.0/jobs/${encodeURIComponent(id)}`);
    }

    /**
     * Requeue a failed job.
     * @param {string} id
     * @returns {Promise<object>}
     */
    restartJob(id) {
        return this.request('POST', `/v1.0/jobs/${encodeURIComponent(id)}/restart`);
    }

    /**
     * Stop (cancel) a queued or in-flight ingestion job.
     * @param {string} id
     * @returns {Promise<object>} The updated (cancelled) job.
     */
    stopJob(id) {
        return this.request('POST', `/v1.0/jobs/${encodeURIComponent(id)}/stop`);
    }

    /**
     * Get a job's live per-stage log ({ job, events }); poll this for a follow-logs view.
     * @param {string} id
     * @returns {Promise<object>}
     */
    getJobLog(id) {
        return this.request('GET', `/v1.0/jobs/${encodeURIComponent(id)}/log`);
    }

    // ==================== Model Runners ====================

    /**
     * List model runners (paginated).
     * @param {object} [options] `{ maxResults, skip, order, search }`
     * @returns {Promise<object>} EnumerationResult envelope; records are in `.objects`.
     */
    listModelRunners(options = {}) {
        return this.request('GET', '/v1.0/model-runners', { query: options });
    }

    /** @param {object} runner CreateModelRunnerRequest @returns {Promise<object>} */
    createModelRunner(runner) {
        return this.request('POST', '/v1.0/model-runners', { body: runner });
    }

    /** @param {string} id @returns {Promise<object>} */
    getModelRunner(id) {
        return this.request('GET', `/v1.0/model-runners/${encodeURIComponent(id)}`);
    }

    /** @param {string} id @param {object} runner @returns {Promise<object>} */
    updateModelRunner(id, runner) {
        return this.request('PUT', `/v1.0/model-runners/${encodeURIComponent(id)}`, { body: runner });
    }

    /** @param {string} id @returns {Promise<null>} */
    deleteModelRunner(id) {
        return this.request('DELETE', `/v1.0/model-runners/${encodeURIComponent(id)}`);
    }

    // ==================== Prompts ====================

    /**
     * List prompts (paginated).
     * @param {object} [options] `{ maxResults, skip, order, search }`
     * @returns {Promise<object>} EnumerationResult envelope; records are in `.objects`.
     */
    listPrompts(options = {}) {
        return this.request('GET', '/v1.0/prompts', { query: options });
    }

    /** @param {object} prompt @returns {Promise<object>} */
    createPrompt(prompt) {
        return this.request('POST', '/v1.0/prompts', { body: prompt });
    }

    /** @param {string} id @returns {Promise<object>} */
    getPrompt(id) {
        return this.request('GET', `/v1.0/prompts/${encodeURIComponent(id)}`);
    }

    /** @param {string} id @param {object} prompt @returns {Promise<object>} */
    updatePrompt(id, prompt) {
        return this.request('PUT', `/v1.0/prompts/${encodeURIComponent(id)}`, { body: prompt });
    }

    /** @param {string} id @returns {Promise<null>} */
    deletePrompt(id) {
        return this.request('DELETE', `/v1.0/prompts/${encodeURIComponent(id)}`);
    }

    // ==================== Request History ====================

    /**
     * Paginated request-history list.
     * @param {object} [filters] `{ method, statusCode, pathContains, fromUtc, toUtc, pageNumber, pageSize, tenantId, userId }`
     * @returns {Promise<object>}
     */
    listRequestHistory(filters) {
        return this.request('GET', '/v1.0/api/request-history', { query: filters });
    }

    /**
     * Time-bucketed request-history counts.
     * @param {object} [params] `{ fromUtc, toUtc, bucketMinutes }`
     * @returns {Promise<object>}
     */
    requestHistorySummary(params) {
        return this.request('GET', '/v1.0/api/request-history/summary', { query: params });
    }

    /** @param {string} id @returns {Promise<object>} */
    getRequestHistory(id) {
        return this.request('GET', `/v1.0/api/request-history/${encodeURIComponent(id)}`);
    }

    /** @param {string} id @returns {Promise<null>} */
    deleteRequestHistory(id) {
        return this.request('DELETE', `/v1.0/api/request-history/${encodeURIComponent(id)}`);
    }

    /**
     * Bulk-delete request history matching a filter.
     * @param {object} [filters]
     * @returns {Promise<{ deletedCount: number }>}
     */
    deleteRequestHistoryBulk(filters) {
        return this.request('DELETE', '/v1.0/api/request-history', { query: filters });
    }

    // ==================== Knowledge Graph ====================

    /** @param {string} id @returns {Promise<object>} */
    getNode(id) {
        return this.request('GET', `/v1.0/graph/nodes/${encodeURIComponent(id)}`);
    }

    /** @param {string} id @returns {Promise<object>} */
    getNeighbors(id) {
        return this.request('GET', `/v1.0/graph/nodes/${encodeURIComponent(id)}/neighbors`);
    }

    /** @param {string} id @returns {Promise<object>} */
    getEdges(id) {
        return this.request('GET', `/v1.0/graph/nodes/${encodeURIComponent(id)}/edges`);
    }

    // ==================== Settings ====================

    /**
     * Retrieve system settings (system-admin only). Secret fields are masked as
     * `"********"` in the returned `settings` object.
     * @returns {Promise<object>} `{ success, settings, meta }` where `meta` is
     *   `{ sections: [{ key, label, requiresRestart }], secretFields: string[], secretMask }`.
     */
    getSettings() {
        return this.request('GET', '/v1.0/settings');
    }

    /**
     * Update system settings (system-admin only). Submitting a secret field
     * still equal to `"********"` preserves the stored value server-side.
     * @param {object} settings The settings object to persist.
     * @returns {Promise<object>} `{ success, restartRequired, message, meta }`
     */
    updateSettings(settings) {
        return this.request('PUT', '/v1.0/settings', { body: settings });
    }

    // ==================== Search & Ask ====================

    /**
     * Full-text search resolved to graph nodes.
     * @param {string} q
     * @param {number} [max=20]
     * @returns {Promise<{ query: string, results: object[] }>}
     */
    search(q, max = 20) {
        return this.request('GET', '/v1.0/search', { query: { q, max } });
    }

    /**
     * Grounded answer to a natural-language question.
     * @param {{ question: string, maxResults?: number }} body
     * @returns {Promise<{ answer: string, sources: object[], grounded: boolean }>}
     */
    query(body) {
        return this.request('POST', '/v1.0/query', { body });
    }
}

export default PneumaClient;
