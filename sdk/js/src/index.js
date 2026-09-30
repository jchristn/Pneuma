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
     * @param {string} [options.rawBody] - A non-JSON request body sent as-is (for example a Turtle document)
     * @param {string} [options.contentType] - Content type of `rawBody`
     * @returns {Promise<*>} Parsed JSON body, or null for 204/empty responses.
     * @throws {PneumaError} On any non-2xx status.
     */
    async request(method, path, options = {}) {
        const { body, query, auth = true, headers = {}, rawBody, contentType } = options;
        const url = `${this.baseUrl}${path}${buildQuery(query)}`;

        const finalHeaders = { Accept: 'application/json', ...headers };
        if (auth && this.token) {
            finalHeaders.Authorization = `Bearer ${this.token}`;
        }

        const init = { method, headers: finalHeaders };
        if (typeof rawBody === 'string') {
            finalHeaders['Content-Type'] = contentType || 'text/plain';
            init.body = rawBody;
        } else if (body !== undefined && body !== null) {
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
     * Submit a link for a subject, enqueuing an ingestion job. The embedding/inference models and collection
     * used for ingestion are taken from the subject, so only the URL (and optional title) are supplied here.
     * The subject must have those configured or the request fails with 400. Optional `labels` (strings) and
     * `tags` (key/value map) are attached to every chunk and to the link's source graph node, so retrieval can
     * later be scoped to them.
     * @param {string} subjectId
     * @param {{ url: string, title?: string, labels?: string[], tags?: Record<string,string> }} link
     * @returns {Promise<object>}
     */
    submitLink(subjectId, link) {
        return this.request('POST', `/v1.0/subjects/${encodeURIComponent(subjectId)}/links`, { body: link });
    }

    /**
     * Push content (text, Markdown, HTML, or JSON) into a subject; it is stored and ingested like a link. Reusing an
     * `externalKey` replaces earlier content with that key instead of adding a duplicate.
     * @param {string} subjectId
     * @param {{ content: string, contentType: string, title?: string, externalKey?: string, labels?: string[], tags?: Record<string,string> }} item
     * @returns {Promise<{ index: number, statusCode: number, replaced: boolean, link: object, jobId: string }>}
     */
    submitContent(subjectId, item) {
        return this.request('POST', `/v1.0/subjects/${encodeURIComponent(subjectId)}/content`, { body: item });
    }

    /**
     * Push up to 100 content items in one call; every item is attempted and reported.
     * @param {string} subjectId
     * @param {object[]} items items shaped like `submitContent`'s
     * @returns {Promise<{ accepted: number, rejected: number, results: object[] }>}
     */
    submitContentBatch(subjectId, items) {
        return this.request('POST', `/v1.0/subjects/${encodeURIComponent(subjectId)}/content/batch`, { body: { items } });
    }

    /**
     * Submit multiple links for a subject in a single call, enqueuing one ingestion job per URL. The models and
     * collection are taken from the subject. Any `labels`/`tags` are applied to every URL in the batch.
     * @param {string} subjectId
     * @param {{ urls: string[], labels?: string[], tags?: Record<string,string> }} body
     * @returns {Promise<{ created: number, links: object[] }>}
     */
    submitLinks(subjectId, body) {
        return this.request('POST', `/v1.0/subjects/${encodeURIComponent(subjectId)}/links/bulk`, { body });
    }

    /**
     * List the model endpoints available for ingestion, grouped by usage.
     * Each entry has `id`, `type` ("Embedding" | "Completion"), `name`, `model`,
     * `endpoint`, `apiFormat`, `provider` (OpenAI | OpenAICompatible | Gemini |
     * Ollama | AzureOpenAI | Anthropic | Bedrock | VoyageAI | VertexAI),
     * `deployment`, `apiVersion`, `region`, `project`, `accessKeyId` and `active`.
     * Secret material (`apiKey`, `secretAccessKey`, `sessionToken`) is write-only
     * and never returned.
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
    // ---- Crawl plans and crawl operations ----

    /** List the crawl plan types this server supports, each with its settings schema. @returns {Promise<object[]>} */
    listCrawlPlanTypes() {
        return this.request('GET', '/v1.0/crawl-plan-types');
    }

    /**
     * Create a crawl plan that keeps a subject in sync with a source. Secret settings (passwords, keys, tokens) are
     * write-only: they are stored encrypted and never returned; `secretsSet` names the ones stored.
     * @param {string} subjectId
     * @param {object} plan `{ name, type, web|sitemap|s3|cifs|nfs, filter?, schedule?, processAdditions?, processUpdates?, processDeletions?, maxDeletionFraction?, labels?, tags? }`
     * @returns {Promise<object>}
     */
    createCrawlPlan(subjectId, plan) {
        return this.request('POST', `/v1.0/subjects/${encodeURIComponent(subjectId)}/crawl-plans`, { body: plan });
    }

    /** List crawl plans; pass `{ subjectId }` to scope to a subject. @param {object} [options] @returns {Promise<object>} */
    listCrawlPlans(options = {}) {
        return this.request('GET', '/v1.0/crawl-plans', { query: options });
    }

    /** @param {string} id @returns {Promise<object>} */
    getCrawlPlan(id) {
        return this.request('GET', `/v1.0/crawl-plans/${encodeURIComponent(id)}`);
    }

    /**
     * Replace a crawl plan's configuration. Secrets left out keep their stored values; list names in
     * `clearSecrets` to remove them. The type cannot change.
     * @param {string} id @param {object} plan @returns {Promise<object>}
     */
    updateCrawlPlan(id, plan) {
        return this.request('PUT', `/v1.0/crawl-plans/${encodeURIComponent(id)}`, { body: plan });
    }

    /** Delete a crawl plan; `deleteLinks` also deletes the links it created. @param {string} id @param {boolean} [deleteLinks] */
    deleteCrawlPlan(id, deleteLinks = false) {
        return this.request('DELETE', `/v1.0/crawl-plans/${encodeURIComponent(id)}`, { query: deleteLinks ? { deleteLinks: 'true' } : {} });
    }

    /** Test a draft plan's connection without saving it. @param {object} plan @param {string} [fromPlanId] @returns {Promise<object>} */
    testCrawlPlanDraft(plan, fromPlanId) {
        return this.request('POST', '/v1.0/crawl-plans/test', { body: plan, query: fromPlanId ? { fromPlanId } : {} });
    }

    /** Test a stored plan's connection step by step. @param {string} id @returns {Promise<object>} */
    testCrawlPlan(id) {
        return this.request('POST', `/v1.0/crawl-plans/${encodeURIComponent(id)}/test`);
    }

    /** Preview what a plan would do if it ran now; nothing is changed. @param {string} id @returns {Promise<object>} */
    previewCrawlPlan(id) {
        return this.request('POST', `/v1.0/crawl-plans/${encodeURIComponent(id)}/preview`);
    }

    /** Start a crawl operation now (202; 409 when already running). @param {string} id @returns {Promise<object>} */
    startCrawlPlan(id) {
        return this.request('POST', `/v1.0/crawl-plans/${encodeURIComponent(id)}/start`);
    }

    /** Stop a plan's running operation. @param {string} id */
    stopCrawlPlan(id) {
        return this.request('POST', `/v1.0/crawl-plans/${encodeURIComponent(id)}/stop`);
    }

    /** @param {string} id @param {object} [options] @returns {Promise<object>} */
    listCrawlPlanOperations(id, options = {}) {
        return this.request('GET', `/v1.0/crawl-plans/${encodeURIComponent(id)}/operations`, { query: options });
    }

    /** List a plan's tracked objects; `{ status }` filters. @param {string} id @param {object} [options] @returns {Promise<object>} */
    listCrawlPlanObjects(id, options = {}) {
        return this.request('GET', `/v1.0/crawl-plans/${encodeURIComponent(id)}/objects`, { query: options });
    }

    /** List crawl operations; `{ planId, status }` filter. @param {object} [options] @returns {Promise<object>} */
    listCrawlOperations(options = {}) {
        return this.request('GET', '/v1.0/crawl-operations', { query: options });
    }

    /** @param {string} id @returns {Promise<object>} */
    getCrawlOperation(id) {
        return this.request('GET', `/v1.0/crawl-operations/${encodeURIComponent(id)}`);
    }

    /** List what an operation did with each object; `{ action }` filters. @param {string} id @param {object} [options] @returns {Promise<object>} */
    listCrawlOperationObjects(id, options = {}) {
        return this.request('GET', `/v1.0/crawl-operations/${encodeURIComponent(id)}/objects`, { query: options });
    }

    /** Confirm the deletions a held operation is waiting on. @param {string} id @returns {Promise<object>} */
    confirmCrawlDeletions(id) {
        return this.request('POST', `/v1.0/crawl-operations/${encodeURIComponent(id)}/confirm-deletions`);
    }

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
     * Get a link's stored chunks pipeline artifact.
     * Rejects with a 404 error if that stage hasn't produced output yet.
     * @param {string} id @returns {Promise<object>}
     */
    getLinkChunks(id) {
        return this.request('GET', `/v1.0/links/${encodeURIComponent(id)}/chunks`);
    }

    /**
     * Get a link's stored embeddings (vectors) pipeline artifact.
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

    /**
     * Set a link's scheduled refresh.
     * @param {string} id
     * @param {{refreshIntervalMinutes?: number, useSubjectDefault?: boolean}} body 0 is off, otherwise 60 to 525600
     * @returns {Promise<object>} the updated link
     */
    setLinkRefresh(id, body) {
        return this.request('PUT', `/v1.0/links/${encodeURIComponent(id)}`, { body });
    }

    /**
     * Set the scheduled refresh of several links.
     * @param {string[]} ids
     * @param {{refreshIntervalMinutes?: number, useSubjectDefault?: boolean}} body
     * @returns {Promise<{updated: number, skipped: string[]}>}
     */
    bulkSetLinkRefresh(ids, body) {
        return this.request('POST', '/v1.0/links/refresh-interval', { body: { ...body, ids } });
    }

    /**
     * Check a link for changes now (a conditional GET); a changed link is re-ingested.
     * @param {string} id
     * @returns {Promise<{linkId: string, outcome: string, jobId?: string, message?: string, nextRefreshUtc?: string}>}
     */
    refreshLinkNow(id) {
        return this.request('POST', `/v1.0/links/${encodeURIComponent(id)}/refresh`);
    }

    /** @param {string} id @returns {Promise<null>} */
    deleteLink(id) {
        return this.request('DELETE', `/v1.0/links/${encodeURIComponent(id)}`);
    }

    // ==================== Jobs ====================

    /**
     * List ingestion jobs (paginated).
     * @param {string} [status] optional status filter
     * @param {object} [options] `{ maxResults, skip, order, search, failureCategory, hasWarnings }`; `failureCategory`
     *   narrows to jobs that failed for that reason (for example `Fetch`), `hasWarnings` to jobs with (true) or
     *   without (false) warnings.
     * @returns {Promise<object>} EnumerationResult envelope; records are in `.objects`.
     */
    listJobs(status, options = {}) {
        return this.request('GET', '/v1.0/jobs', { query: { status, ...options } });
    }

    /**
     * Job detail with per-stage events, attempt history, and remediation for its failure category.
     * @param {string} id
     * @returns {Promise<{ job: object, events: object[], attempts: object[], remediation: ?string }>}
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

    // ==================== Ontologies ====================

    /** List the built-in ontology templates. @returns {Promise<object[]>} */
    listOntologyTemplates() {
        return this.request('GET', '/v1.0/ontology-templates');
    }

    /** List the tenant's ontologies. @param {object} [options] `{ maxResults, skip, order, search }` @returns {Promise<object>} */
    listOntologies(options = {}) {
        return this.request('GET', '/v1.0/ontologies', { query: options });
    }

    /**
     * Create an ontology; its first version is a draft (empty, from a template, or a copy of a version).
     * @param {object} ontology `{ name, description?, template?, copyFromVersionId? }`
     * @returns {Promise<object>} `{ ontology, versions, pinnedSubjects }`
     */
    createOntology(ontology) {
        return this.request('POST', '/v1.0/ontologies', { body: ontology });
    }

    /** Get an ontology with its versions and the subjects that pin them. @param {string} id @returns {Promise<object>} */
    getOntology(id) {
        return this.request('GET', `/v1.0/ontologies/${encodeURIComponent(id)}`);
    }

    /** Rename or re-describe an ontology. @param {string} id @param {object} update `{ name?, description? }` @returns {Promise<object>} */
    updateOntology(id, update) {
        return this.request('PUT', `/v1.0/ontologies/${encodeURIComponent(id)}`, { body: update });
    }

    /** Delete an ontology and its versions (refused while a subject pins one). @param {string} id */
    deleteOntology(id) {
        return this.request('DELETE', `/v1.0/ontologies/${encodeURIComponent(id)}`);
    }

    /** List an ontology's versions, newest first. @param {string} id @param {object} [options] @returns {Promise<object>} */
    listOntologyVersions(id, options = {}) {
        return this.request('GET', `/v1.0/ontologies/${encodeURIComponent(id)}/versions`, { query: options });
    }

    /** Start a new draft that copies a version (by default the newest). @param {string} id @param {string} [basedOnVersionId] @returns {Promise<object>} */
    createOntologyDraft(id, basedOnVersionId) {
        return this.request('POST', `/v1.0/ontologies/${encodeURIComponent(id)}/versions`, { body: { basedOnVersionId: basedOnVersionId || null } });
    }

    /**
     * Have the inference model propose a new draft (uses the ontology.propose prompts).
     * @param {string} id
     * @param {object} request `{ subjectId?, sampleText?, modelRunnerId?, sampleCells?, instructions?, language?, basedOnVersionId? }`
     * @returns {Promise<object>} the proposed draft
     */
    proposeOntology(id, request) {
        return this.request('POST', `/v1.0/ontologies/${encodeURIComponent(id)}/propose`, { body: request });
    }

    /** Get a version with its types, rules, concepts, and approval problems. @param {string} versionId @returns {Promise<object>} */
    getOntologyVersion(versionId) {
        return this.request('GET', `/v1.0/ontology-versions/${encodeURIComponent(versionId)}`);
    }

    /** Replace a draft's contents. @param {string} versionId @param {object} version @returns {Promise<object>} */
    updateOntologyVersion(versionId, version) {
        return this.request('PUT', `/v1.0/ontology-versions/${encodeURIComponent(versionId)}`, { body: version });
    }

    /** Delete a draft version. @param {string} versionId */
    deleteOntologyVersion(versionId) {
        return this.request('DELETE', `/v1.0/ontology-versions/${encodeURIComponent(versionId)}`);
    }

    /** Approve a draft (needs Ontology Execute). @param {string} versionId @param {string} [changeSummary] @returns {Promise<object>} */
    approveOntologyVersion(versionId, changeSummary) {
        return this.request('POST', `/v1.0/ontology-versions/${encodeURIComponent(versionId)}/approve`, { body: { changeSummary: changeSummary || null } });
    }

    /** Retire an approved version (refused while a subject pins it). @param {string} versionId @returns {Promise<object>} */
    retireOntologyVersion(versionId) {
        return this.request('POST', `/v1.0/ontology-versions/${encodeURIComponent(versionId)}/retire`);
    }

    /** Compare a version with another (default: the version it was copied from). @param {string} versionId @param {string} [against] @returns {Promise<object>} */
    diffOntologyVersion(versionId, against) {
        return this.request('GET', `/v1.0/ontology-versions/${encodeURIComponent(versionId)}/diff`, { query: against ? { against } : {} });
    }

    /** Get the definition text the classifier sees for a version. @param {string} versionId @returns {Promise<object>} `{ versionId, definition }` */
    getOntologyDefinition(versionId) {
        return this.request('GET', `/v1.0/ontology-versions/${encodeURIComponent(versionId)}/definition`);
    }

    /**
     * Export a version as OWL and SKOS.
     * @param {string} versionId @param {string} [format='turtle'] 'turtle' or 'jsonld' @param {string} [baseIri]
     * @returns {Promise<string|object>} Turtle text, or the parsed JSON-LD
     */
    exportOntologyVersion(versionId, format = 'turtle', baseIri) {
        return this.request('GET', `/v1.0/ontology-versions/${encodeURIComponent(versionId)}/export`, { query: { format, baseIri } });
    }

    /**
     * Import a SKOS taxonomy into a draft.
     * @param {string} versionId @param {string} document Turtle or JSON-LD text
     * @param {object} [options] `{ format: 'turtle'|'jsonld', mode: 'merge'|'replace' }`
     * @returns {Promise<object>} `{ added, updated, removed, total, warnings, version }`
     */
    importTaxonomy(versionId, document, options = {}) {
        const format = options.format || 'turtle';
        return this.request('POST', `/v1.0/ontology-versions/${encodeURIComponent(versionId)}/taxonomy/import`, {
            query: { format, mode: options.mode || 'merge' },
            rawBody: document,
            contentType: format === 'jsonld' ? 'application/ld+json' : 'text/turtle'
        });
    }

    /** Show how a subject classifies (pinned version, definition, settings). @param {string} subjectId @returns {Promise<object>} */
    getSubjectOntology(subjectId) {
        return this.request('GET', `/v1.0/subjects/${encodeURIComponent(subjectId)}/ontology`);
    }

    /**
     * Pin a subject to an approved version, or unpin it (null).
     * @param {string} subjectId @param {string|null} ontologyVersionId @param {boolean} [retag=true]
     * @returns {Promise<object>}
     */
    setSubjectOntology(subjectId, ontologyVersionId, retag = true) {
        return this.request('PUT', `/v1.0/subjects/${encodeURIComponent(subjectId)}/ontology`, { body: { ontologyVersionId, retag } });
    }

    /** List a subject's ontology violations; `{ status, jobId, operationId }` filter. @param {string} subjectId @param {object} [options] @returns {Promise<object>} */
    listOntologyViolations(subjectId, options = {}) {
        return this.request('GET', `/v1.0/subjects/${encodeURIComponent(subjectId)}/ontology-violations`, { query: options });
    }

    /** Release a quarantined element into the graph. @param {string} violationId @returns {Promise<object>} */
    releaseOntologyViolation(violationId) {
        return this.request('POST', `/v1.0/ontology-violations/${encodeURIComponent(violationId)}/release`);
    }

    /** Dismiss a quarantined element. @param {string} violationId @returns {Promise<object>} */
    dismissOntologyViolation(violationId) {
        return this.request('POST', `/v1.0/ontology-violations/${encodeURIComponent(violationId)}/dismiss`);
    }

    /** List a subject's ontology operations. @param {string} subjectId @param {object} [options] @returns {Promise<object>} */
    listOntologyOperations(subjectId, options = {}) {
        return this.request('GET', `/v1.0/subjects/${encodeURIComponent(subjectId)}/ontology-operations`, { query: options });
    }

    /** Queue a background ontology operation. @param {string} subjectId @param {object} request `{ kind: 'Validate'|'Retag'|'DriftCheck', sampleSize? }` @returns {Promise<object>} */
    startOntologyOperation(subjectId, request) {
        return this.request('POST', `/v1.0/subjects/${encodeURIComponent(subjectId)}/ontology-operations`, { body: request });
    }

    /** Get an ontology operation with its items. @param {string} operationId @returns {Promise<object>} `{ operation, items }` */
    getOntologyOperation(operationId) {
        return this.request('GET', `/v1.0/ontology-operations/${encodeURIComponent(operationId)}`);
    }

    /**
     * Export a subject's graph.
     * @param {string} subjectId @param {string} [format='json'] 'json', 'jsonld', 'turtle', or 'graphml' @param {string} [baseIri]
     * @returns {Promise<string|object>} parsed JSON for json/jsonld, text otherwise
     */
    exportSubjectGraph(subjectId, format = 'json', baseIri) {
        return this.request('GET', `/v1.0/subjects/${encodeURIComponent(subjectId)}/graph/export`, { query: { format, baseIri } });
    }

    /** Remove the classification cache entries a subject stored. @param {string} subjectId @returns {Promise<object>} `{ removed }` */
    clearClassificationCache(subjectId) {
        return this.request('DELETE', `/v1.0/subjects/${encodeURIComponent(subjectId)}/classification-cache`);
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

    /**
     * Create a model runner. `runner` accepts `name`, `provider` (OpenAI |
     * OpenAICompatible | Gemini | Ollama | AzureOpenAI | Anthropic | Bedrock |
     * VoyageAI | VertexAI), `endpoint`, `apiFormat`, `deployment`, `apiVersion`,
     * `region`, `project`, `accessKeyId` and `active`. Secret material (`apiKey`,
     * `secretAccessKey`, `sessionToken`) is write-only: it may be sent here but is
     * never returned on reads.
     * @param {object} runner @returns {Promise<object>}
     */
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

    // ==================== Subject Prompts ====================

    /**
     * List a subject's effective prompts, one per key. Each entry has `key`, `name`,
     * `effectiveContent`, `globalContent`, `overrideContent` (may be null), `source`
     * ("Global" | "SubjectOverride"), and `mergeMode` ("Append" | "Replace").
     * @param {string} subjectId
     * @returns {Promise<object[]>}
     */
    listSubjectPrompts(subjectId) {
        return this.request('GET', `/v1.0/subjects/${encodeURIComponent(subjectId)}/prompts`);
    }

    /**
     * Set (create or update) a subject-level prompt override for a given key.
     * @param {string} subjectId
     * @param {string} key - The prompt key to override.
     * @param {{ content: string, mergeMode?: ('Append'|'Replace') }} override
     * @returns {Promise<object>} The subject's effective prompt for the key after the update.
     */
    setSubjectPrompt(subjectId, key, { content, mergeMode = 'Append' } = {}) {
        return this.request(
            'PUT',
            `/v1.0/subjects/${encodeURIComponent(subjectId)}/prompts/${encodeURIComponent(key)}`,
            { body: { content, mergeMode } }
        );
    }

    /**
     * Delete a subject-level prompt override for a given key, reverting it to the global prompt.
     * @param {string} subjectId
     * @param {string} key
     * @returns {Promise<null>}
     */
    deleteSubjectPrompt(subjectId, key) {
        return this.request(
            'DELETE',
            `/v1.0/subjects/${encodeURIComponent(subjectId)}/prompts/${encodeURIComponent(key)}`
        );
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
     * Grounded answer to a natural-language question. An optional `metadataFilter` (required/excluded
     * labels and tags) scopes retrieval to documents ingested with matching labels/tags; it is merged with
     * the subject's default filter (union of required and excluded).
     * @param {{ question: string, maxResults?: number, subjectId?: string, metadataFilter?: { requiredLabels?: string[], excludedLabels?: string[], requiredTags?: {key:string,condition?:string,value?:string}[], excludedTags?: {key:string,condition?:string,value?:string}[] } }} body
     * @returns {Promise<{ answer: string, sources: object[], grounded: boolean }>}
     */
    query(body) {
        return this.request('POST', '/v1.0/query', { body });
    }
}

export default PneumaClient;
