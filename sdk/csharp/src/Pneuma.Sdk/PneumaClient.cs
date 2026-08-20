namespace Pneuma.Sdk
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Sdk.Models;
    using Pneuma.Sdk.Requests;
    using Pneuma.Sdk.Responses;

    /// <summary>
    /// Hand-rolled client for the Pneuma REST API. Wraps an <see cref="HttpClient"/> and exposes typed,
    /// asynchronous methods for every documented endpoint. Non-2xx responses are surfaced as
    /// <see cref="PneumaException"/>.
    /// </summary>
    public class PneumaClient : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Base URL of the Pneuma server, without a trailing slash. Loopback hosts are normalized to
        /// 127.0.0.1.
        /// </summary>
        public string BaseUrl { get; private set; }

        /// <summary>
        /// Bearer token sent as <c>Authorization: Bearer &lt;token&gt;</c> on each request. Set by
        /// <see cref="LoginAsync"/>, or assign directly. Null suppresses the header.
        /// </summary>
        public string? Token { get; set; }

        /// <summary>
        /// Request timeout in milliseconds. Applied to the underlying <see cref="HttpClient"/>.
        /// </summary>
        public int TimeoutMs
        {
            get
            {
                return (int)_Http.Timeout.TotalMilliseconds;
            }
            set
            {
                _Http.Timeout = TimeSpan.FromMilliseconds(value <= 0 ? 100000 : value);
            }
        }

        #endregion

        #region Private-Members

        private readonly HttpClient _Http;
        private readonly bool _OwnsHttpClient;
        private readonly JsonSerializerOptions _Json;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize a new instance of the <see cref="PneumaClient"/> class.
        /// </summary>
        /// <param name="baseUrl">Base URL of the Pneuma server (for example http://127.0.0.1:8080).
        /// Loopback hostnames are normalized to 127.0.0.1.</param>
        /// <param name="token">Optional bearer token to send on requests.</param>
        public PneumaClient(string baseUrl, string? token = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));

            BaseUrl = NormalizeBaseUrl(baseUrl);
            Token = token;

            _Http = new HttpClient();
            _Http.Timeout = TimeSpan.FromMilliseconds(100000);
            _OwnsHttpClient = true;

            _Json = BuildJsonOptions();
        }

        /// <summary>
        /// Initialize a new instance of the <see cref="PneumaClient"/> class using a caller-supplied
        /// <see cref="HttpClient"/>. The supplied client is not disposed by this instance.
        /// </summary>
        /// <param name="baseUrl">Base URL of the Pneuma server. Loopback hostnames are normalized to
        /// 127.0.0.1.</param>
        /// <param name="httpClient">HTTP client to use for all requests.</param>
        /// <param name="token">Optional bearer token to send on requests.</param>
        public PneumaClient(string baseUrl, HttpClient httpClient, string? token = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
            if (httpClient == null) throw new ArgumentNullException(nameof(httpClient));

            BaseUrl = NormalizeBaseUrl(baseUrl);
            Token = token;

            _Http = httpClient;
            _OwnsHttpClient = false;

            _Json = BuildJsonOptions();
        }

        #endregion

        #region Public-Methods-System

        /// <summary>
        /// Get server health.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Health response.</returns>
        public Task<HealthResponse> GetHealthAsync(CancellationToken token = default)
        {
            return SendAsync<HealthResponse>(HttpMethod.Get, "/v1.0/api/health", null, token);
        }

        #endregion

        #region Public-Methods-Tokens

        /// <summary>
        /// Log in with email and password, store the returned bearer token on this client, and return
        /// the full token response.
        /// </summary>
        /// <param name="email">Email address.</param>
        /// <param name="password">Plaintext password.</param>
        /// <param name="tenantId">Optional tenant identifier to disambiguate the email.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Token response, including the issued bearer token.</returns>
        public async Task<TokenResponse> LoginAsync(string email, string password, string? tenantId = null, CancellationToken token = default)
        {
            LoginRequest request = new LoginRequest { Email = email, Password = password, TenantId = tenantId };
            TokenResponse response = await SendAsync<TokenResponse>(HttpMethod.Post, "/v1.0/token", request, token).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(response.Token)) Token = response.Token;
            return response;
        }

        /// <summary>
        /// Validate the current bearer token; returns a principal summary.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Token response describing the current principal.</returns>
        public Task<TokenResponse> ValidateTokenAsync(CancellationToken token = default)
        {
            return SendAsync<TokenResponse>(HttpMethod.Get, "/v1.0/token", null, token);
        }

        /// <summary>
        /// Get the decoded authentication context for the current token as raw JSON.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Raw JSON body describing the authentication context.</returns>
        public async Task<string> GetTokenDetailsAsync(CancellationToken token = default)
        {
            string? body = await SendCoreAsync(HttpMethod.Get, "/v1.0/token/details", null, token).ConfigureAwait(false);
            return body ?? string.Empty;
        }

        /// <summary>
        /// Revoke the current session (logout). Clears the stored token on success.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task LogoutAsync(CancellationToken token = default)
        {
            await SendCoreAsync(HttpMethod.Delete, "/v1.0/token", null, token).ConfigureAwait(false);
            Token = null;
        }

        #endregion

        #region Public-Methods-Tenants

        /// <summary>List tenants (admin).</summary>
        /// <param name="query">Optional paging and filtering options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated result of tenants.</returns>
        public Task<EnumerationResult<Tenant>> ListTenantsAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            string path = "/v1.0/tenants" + (query?.ToQueryString() ?? string.Empty);
            return SendAsync<EnumerationResult<Tenant>>(HttpMethod.Get, path, null, token);
        }

        /// <summary>Create a tenant (admin).</summary>
        /// <param name="tenant">Tenant to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created tenant.</returns>
        public Task<Tenant> CreateTenantAsync(Tenant tenant, CancellationToken token = default)
        {
            if (tenant == null) throw new ArgumentNullException(nameof(tenant));
            return SendAsync<Tenant>(HttpMethod.Post, "/v1.0/tenants", tenant, token);
        }

        /// <summary>Get a tenant by identifier (admin).</summary>
        /// <param name="id">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The tenant.</returns>
        public Task<Tenant> GetTenantAsync(string id, CancellationToken token = default)
        {
            return SendAsync<Tenant>(HttpMethod.Get, "/v1.0/tenants/" + Escape(id), null, token);
        }

        /// <summary>Update a tenant (admin).</summary>
        /// <param name="id">Tenant identifier.</param>
        /// <param name="tenant">Updated tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The saved tenant.</returns>
        public Task<Tenant> UpdateTenantAsync(string id, Tenant tenant, CancellationToken token = default)
        {
            if (tenant == null) throw new ArgumentNullException(nameof(tenant));
            return SendAsync<Tenant>(HttpMethod.Put, "/v1.0/tenants/" + Escape(id), tenant, token);
        }

        /// <summary>Delete a tenant (admin).</summary>
        /// <param name="id">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task DeleteTenantAsync(string id, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Delete, "/v1.0/tenants/" + Escape(id), null, token);
        }

        #endregion

        #region Public-Methods-Users

        /// <summary>List users. Admins may pass a tenant identifier to scope the list.</summary>
        /// <param name="query">Optional paging and filtering options.</param>
        /// <param name="tenantId">Optional tenant identifier (admin only).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated result of users.</returns>
        public Task<EnumerationResult<User>> ListUsersAsync(EnumerationQuery? query = null, string? tenantId = null, CancellationToken token = default)
        {
            List<(string, string?)> pairs = new List<(string, string?)> { ("tenantId", tenantId) };
            AddEnumPairs(pairs, query);
            string path = "/v1.0/users" + QueryString(pairs.ToArray());
            return SendAsync<EnumerationResult<User>>(HttpMethod.Get, path, null, token);
        }

        /// <summary>Create a user.</summary>
        /// <param name="request">User creation request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created user.</returns>
        public Task<User> CreateUserAsync(CreateUserRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<User>(HttpMethod.Post, "/v1.0/users", request, token);
        }

        /// <summary>Get a user by identifier.</summary>
        /// <param name="id">User identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The user.</returns>
        public Task<User> GetUserAsync(string id, CancellationToken token = default)
        {
            return SendAsync<User>(HttpMethod.Get, "/v1.0/users/" + Escape(id), null, token);
        }

        /// <summary>Update a user.</summary>
        /// <param name="id">User identifier.</param>
        /// <param name="request">Updated user fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The saved user.</returns>
        public Task<User> UpdateUserAsync(string id, CreateUserRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<User>(HttpMethod.Put, "/v1.0/users/" + Escape(id), request, token);
        }

        /// <summary>Delete a user.</summary>
        /// <param name="id">User identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task DeleteUserAsync(string id, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Delete, "/v1.0/users/" + Escape(id), null, token);
        }

        #endregion

        #region Public-Methods-Credentials

        /// <summary>Create a credential. The raw secret key is returned once, in the response.</summary>
        /// <param name="request">Credential creation request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Credential response including the one-time secret.</returns>
        public Task<CredentialResponse> CreateCredentialAsync(CreateCredentialRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<CredentialResponse>(HttpMethod.Post, "/v1.0/credentials", request, token);
        }

        /// <summary>List credentials.</summary>
        /// <param name="query">Optional paging and filtering options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated result of credentials (secrets omitted).</returns>
        public Task<EnumerationResult<CredentialResponse>> ListCredentialsAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            string path = "/v1.0/credentials" + (query?.ToQueryString() ?? string.Empty);
            return SendAsync<EnumerationResult<CredentialResponse>>(HttpMethod.Get, path, null, token);
        }

        /// <summary>Get a credential by identifier.</summary>
        /// <param name="id">Credential identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The credential (secret omitted).</returns>
        public Task<CredentialResponse> GetCredentialAsync(string id, CancellationToken token = default)
        {
            return SendAsync<CredentialResponse>(HttpMethod.Get, "/v1.0/credentials/" + Escape(id), null, token);
        }

        /// <summary>Delete a credential.</summary>
        /// <param name="id">Credential identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task DeleteCredentialAsync(string id, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Delete, "/v1.0/credentials/" + Escape(id), null, token);
        }

        #endregion

        #region Public-Methods-Roles

        /// <summary>List roles (admin).</summary>
        /// <param name="query">Optional paging and filtering options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated result of roles.</returns>
        public Task<EnumerationResult<UserRole>> ListRolesAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            string path = "/v1.0/roles" + (query?.ToQueryString() ?? string.Empty);
            return SendAsync<EnumerationResult<UserRole>>(HttpMethod.Get, path, null, token);
        }

        /// <summary>Create a role (admin).</summary>
        /// <param name="role">Role to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created role.</returns>
        public Task<UserRole> CreateRoleAsync(UserRole role, CancellationToken token = default)
        {
            if (role == null) throw new ArgumentNullException(nameof(role));
            return SendAsync<UserRole>(HttpMethod.Post, "/v1.0/roles", role, token);
        }

        /// <summary>Get a role by identifier (admin).</summary>
        /// <param name="id">Role identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The role.</returns>
        public Task<UserRole> GetRoleAsync(string id, CancellationToken token = default)
        {
            return SendAsync<UserRole>(HttpMethod.Get, "/v1.0/roles/" + Escape(id), null, token);
        }

        /// <summary>Update a role (admin). Built-in roles are protected.</summary>
        /// <param name="id">Role identifier.</param>
        /// <param name="role">Updated role.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The saved role.</returns>
        public Task<UserRole> UpdateRoleAsync(string id, UserRole role, CancellationToken token = default)
        {
            if (role == null) throw new ArgumentNullException(nameof(role));
            return SendAsync<UserRole>(HttpMethod.Put, "/v1.0/roles/" + Escape(id), role, token);
        }

        /// <summary>Delete a role (admin). Built-in roles are protected.</summary>
        /// <param name="id">Role identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task DeleteRoleAsync(string id, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Delete, "/v1.0/roles/" + Escape(id), null, token);
        }

        #endregion

        #region Public-Methods-Permissions

        /// <summary>List permissions (admin).</summary>
        /// <param name="query">Optional paging and filtering options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated result of permissions.</returns>
        public Task<EnumerationResult<Permission>> ListPermissionsAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            string path = "/v1.0/permissions" + (query?.ToQueryString() ?? string.Empty);
            return SendAsync<EnumerationResult<Permission>>(HttpMethod.Get, path, null, token);
        }

        /// <summary>Create a permission (admin).</summary>
        /// <param name="permission">Permission to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created permission.</returns>
        public Task<Permission> CreatePermissionAsync(Permission permission, CancellationToken token = default)
        {
            if (permission == null) throw new ArgumentNullException(nameof(permission));
            return SendAsync<Permission>(HttpMethod.Post, "/v1.0/permissions", permission, token);
        }

        /// <summary>Get a permission by identifier (admin).</summary>
        /// <param name="id">Permission identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The permission.</returns>
        public Task<Permission> GetPermissionAsync(string id, CancellationToken token = default)
        {
            return SendAsync<Permission>(HttpMethod.Get, "/v1.0/permissions/" + Escape(id), null, token);
        }

        /// <summary>Update a permission (admin).</summary>
        /// <param name="id">Permission identifier.</param>
        /// <param name="permission">Updated permission.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The saved permission.</returns>
        public Task<Permission> UpdatePermissionAsync(string id, Permission permission, CancellationToken token = default)
        {
            if (permission == null) throw new ArgumentNullException(nameof(permission));
            return SendAsync<Permission>(HttpMethod.Put, "/v1.0/permissions/" + Escape(id), permission, token);
        }

        /// <summary>Delete a permission (admin).</summary>
        /// <param name="id">Permission identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task DeletePermissionAsync(string id, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Delete, "/v1.0/permissions/" + Escape(id), null, token);
        }

        #endregion

        #region Public-Methods-Assignments

        /// <summary>List role assignments for a user (admin).</summary>
        /// <param name="userId">User identifier.</param>
        /// <param name="query">Optional paging and filtering options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated result of assignments.</returns>
        public Task<EnumerationResult<UserRoleAssignment>> ListAssignmentsAsync(string userId, EnumerationQuery? query = null, CancellationToken token = default)
        {
            List<(string, string?)> pairs = new List<(string, string?)> { ("userId", userId) };
            AddEnumPairs(pairs, query);
            string path = "/v1.0/assignments" + QueryString(pairs.ToArray());
            return SendAsync<EnumerationResult<UserRoleAssignment>>(HttpMethod.Get, path, null, token);
        }

        /// <summary>Create a role assignment (admin).</summary>
        /// <param name="assignment">Assignment to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created assignment.</returns>
        public Task<UserRoleAssignment> CreateAssignmentAsync(UserRoleAssignment assignment, CancellationToken token = default)
        {
            if (assignment == null) throw new ArgumentNullException(nameof(assignment));
            return SendAsync<UserRoleAssignment>(HttpMethod.Post, "/v1.0/assignments", assignment, token);
        }

        /// <summary>Delete a role assignment (admin).</summary>
        /// <param name="id">Assignment identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task DeleteAssignmentAsync(string id, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Delete, "/v1.0/assignments/" + Escape(id), null, token);
        }

        #endregion

        #region Public-Methods-Audit

        /// <summary>List recent security audit events.</summary>
        /// <param name="query">Optional paging and filtering options.</param>
        /// <param name="tenantId">Optional tenant identifier (admin only).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated result of audit records.</returns>
        public Task<EnumerationResult<AuditRecord>> ListAuditAsync(EnumerationQuery? query = null, string? tenantId = null, CancellationToken token = default)
        {
            List<(string, string?)> pairs = new List<(string, string?)> { ("tenantId", tenantId) };
            AddEnumPairs(pairs, query);
            string path = "/v1.0/audit" + QueryString(pairs.ToArray());
            return SendAsync<EnumerationResult<AuditRecord>>(HttpMethod.Get, path, null, token);
        }

        #endregion

        #region Public-Methods-Subjects

        /// <summary>List subjects for the caller's tenant.</summary>
        /// <param name="query">Optional paging and filtering options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated result of subjects.</returns>
        public Task<EnumerationResult<Subject>> ListSubjectsAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            string path = "/v1.0/subjects" + (query?.ToQueryString() ?? string.Empty);
            return SendAsync<EnumerationResult<Subject>>(HttpMethod.Get, path, null, token);
        }

        /// <summary>Create a subject.</summary>
        /// <param name="subject">Subject to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created subject.</returns>
        public Task<Subject> CreateSubjectAsync(Subject subject, CancellationToken token = default)
        {
            if (subject == null) throw new ArgumentNullException(nameof(subject));
            return SendAsync<Subject>(HttpMethod.Post, "/v1.0/subjects", subject, token);
        }

        /// <summary>Get a subject by identifier.</summary>
        /// <param name="id">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The subject.</returns>
        public Task<Subject> GetSubjectAsync(string id, CancellationToken token = default)
        {
            return SendAsync<Subject>(HttpMethod.Get, "/v1.0/subjects/" + Escape(id), null, token);
        }

        /// <summary>Resolve a subject by its URL slug.</summary>
        /// <param name="slug">URL slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The subject.</returns>
        public Task<Subject> GetSubjectBySlugAsync(string slug, CancellationToken token = default)
        {
            return SendAsync<Subject>(HttpMethod.Get, "/v1.0/subjects/by-slug/" + Escape(slug), null, token);
        }

        /// <summary>Update a subject.</summary>
        /// <param name="id">Subject identifier.</param>
        /// <param name="subject">Updated subject.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The saved subject.</returns>
        public Task<Subject> UpdateSubjectAsync(string id, Subject subject, CancellationToken token = default)
        {
            if (subject == null) throw new ArgumentNullException(nameof(subject));
            return SendAsync<Subject>(HttpMethod.Put, "/v1.0/subjects/" + Escape(id), subject, token);
        }

        /// <summary>Delete a subject.</summary>
        /// <param name="id">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task DeleteSubjectAsync(string id, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Delete, "/v1.0/subjects/" + Escape(id), null, token);
        }

        #endregion

        #region Public-Methods-History-And-Feedback

        /// <summary>List persisted chat turns (newest first), optionally scoped to a subject.</summary>
        /// <param name="subjectId">Subject to scope to, or null for all subjects.</param>
        /// <param name="query">Optional paging options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated result of chat turns.</returns>
        public Task<EnumerationResult<ChatTurnRecord>> ListHistoryAsync(string? subjectId = null, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return SendAsync<EnumerationResult<ChatTurnRecord>>(HttpMethod.Get, "/v1.0/history" + BuildFilteredQuery(query, subjectId), null, token);
        }

        /// <summary>Get a single chat turn together with its feedback.</summary>
        /// <param name="id">Turn identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The turn and its feedback.</returns>
        public Task<ChatTurnDetail> GetHistoryTurnAsync(string id, CancellationToken token = default)
        {
            return SendAsync<ChatTurnDetail>(HttpMethod.Get, "/v1.0/history/" + Escape(id), null, token);
        }

        /// <summary>List chat feedback (newest first), each enriched with the rated turn, optionally scoped to a subject.</summary>
        /// <param name="subjectId">Subject to scope to, or null for all subjects.</param>
        /// <param name="query">Optional paging options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated result of feedback with its turns.</returns>
        public Task<EnumerationResult<ChatFeedbackDetail>> ListFeedbackAsync(string? subjectId = null, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return SendAsync<EnumerationResult<ChatFeedbackDetail>>(HttpMethod.Get, "/v1.0/feedback" + BuildFilteredQuery(query, subjectId), null, token);
        }

        /// <summary>Submit thumbs up/down and/or a comment on a chat answer.</summary>
        /// <param name="request">The feedback request (turn id, rating, optional comment).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created feedback record.</returns>
        public Task<ChatFeedback> SubmitFeedbackAsync(SubmitFeedbackRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<ChatFeedback>(HttpMethod.Post, "/v1.0/feedback", request, token);
        }

        private static string BuildFilteredQuery(EnumerationQuery? query, string? subjectId)
        {
            string q = query?.ToQueryString() ?? string.Empty;
            if (string.IsNullOrEmpty(subjectId)) return q;
            string separator = string.IsNullOrEmpty(q) ? "?" : "&";
            return q + separator + "subjectId=" + Uri.EscapeDataString(subjectId);
        }

        #endregion

        #region Public-Methods-Links-And-Ingestion

        /// <summary>Submit a content link for a subject; enqueues an ingestion job.</summary>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="request">Link submission request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created link.</returns>
        public Task<SubjectLink> SubmitLinkAsync(string subjectId, SubmitLinkRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<SubjectLink>(HttpMethod.Post, "/v1.0/subjects/" + Escape(subjectId) + "/links", request, token);
        }

        /// <summary>Submit multiple content links for a subject in a single call; enqueues one ingestion job per URL.</summary>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="request">Bulk link submission request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The bulk submission result, including the created links.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public Task<BulkSubmitLinkResponse> SubmitLinksAsync(string subjectId, BulkSubmitLinkRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<BulkSubmitLinkResponse>(HttpMethod.Post, "/v1.0/subjects/" + Escape(subjectId) + "/links/bulk", request, token);
        }

        /// <summary>List the Partio endpoints available for ingestion, grouped by usage.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The available embedding and completion endpoints.</returns>
        public Task<IngestionEndpointsResponse> ListIngestionEndpointsAsync(CancellationToken token = default)
        {
            return SendAsync<IngestionEndpointsResponse>(HttpMethod.Get, "/v1.0/ingestion/endpoints", null, token);
        }

        /// <summary>List a subject's submitted links.</summary>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="query">Optional paging and filtering options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated result of links.</returns>
        public Task<EnumerationResult<SubjectLink>> ListSubjectLinksAsync(string subjectId, EnumerationQuery? query = null, CancellationToken token = default)
        {
            string path = "/v1.0/subjects/" + Escape(subjectId) + "/links" + (query?.ToQueryString() ?? string.Empty);
            return SendAsync<EnumerationResult<SubjectLink>>(HttpMethod.Get, path, null, token);
        }

        /// <summary>List all links for the caller's tenant.</summary>
        /// <param name="query">Optional paging and filtering options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated result of links.</returns>
        public Task<EnumerationResult<SubjectLink>> ListLinksAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            string path = "/v1.0/links" + (query?.ToQueryString() ?? string.Empty);
            return SendAsync<EnumerationResult<SubjectLink>>(HttpMethod.Get, path, null, token);
        }

        /// <summary>Get a link by identifier.</summary>
        /// <param name="id">Link identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The link.</returns>
        public Task<SubjectLink> GetLinkAsync(string id, CancellationToken token = default)
        {
            return SendAsync<SubjectLink>(HttpMethod.Get, "/v1.0/links/" + Escape(id), null, token);
        }

        /// <summary>Delete a link.</summary>
        /// <param name="id">Link identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task DeleteLinkAsync(string id, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Delete, "/v1.0/links/" + Escape(id), null, token);
        }

        /// <summary>Get a link's per-step ingestion log (one entry per ingestion run, each with its events).</summary>
        /// <param name="id">Link identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The ingestion log entries.</returns>
        public Task<List<IngestionJobDetail>> GetLinkIngestionLogAsync(string id, CancellationToken token = default)
        {
            return SendAsync<List<IngestionJobDetail>>(HttpMethod.Get, "/v1.0/links/" + Escape(id) + "/log", null, token);
        }

        /// <summary>Get a link's stored DocumentAtom semantic cells (atoms) pipeline artifact as raw JSON.</summary>
        /// <param name="id">Link identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The atoms artifact as a raw JSON string, or null if empty.</returns>
        /// <exception cref="PneumaException">Thrown with status 404 when the artifact isn't present yet.</exception>
        public Task<string?> GetLinkAtomsAsync(string id, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Get, "/v1.0/links/" + Escape(id) + "/atoms", null, token);
        }

        /// <summary>Get a link's stored Partio chunks pipeline artifact as raw JSON.</summary>
        /// <param name="id">Link identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The chunks artifact as a raw JSON string, or null if empty.</returns>
        /// <exception cref="PneumaException">Thrown with status 404 when the artifact isn't present yet.</exception>
        public Task<string?> GetLinkChunksAsync(string id, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Get, "/v1.0/links/" + Escape(id) + "/chunks", null, token);
        }

        /// <summary>Get a link's stored Partio embeddings (vectors) pipeline artifact as raw JSON.</summary>
        /// <param name="id">Link identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The vectors artifact as a raw JSON string, or null if empty.</returns>
        /// <exception cref="PneumaException">Thrown with status 404 when the artifact isn't present yet.</exception>
        public Task<string?> GetLinkVectorsAsync(string id, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Get, "/v1.0/links/" + Escape(id) + "/vectors", null, token);
        }

        /// <summary>Get a link's stored candidate subgraph pipeline artifact as raw JSON.</summary>
        /// <param name="id">Link identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The subgraph artifact as a raw JSON string, or null if empty.</returns>
        /// <exception cref="PneumaException">Thrown with status 404 when the artifact isn't present yet.</exception>
        public Task<string?> GetLinkSubgraphAsync(string id, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Get, "/v1.0/links/" + Escape(id) + "/subgraph", null, token);
        }

        /// <summary>Get a link's raw crawled source document pipeline artifact as bytes (original content type).</summary>
        /// <param name="id">Link identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The raw source document bytes.</returns>
        /// <exception cref="PneumaException">Thrown with status 404 when the artifact isn't present yet.</exception>
        public Task<byte[]> GetLinkSourceAsync(string id, CancellationToken token = default)
        {
            return SendBytesCoreAsync(HttpMethod.Get, "/v1.0/links/" + Escape(id) + "/source", token);
        }

        /// <summary>List ingestion jobs, optionally filtered by status.</summary>
        /// <param name="query">Optional paging and filtering options.</param>
        /// <param name="status">Optional status filter (for example "Queued", "Failed").</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated result of jobs.</returns>
        public Task<EnumerationResult<IngestionJob>> ListJobsAsync(EnumerationQuery? query = null, string? status = null, CancellationToken token = default)
        {
            List<(string, string?)> pairs = new List<(string, string?)> { ("status", status) };
            AddEnumPairs(pairs, query);
            string path = "/v1.0/jobs" + QueryString(pairs.ToArray());
            return SendAsync<EnumerationResult<IngestionJob>>(HttpMethod.Get, path, null, token);
        }

        /// <summary>Get an ingestion job with its per-stage events.</summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Job detail.</returns>
        public Task<IngestionJobDetail> GetJobAsync(string id, CancellationToken token = default)
        {
            return SendAsync<IngestionJobDetail>(HttpMethod.Get, "/v1.0/jobs/" + Escape(id), null, token);
        }

        /// <summary>Requeue a failed ingestion job.</summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated job.</returns>
        public Task<IngestionJob> RestartJobAsync(string id, CancellationToken token = default)
        {
            return SendAsync<IngestionJob>(HttpMethod.Post, "/v1.0/jobs/" + Escape(id) + "/restart", null, token);
        }

        /// <summary>Stop (cancel) a queued or in-flight ingestion job.</summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated (cancelled) job.</returns>
        public Task<IngestionJob> StopJobAsync(string id, CancellationToken token = default)
        {
            return SendAsync<IngestionJob>(HttpMethod.Post, "/v1.0/jobs/" + Escape(id) + "/stop", null, token);
        }

        /// <summary>Get a job's live per-stage log (job plus its ordered events); poll for a follow-logs view.</summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The job detail with its events.</returns>
        public Task<IngestionJobDetail> GetJobLogAsync(string id, CancellationToken token = default)
        {
            return SendAsync<IngestionJobDetail>(HttpMethod.Get, "/v1.0/jobs/" + Escape(id) + "/log", null, token);
        }

        #endregion

        #region Public-Methods-Model-Runners

        /// <summary>List model runners (admin).</summary>
        /// <param name="query">Optional paging and filtering options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated result of model runners.</returns>
        public Task<EnumerationResult<ModelRunner>> ListModelRunnersAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            string path = "/v1.0/model-runners" + (query?.ToQueryString() ?? string.Empty);
            return SendAsync<EnumerationResult<ModelRunner>>(HttpMethod.Get, path, null, token);
        }

        /// <summary>Create a model runner (admin).</summary>
        /// <param name="request">Model runner creation request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created model runner.</returns>
        public Task<ModelRunner> CreateModelRunnerAsync(CreateModelRunnerRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<ModelRunner>(HttpMethod.Post, "/v1.0/model-runners", request, token);
        }

        /// <summary>Get a model runner by identifier (admin).</summary>
        /// <param name="id">Model runner identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The model runner.</returns>
        public Task<ModelRunner> GetModelRunnerAsync(string id, CancellationToken token = default)
        {
            return SendAsync<ModelRunner>(HttpMethod.Get, "/v1.0/model-runners/" + Escape(id), null, token);
        }

        /// <summary>Update a model runner (admin).</summary>
        /// <param name="id">Model runner identifier.</param>
        /// <param name="request">Updated model runner fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The saved model runner.</returns>
        public Task<ModelRunner> UpdateModelRunnerAsync(string id, CreateModelRunnerRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<ModelRunner>(HttpMethod.Put, "/v1.0/model-runners/" + Escape(id), request, token);
        }

        /// <summary>Delete a model runner (admin).</summary>
        /// <param name="id">Model runner identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task DeleteModelRunnerAsync(string id, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Delete, "/v1.0/model-runners/" + Escape(id), null, token);
        }

        #endregion

        #region Public-Methods-Prompts

        /// <summary>List prompts (admin).</summary>
        /// <param name="query">Optional paging and filtering options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated result of prompts.</returns>
        public Task<EnumerationResult<Prompt>> ListPromptsAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            string path = "/v1.0/prompts" + (query?.ToQueryString() ?? string.Empty);
            return SendAsync<EnumerationResult<Prompt>>(HttpMethod.Get, path, null, token);
        }

        /// <summary>Create a prompt (admin).</summary>
        /// <param name="prompt">Prompt to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created prompt.</returns>
        public Task<Prompt> CreatePromptAsync(Prompt prompt, CancellationToken token = default)
        {
            if (prompt == null) throw new ArgumentNullException(nameof(prompt));
            return SendAsync<Prompt>(HttpMethod.Post, "/v1.0/prompts", prompt, token);
        }

        /// <summary>Get a prompt by identifier (admin).</summary>
        /// <param name="id">Prompt identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The prompt.</returns>
        public Task<Prompt> GetPromptAsync(string id, CancellationToken token = default)
        {
            return SendAsync<Prompt>(HttpMethod.Get, "/v1.0/prompts/" + Escape(id), null, token);
        }

        /// <summary>Update a prompt (admin).</summary>
        /// <param name="id">Prompt identifier.</param>
        /// <param name="prompt">Updated prompt.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The saved prompt.</returns>
        public Task<Prompt> UpdatePromptAsync(string id, Prompt prompt, CancellationToken token = default)
        {
            if (prompt == null) throw new ArgumentNullException(nameof(prompt));
            return SendAsync<Prompt>(HttpMethod.Put, "/v1.0/prompts/" + Escape(id), prompt, token);
        }

        /// <summary>Delete a prompt (admin).</summary>
        /// <param name="id">Prompt identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task DeletePromptAsync(string id, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Delete, "/v1.0/prompts/" + Escape(id), null, token);
        }

        #endregion

        #region Public-Methods-Request-History

        /// <summary>List request history entries matching a filter. Bodies are omitted from list items.</summary>
        /// <param name="filter">Filter and paging options. Null uses defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of request history entries.</returns>
        public Task<RequestHistoryPage> ListRequestHistoryAsync(RequestHistoryFilter? filter = null, CancellationToken token = default)
        {
            filter ??= new RequestHistoryFilter();
            string path = "/v1.0/api/request-history" + BuildHistoryQuery(filter, includePaging: true, includeBuckets: false);
            return SendAsync<RequestHistoryPage>(HttpMethod.Get, path, null, token);
        }

        /// <summary>Get a time-bucketed request history summary.</summary>
        /// <param name="filter">Filter and bucket options. Null uses defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The summary.</returns>
        public Task<RequestHistorySummary> GetRequestHistorySummaryAsync(RequestHistoryFilter? filter = null, CancellationToken token = default)
        {
            filter ??= new RequestHistoryFilter();
            string path = "/v1.0/api/request-history/summary" + BuildHistoryQuery(filter, includePaging: false, includeBuckets: true);
            return SendAsync<RequestHistorySummary>(HttpMethod.Get, path, null, token);
        }

        /// <summary>Get a full request history entry, including headers and bodies.</summary>
        /// <param name="id">Entry identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The full entry.</returns>
        public Task<RequestHistoryEntry> GetRequestHistoryAsync(string id, CancellationToken token = default)
        {
            return SendAsync<RequestHistoryEntry>(HttpMethod.Get, "/v1.0/api/request-history/" + Escape(id), null, token);
        }

        /// <summary>Delete a single request history entry.</summary>
        /// <param name="id">Entry identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task DeleteRequestHistoryAsync(string id, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Delete, "/v1.0/api/request-history/" + Escape(id), null, token);
        }

        /// <summary>Bulk-delete request history entries matching a filter.</summary>
        /// <param name="filter">Filter selecting entries to delete. Null uses defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of deleted entries.</returns>
        public Task<DeletedCountResponse> DeleteRequestHistoryManyAsync(RequestHistoryFilter? filter = null, CancellationToken token = default)
        {
            filter ??= new RequestHistoryFilter();
            string path = "/v1.0/api/request-history" + BuildHistoryQuery(filter, includePaging: false, includeBuckets: false);
            return SendAsync<DeletedCountResponse>(HttpMethod.Delete, path, null, token);
        }

        #endregion

        #region Public-Methods-Graph

        /// <summary>Get a knowledge-graph node's contents.</summary>
        /// <param name="id">Node identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The node.</returns>
        public Task<GraphNode> GetGraphNodeAsync(string id, CancellationToken token = default)
        {
            return SendAsync<GraphNode>(HttpMethod.Get, "/v1.0/graph/nodes/" + Escape(id), null, token);
        }

        /// <summary>Get the neighbors of a knowledge-graph node.</summary>
        /// <param name="id">Node identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of adjacent nodes.</returns>
        public Task<List<GraphNode>> GetGraphNeighborsAsync(string id, CancellationToken token = default)
        {
            return SendAsync<List<GraphNode>>(HttpMethod.Get, "/v1.0/graph/nodes/" + Escape(id) + "/neighbors", null, token);
        }

        /// <summary>Get the edges (relationships) for a knowledge-graph node.</summary>
        /// <param name="id">Node identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of edges.</returns>
        public Task<List<GraphEdge>> GetGraphEdgesAsync(string id, CancellationToken token = default)
        {
            return SendAsync<List<GraphEdge>>(HttpMethod.Get, "/v1.0/graph/nodes/" + Escape(id) + "/edges", null, token);
        }

        #endregion

        #region Public-Methods-Search-And-Query

        /// <summary>Run a full-text search resolved to a representative set of graph nodes.</summary>
        /// <param name="query">The search query.</param>
        /// <param name="max">Maximum number of results.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The search response.</returns>
        public Task<SearchResponse> SearchAsync(string query, int max = 20, CancellationToken token = default)
        {
            string path = "/v1.0/search" + QueryString(("q", query), ("max", max.ToString(CultureInfo.InvariantCulture)));
            return SendAsync<SearchResponse>(HttpMethod.Get, path, null, token);
        }

        /// <summary>Ask a grounded question against the corpus.</summary>
        /// <param name="request">The query request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The grounded answer.</returns>
        public Task<QueryResponse> QueryAsync(QueryRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<QueryResponse>(HttpMethod.Post, "/v1.0/query", request, token);
        }

        /// <summary>Ask a grounded question against the corpus.</summary>
        /// <param name="question">The natural-language question.</param>
        /// <param name="maxResults">Maximum sources to retrieve.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The grounded answer.</returns>
        public Task<QueryResponse> QueryAsync(string question, int maxResults = 8, CancellationToken token = default)
        {
            QueryRequest request = new QueryRequest { Question = question, MaxResults = maxResults };
            return QueryAsync(request, token);
        }

        #endregion

        #region Public-Methods-Settings

        /// <summary>Get the server settings object with secrets masked (system admin only).</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The settings envelope, including the masked settings object and metadata.</returns>
        public Task<SettingsEnvelope> GetSettingsAsync(CancellationToken token = default)
        {
            return SendAsync<SettingsEnvelope>(HttpMethod.Get, "/v1.0/settings", null, token);
        }

        /// <summary>Update the server settings object (system admin only).</summary>
        /// <param name="settings">The raw settings object to persist (for example a
        /// <see cref="System.Text.Json.JsonElement"/>, a JSON object, or a dictionary).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The settings envelope describing the outcome, including whether a restart is required.</returns>
        public Task<SettingsEnvelope> UpdateSettingsAsync(object settings, CancellationToken token = default)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return SendAsync<SettingsEnvelope>(HttpMethod.Put, "/v1.0/settings", settings, token);
        }

        #endregion

        #region Public-Methods-Disposal

        /// <summary>
        /// Dispose the client, releasing the underlying <see cref="HttpClient"/> when this instance owns it.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose the client.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing && _OwnsHttpClient) _Http.Dispose();
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private static JsonSerializerOptions BuildJsonOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        private static string NormalizeBaseUrl(string baseUrl)
        {
            string normalized = baseUrl.Trim();
            normalized = normalized.Replace("localhost", "127.0.0.1", StringComparison.OrdinalIgnoreCase);
            return normalized.TrimEnd('/');
        }

        private static string Escape(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty);
        }

        private static string QueryString(params (string Key, string? Value)[] pairs)
        {
            StringBuilder sb = new StringBuilder();
            foreach ((string Key, string? Value) pair in pairs)
            {
                if (string.IsNullOrEmpty(pair.Value)) continue;
                sb.Append(sb.Length == 0 ? '?' : '&');
                sb.Append(Uri.EscapeDataString(pair.Key));
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(pair.Value));
            }
            return sb.ToString();
        }

        private static void AddEnumPairs(List<(string, string?)> pairs, EnumerationQuery? query)
        {
            if (query == null) return;
            pairs.Add(("maxResults", query.MaxResults.HasValue ? query.MaxResults.Value.ToString(CultureInfo.InvariantCulture) : null));
            pairs.Add(("skip", query.Skip.HasValue ? query.Skip.Value.ToString(CultureInfo.InvariantCulture) : null));
            pairs.Add(("order", query.Order));
            pairs.Add(("search", query.Search));
        }

        private static string BuildHistoryQuery(RequestHistoryFilter filter, bool includePaging, bool includeBuckets)
        {
            List<(string, string?)> pairs = new List<(string, string?)>
            {
                ("tenantId", filter.TenantId),
                ("userId", filter.UserId),
                ("method", filter.Method),
                ("pathContains", filter.PathContains),
                ("statusCode", filter.StatusCode?.ToString(CultureInfo.InvariantCulture)),
                ("fromUtc", filter.FromUtc?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)),
                ("toUtc", filter.ToUtc?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))
            };

            if (includePaging)
            {
                pairs.Add(("pageNumber", filter.PageNumber.ToString(CultureInfo.InvariantCulture)));
                pairs.Add(("pageSize", filter.PageSize.ToString(CultureInfo.InvariantCulture)));
            }

            if (includeBuckets)
            {
                pairs.Add(("bucketMinutes", filter.BucketMinutes.ToString(CultureInfo.InvariantCulture)));
            }

            return QueryString(pairs.ToArray());
        }

        private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken token)
        {
            string? responseBody = await SendCoreAsync(method, path, body, token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(responseBody)) return default!;
            T? result = JsonSerializer.Deserialize<T>(responseBody, _Json);
            return result!;
        }

        private async Task<string?> SendCoreAsync(HttpMethod method, string path, object? body, CancellationToken token)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, BaseUrl + path))
            {
                if (!string.IsNullOrEmpty(Token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

                if (body != null)
                {
                    string json = JsonSerializer.Serialize(body, body.GetType(), _Json);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
                {
                    string responseBody = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    int status = (int)response.StatusCode;

                    if (status < 200 || status > 299)
                    {
                        ErrorResponse? error = null;
                        if (!string.IsNullOrWhiteSpace(responseBody))
                        {
                            try { error = JsonSerializer.Deserialize<ErrorResponse>(responseBody, _Json); }
                            catch (JsonException) { error = null; }
                        }
                        throw new PneumaException(status, responseBody, error);
                    }

                    return responseBody;
                }
            }
        }

        private async Task<byte[]> SendBytesCoreAsync(HttpMethod method, string path, CancellationToken token)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, BaseUrl + path))
            {
                if (!string.IsNullOrEmpty(Token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

                using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
                {
                    int status = (int)response.StatusCode;

                    if (status < 200 || status > 299)
                    {
                        string errorBody = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                        ErrorResponse? error = null;
                        if (!string.IsNullOrWhiteSpace(errorBody))
                        {
                            try { error = JsonSerializer.Deserialize<ErrorResponse>(errorBody, _Json); }
                            catch (JsonException) { error = null; }
                        }
                        throw new PneumaException(status, errorBody, error);
                    }

                    return await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                }
            }
        }

        #endregion
    }
}
