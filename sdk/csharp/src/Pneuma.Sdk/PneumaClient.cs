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
    using Pneuma.Sdk.Enums;
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

        #region Public-Methods-Subject-Prompts

        /// <summary>List a subject's effective prompts, one per key, showing the global baseline, any
        /// subject-level override, and the resolved effective content.</summary>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The subject's prompts.</returns>
        public Task<List<SubjectPromptDto>> ListSubjectPromptsAsync(string subjectId, CancellationToken token = default)
        {
            return SendAsync<List<SubjectPromptDto>>(HttpMethod.Get, "/v1.0/subjects/" + Escape(subjectId) + "/prompts", null, token);
        }

        /// <summary>Set (create or update) a subject-level prompt override for a given key.</summary>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="key">Prompt key to override.</param>
        /// <param name="request">The override content and merge mode.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The subject's effective prompt for the key after the update.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public Task<SubjectPromptDto> SetSubjectPromptAsync(string subjectId, string key, SubjectPromptUpdateRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<SubjectPromptDto>(HttpMethod.Put, "/v1.0/subjects/" + Escape(subjectId) + "/prompts/" + Escape(key), request, token);
        }

        /// <summary>Delete a subject-level prompt override for a given key, reverting the key to the global prompt.</summary>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="key">Prompt key whose override to remove.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task DeleteSubjectPromptAsync(string subjectId, string key, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Delete, "/v1.0/subjects/" + Escape(subjectId) + "/prompts/" + Escape(key), null, token);
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

        /// <summary>
        /// Push content (text, Markdown, HTML, or JSON) into a subject; it is stored and ingested like a link. Reusing an
        /// external key replaces earlier content with that key.
        /// </summary>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="request">The content.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result: the link, the queued job id, and whether earlier content was replaced.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        /// <exception cref="PneumaException">Thrown with 400 for invalid content, 404 for an unknown subject, or 413 when too large.</exception>
        public Task<ContentSubmitResult> SubmitContentAsync(string subjectId, SubmitContentRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<ContentSubmitResult>(HttpMethod.Post, "/v1.0/subjects/" + Escape(subjectId) + "/content", request, token);
        }

        /// <summary>Push up to 100 content items in one call; every item is attempted and reported.</summary>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="request">The items.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts and a result per item.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public Task<ContentBatchResponse> SubmitContentBatchAsync(string subjectId, SubmitContentBatchRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<ContentBatchResponse>(HttpMethod.Post, "/v1.0/subjects/" + Escape(subjectId) + "/content/batch", request, token);
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

        /// <summary>List the model endpoints available for ingestion, grouped by usage.</summary>
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

        /// <summary>Set a link's scheduled refresh: an interval (0 off, 60 to 525600 minutes) or the subject's default.</summary>
        /// <param name="id">Link identifier.</param>
        /// <param name="request">The interval, or <see cref="LinkRefreshRequest.UseSubjectDefault"/>.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated link.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public Task<SubjectLink> SetLinkRefreshAsync(string id, LinkRefreshRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<SubjectLink>(HttpMethod.Put, "/v1.0/links/" + Escape(id), request, token);
        }

        /// <summary>Set the scheduled refresh of several links (<see cref="LinkRefreshRequest.Ids"/>).</summary>
        /// <param name="request">Link ids and the interval, or <see cref="LinkRefreshRequest.UseSubjectDefault"/>.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>How many links were updated and which were skipped.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public Task<LinkRefreshBulkResult> BulkSetLinkRefreshAsync(LinkRefreshRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<LinkRefreshBulkResult>(HttpMethod.Post, "/v1.0/links/refresh-interval", request, token);
        }

        /// <summary>Check a link for changes now (a conditional GET); a changed link is re-ingested.</summary>
        /// <param name="id">Link identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What the check found.</returns>
        public Task<LinkRefreshResult> RefreshLinkNowAsync(string id, CancellationToken token = default)
        {
            return SendAsync<LinkRefreshResult>(HttpMethod.Post, "/v1.0/links/" + Escape(id) + "/refresh", null, token);
        }

        /// <summary>What the new subject wizard can do for the caller (ontology modes, limits, whether a completion model exists).</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The options.</returns>
        public Task<WizardOptions> GetWizardOptionsAsync(CancellationToken token = default)
        {
            return SendAsync<WizardOptions>(HttpMethod.Get, "/v1.0/subject-wizard/options", null, token);
        }

        /// <summary>Draft the subject brief (reads the draft's reference URL when set).</summary>
        /// <param name="request">The draft so far.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The brief.</returns>
        public Task<WizardResult<WizardBrief>> DraftWizardBriefAsync(WizardGenerateRequest request, CancellationToken token = default)
        {
            return SendAsync<WizardResult<WizardBrief>>(HttpMethod.Post, "/v1.0/subject-wizard/brief", request, token);
        }

        /// <summary>Draft example questions (Mode "replace" keeps locked and edited ones; "more" adds new ones).</summary>
        /// <param name="request">The draft so far.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The full question list.</returns>
        public Task<WizardResult<List<WizardQuestion>>> DraftWizardQuestionsAsync(WizardGenerateRequest request, CancellationToken token = default)
        {
            return SendAsync<WizardResult<List<WizardQuestion>>>(HttpMethod.Post, "/v1.0/subject-wizard/questions", request, token);
        }

        /// <summary>Draft the ontology from the brief and questions.</summary>
        /// <param name="request">The draft so far.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The ontology.</returns>
        public Task<WizardResult<WizardOntology>> DraftWizardOntologyAsync(WizardGenerateRequest request, CancellationToken token = default)
        {
            return SendAsync<WizardResult<WizardOntology>>(HttpMethod.Post, "/v1.0/subject-wizard/ontology", request, token);
        }

        /// <summary>Draft the subject's prompt additions.</summary>
        /// <param name="request">The draft so far.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The prompts.</returns>
        public Task<WizardResult<WizardPrompts>> DraftWizardPromptsAsync(WizardGenerateRequest request, CancellationToken token = default)
        {
            return SendAsync<WizardResult<WizardPrompts>>(HttpMethod.Post, "/v1.0/subject-wizard/prompts", request, token);
        }

        /// <summary>Suggest where content for the subject might come from.</summary>
        /// <param name="request">The draft so far.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The suggestions.</returns>
        public Task<WizardResult<List<WizardSourceSuggestion>>> DraftWizardSourcesAsync(WizardGenerateRequest request, CancellationToken token = default)
        {
            return SendAsync<WizardResult<List<WizardSourceSuggestion>>>(HttpMethod.Post, "/v1.0/subject-wizard/sources", request, token);
        }

        /// <summary>Clean up a draft ontology and render it as the classifier will see it (no model call).</summary>
        /// <param name="draft">The draft.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The rendered ontology.</returns>
        public Task<WizardRenderedOntology> RenderWizardOntologyAsync(SubjectWizardDraft draft, CancellationToken token = default)
        {
            return SendAsync<WizardRenderedOntology>(HttpMethod.Post, "/v1.0/subject-wizard/render-ontology", new WizardGenerateRequest { Draft = draft }, token);
        }

        /// <summary>Create the subject, its starter questions, and its ontology from a finished draft.</summary>
        /// <param name="request">The finished draft and settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What was created.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public Task<WizardCommitResult> CommitSubjectWizardAsync(WizardCommitRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<WizardCommitResult>(HttpMethod.Post, "/v1.0/subject-wizard/commit", request, token);
        }

        /// <summary>List a subject's starter questions.</summary>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The questions, in order.</returns>
        public Task<List<SubjectQuestion>> GetSubjectQuestionsAsync(string subjectId, CancellationToken token = default)
        {
            return SendAsync<List<SubjectQuestion>>(HttpMethod.Get, "/v1.0/subjects/" + Escape(subjectId) + "/questions", null, token);
        }

        /// <summary>Replace a subject's starter questions.</summary>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="questions">The questions, in order.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored questions.</returns>
        public Task<List<SubjectQuestion>> SetSubjectQuestionsAsync(string subjectId, List<SubjectQuestion> questions, CancellationToken token = default)
        {
            return SendAsync<List<SubjectQuestion>>(HttpMethod.Put, "/v1.0/subjects/" + Escape(subjectId) + "/questions", new SubjectQuestionsRequest { Questions = questions ?? new List<SubjectQuestion>() }, token);
        }

        /// <summary>Create up to 100 evaluation facts at once.</summary>
        /// <param name="facts">The facts.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>How many were created and the facts.</returns>
        public Task<EvalFactBulkResult> BulkCreateEvalFactsAsync(List<EvalFact> facts, CancellationToken token = default)
        {
            return SendAsync<EvalFactBulkResult>(HttpMethod.Post, "/v1.0/eval/facts/bulk", new EvalFactBulkRequest { Facts = facts ?? new List<EvalFact>() }, token);
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

        /// <summary>Get a link's stored chunks pipeline artifact as raw JSON.</summary>
        /// <param name="id">Link identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The chunks artifact as a raw JSON string, or null if empty.</returns>
        /// <exception cref="PneumaException">Thrown with status 404 when the artifact isn't present yet.</exception>
        public Task<string?> GetLinkChunksAsync(string id, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Get, "/v1.0/links/" + Escape(id) + "/chunks", null, token);
        }

        /// <summary>Get a link's stored embeddings (vectors) pipeline artifact as raw JSON.</summary>
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

        /// <summary>List ingestion jobs, optionally filtered by status, failure category, and whether they have warnings.</summary>
        /// <param name="query">Optional paging and filtering options.</param>
        /// <param name="status">Optional status filter (for example "Queued", "Failed").</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="failureCategory">Optional failure-category filter (for example Fetch or ModelUnavailable).</param>
        /// <param name="hasWarnings">Optional filter: true for jobs with warnings, false for jobs without.</param>
        /// <returns>Paginated result of jobs.</returns>
        public Task<EnumerationResult<IngestionJob>> ListJobsAsync(EnumerationQuery? query = null, string? status = null, CancellationToken token = default, IngestionFailureCategoryEnum? failureCategory = null, bool? hasWarnings = null)
        {
            string? warningsText = hasWarnings == null ? null : (hasWarnings.Value ? "true" : "false");
            List<(string, string?)> pairs = new List<(string, string?)> { ("status", status), ("failureCategory", failureCategory?.ToString()), ("hasWarnings", warningsText) };
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

        #region Public-Methods-Crawling

        /// <summary>List the crawl plan types this server supports, each with its settings schema.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The type catalog.</returns>
        public Task<List<CrawlPlanTypeInfo>> ListCrawlPlanTypesAsync(CancellationToken token = default)
        {
            return SendAsync<List<CrawlPlanTypeInfo>>(HttpMethod.Get, "/v1.0/crawl-plan-types", null, token);
        }

        /// <summary>Create a crawl plan that keeps a subject in sync with a source.</summary>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="request">The plan.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created plan (secret values are never returned).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public Task<CrawlPlan> CreateCrawlPlanAsync(string subjectId, CrawlPlanRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<CrawlPlan>(HttpMethod.Post, "/v1.0/subjects/" + Escape(subjectId) + "/crawl-plans", request, token);
        }

        /// <summary>List crawl plans, optionally for one subject.</summary>
        /// <param name="subjectId">Subject identifier, or null for all.</param>
        /// <param name="query">Optional paging and search.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of plans.</returns>
        public Task<EnumerationResult<CrawlPlan>> ListCrawlPlansAsync(string? subjectId = null, EnumerationQuery? query = null, CancellationToken token = default)
        {
            string path = "/v1.0/crawl-plans" + (query?.ToQueryString() ?? string.Empty);
            path = AppendQuery(path, "subjectId", subjectId);
            return SendAsync<EnumerationResult<CrawlPlan>>(HttpMethod.Get, path, null, token);
        }

        /// <summary>Get a crawl plan.</summary>
        /// <param name="id">Crawl plan identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The plan.</returns>
        public Task<CrawlPlan> GetCrawlPlanAsync(string id, CancellationToken token = default)
        {
            return SendAsync<CrawlPlan>(HttpMethod.Get, "/v1.0/crawl-plans/" + Escape(id), null, token);
        }

        /// <summary>Replace a crawl plan's configuration. Secrets left null keep their stored values.</summary>
        /// <param name="id">Crawl plan identifier.</param>
        /// <param name="request">The configuration.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated plan.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public Task<CrawlPlan> UpdateCrawlPlanAsync(string id, CrawlPlanRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<CrawlPlan>(HttpMethod.Put, "/v1.0/crawl-plans/" + Escape(id), request, token);
        }

        /// <summary>Delete a crawl plan.</summary>
        /// <param name="id">Crawl plan identifier.</param>
        /// <param name="deleteLinks">True to also delete the links the plan created.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>How many links were deleted or kept.</returns>
        public Task<CrawlPlanDeleteResult> DeleteCrawlPlanAsync(string id, bool deleteLinks = false, CancellationToken token = default)
        {
            return SendAsync<CrawlPlanDeleteResult>(HttpMethod.Delete, "/v1.0/crawl-plans/" + Escape(id) + (deleteLinks ? "?deleteLinks=true" : string.Empty), null, token);
        }

        /// <summary>Test a draft crawl plan's connection without saving it.</summary>
        /// <param name="request">The draft plan.</param>
        /// <param name="fromPlanId">A stored plan whose secrets fill the draft's empty ones, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Each connectivity step.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public Task<ConnectivityResult> TestCrawlPlanDraftAsync(CrawlPlanRequest request, string? fromPlanId = null, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<ConnectivityResult>(HttpMethod.Post, AppendQuery("/v1.0/crawl-plans/test", "fromPlanId", fromPlanId), request, token);
        }

        /// <summary>Test a stored crawl plan's connection.</summary>
        /// <param name="id">Crawl plan identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Each connectivity step.</returns>
        public Task<ConnectivityResult> TestCrawlPlanAsync(string id, CancellationToken token = default)
        {
            return SendAsync<ConnectivityResult>(HttpMethod.Post, "/v1.0/crawl-plans/" + Escape(id) + "/test", null, token);
        }

        /// <summary>Preview what a crawl plan would do if it ran now; nothing is changed.</summary>
        /// <param name="id">Crawl plan identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The preview.</returns>
        public Task<CrawlPreview> PreviewCrawlPlanAsync(string id, CancellationToken token = default)
        {
            return SendAsync<CrawlPreview>(HttpMethod.Post, "/v1.0/crawl-plans/" + Escape(id) + "/preview", null, token);
        }

        /// <summary>Start a crawl operation now (it runs in the background).</summary>
        /// <param name="id">Crawl plan identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The started operation.</returns>
        public Task<CrawlOperation> StartCrawlPlanAsync(string id, CancellationToken token = default)
        {
            return SendAsync<CrawlOperation>(HttpMethod.Post, "/v1.0/crawl-plans/" + Escape(id) + "/start", null, token);
        }

        /// <summary>Stop a crawl plan's running operation.</summary>
        /// <param name="id">Crawl plan identifier.</param>
        /// <param name="token">Cancellation token.</param>
        public Task StopCrawlPlanAsync(string id, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Post, "/v1.0/crawl-plans/" + Escape(id) + "/stop", null, token);
        }

        /// <summary>List a crawl plan's operations, newest first.</summary>
        /// <param name="id">Crawl plan identifier.</param>
        /// <param name="query">Optional paging and search.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of operations.</returns>
        public Task<EnumerationResult<CrawlOperation>> ListCrawlPlanOperationsAsync(string id, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return SendAsync<EnumerationResult<CrawlOperation>>(HttpMethod.Get, "/v1.0/crawl-plans/" + Escape(id) + "/operations" + (query?.ToQueryString() ?? string.Empty), null, token);
        }

        /// <summary>List the objects a crawl plan tracks.</summary>
        /// <param name="id">Crawl plan identifier.</param>
        /// <param name="status">Only objects with this status, or null.</param>
        /// <param name="query">Optional paging and search.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of objects.</returns>
        public Task<EnumerationResult<CrawlObject>> ListCrawlPlanObjectsAsync(string id, CrawlObjectStatusEnum? status = null, EnumerationQuery? query = null, CancellationToken token = default)
        {
            string path = AppendQuery("/v1.0/crawl-plans/" + Escape(id) + "/objects" + (query?.ToQueryString() ?? string.Empty), "status", status?.ToString());
            return SendAsync<EnumerationResult<CrawlObject>>(HttpMethod.Get, path, null, token);
        }

        /// <summary>List crawl operations, optionally for one plan.</summary>
        /// <param name="planId">Plan identifier, or null for all.</param>
        /// <param name="status">Only operations with this status, or null.</param>
        /// <param name="query">Optional paging and search.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of operations.</returns>
        public Task<EnumerationResult<CrawlOperation>> ListCrawlOperationsAsync(string? planId = null, CrawlOperationStatusEnum? status = null, EnumerationQuery? query = null, CancellationToken token = default)
        {
            string path = AppendQuery("/v1.0/crawl-operations" + (query?.ToQueryString() ?? string.Empty), "planId", planId);
            path = AppendQuery(path, "status", status?.ToString());
            return SendAsync<EnumerationResult<CrawlOperation>>(HttpMethod.Get, path, null, token);
        }

        /// <summary>Get a crawl operation.</summary>
        /// <param name="id">Crawl operation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The operation.</returns>
        public Task<CrawlOperation> GetCrawlOperationAsync(string id, CancellationToken token = default)
        {
            return SendAsync<CrawlOperation>(HttpMethod.Get, "/v1.0/crawl-operations/" + Escape(id), null, token);
        }

        /// <summary>List what a crawl operation did with each object.</summary>
        /// <param name="id">Crawl operation identifier.</param>
        /// <param name="action">Only objects with this action, or null.</param>
        /// <param name="query">Optional paging and search.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of per-object results.</returns>
        public Task<EnumerationResult<CrawlOperationObject>> ListCrawlOperationObjectsAsync(string id, CrawlActionEnum? action = null, EnumerationQuery? query = null, CancellationToken token = default)
        {
            string path = AppendQuery("/v1.0/crawl-operations/" + Escape(id) + "/objects" + (query?.ToQueryString() ?? string.Empty), "action", action?.ToString());
            return SendAsync<EnumerationResult<CrawlOperationObject>>(HttpMethod.Get, path, null, token);
        }

        /// <summary>Confirm the deletions a held crawl operation is waiting on.</summary>
        /// <param name="id">Crawl operation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The operation after the deletions ran.</returns>
        public Task<CrawlOperation> ConfirmCrawlDeletionsAsync(string id, CancellationToken token = default)
        {
            return SendAsync<CrawlOperation>(HttpMethod.Post, "/v1.0/crawl-operations/" + Escape(id) + "/confirm-deletions", null, token);
        }

        #endregion

        #region Public-Methods-Ontologies

        /// <summary>List the built-in ontology templates.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The templates.</returns>
        public Task<List<OntologyTemplateInfo>> ListOntologyTemplatesAsync(CancellationToken token = default)
        {
            return SendAsync<List<OntologyTemplateInfo>>(HttpMethod.Get, "/v1.0/ontology-templates", null, token);
        }

        /// <summary>List the tenant's ontologies.</summary>
        /// <param name="query">Optional paging and search.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of ontologies.</returns>
        public Task<EnumerationResult<Ontology>> ListOntologiesAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            return SendAsync<EnumerationResult<Ontology>>(HttpMethod.Get, "/v1.0/ontologies" + (query?.ToQueryString() ?? string.Empty), null, token);
        }

        /// <summary>Create an ontology; its first version is a draft (empty, from a template, or a copy of a version).</summary>
        /// <param name="request">The ontology.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The ontology with its versions.</returns>
        public Task<OntologyDetail> CreateOntologyAsync(OntologyCreateRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<OntologyDetail>(HttpMethod.Post, "/v1.0/ontologies", request, token);
        }

        /// <summary>Get an ontology with its versions and the subjects that pin them.</summary>
        /// <param name="id">Ontology identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The ontology.</returns>
        public Task<OntologyDetail> GetOntologyAsync(string id, CancellationToken token = default)
        {
            return SendAsync<OntologyDetail>(HttpMethod.Get, "/v1.0/ontologies/" + Escape(id), null, token);
        }

        /// <summary>Rename or re-describe an ontology.</summary>
        /// <param name="id">Ontology identifier.</param>
        /// <param name="request">The new name and description.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The ontology.</returns>
        public Task<OntologyDetail> UpdateOntologyAsync(string id, OntologyUpdateRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<OntologyDetail>(HttpMethod.Put, "/v1.0/ontologies/" + Escape(id), request, token);
        }

        /// <summary>Delete an ontology and its versions (refused while a subject pins one).</summary>
        /// <param name="id">Ontology identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        public Task DeleteOntologyAsync(string id, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Delete, "/v1.0/ontologies/" + Escape(id), null, token);
        }

        /// <summary>List an ontology's versions, newest first.</summary>
        /// <param name="id">Ontology identifier.</param>
        /// <param name="query">Optional paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of versions.</returns>
        public Task<EnumerationResult<OntologyVersion>> ListOntologyVersionsAsync(string id, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return SendAsync<EnumerationResult<OntologyVersion>>(HttpMethod.Get, "/v1.0/ontologies/" + Escape(id) + "/versions" + (query?.ToQueryString() ?? string.Empty), null, token);
        }

        /// <summary>Start a new draft that copies a version (by default the newest).</summary>
        /// <param name="id">Ontology identifier.</param>
        /// <param name="basedOnVersionId">Version to copy, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The draft.</returns>
        public Task<OntologyVersion> CreateOntologyDraftAsync(string id, string? basedOnVersionId = null, CancellationToken token = default)
        {
            return SendAsync<OntologyVersion>(HttpMethod.Post, "/v1.0/ontologies/" + Escape(id) + "/versions", new OntologyDraftRequest { BasedOnVersionId = basedOnVersionId }, token);
        }

        /// <summary>Have the inference model propose a new draft from a subject's content or sample text.</summary>
        /// <param name="id">Ontology identifier.</param>
        /// <param name="request">What to sample and how.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The proposed draft.</returns>
        public Task<OntologyVersion> ProposeOntologyAsync(string id, OntologyProposeRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<OntologyVersion>(HttpMethod.Post, "/v1.0/ontologies/" + Escape(id) + "/propose", request, token);
        }

        /// <summary>Get a version with its types, rules, concepts, and approval problems.</summary>
        /// <param name="versionId">Version identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The version.</returns>
        public Task<OntologyVersion> GetOntologyVersionAsync(string versionId, CancellationToken token = default)
        {
            return SendAsync<OntologyVersion>(HttpMethod.Get, "/v1.0/ontology-versions/" + Escape(versionId), null, token);
        }

        /// <summary>Replace a draft's contents.</summary>
        /// <param name="versionId">Version identifier.</param>
        /// <param name="version">The contents.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The saved draft with its approval problems.</returns>
        public Task<OntologyVersion> UpdateOntologyVersionAsync(string versionId, OntologyVersion version, CancellationToken token = default)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            return SendAsync<OntologyVersion>(HttpMethod.Put, "/v1.0/ontology-versions/" + Escape(versionId), version, token);
        }

        /// <summary>Delete a draft version.</summary>
        /// <param name="versionId">Version identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        public Task DeleteOntologyVersionAsync(string versionId, CancellationToken token = default)
        {
            return SendCoreAsync(HttpMethod.Delete, "/v1.0/ontology-versions/" + Escape(versionId), null, token);
        }

        /// <summary>Approve a draft (needs Ontology Execute).</summary>
        /// <param name="versionId">Version identifier.</param>
        /// <param name="changeSummary">Optional change summary.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The approved version.</returns>
        public Task<OntologyVersion> ApproveOntologyVersionAsync(string versionId, string? changeSummary = null, CancellationToken token = default)
        {
            return SendAsync<OntologyVersion>(HttpMethod.Post, "/v1.0/ontology-versions/" + Escape(versionId) + "/approve", new OntologyApproveRequest { ChangeSummary = changeSummary }, token);
        }

        /// <summary>Retire an approved version (refused while a subject pins it).</summary>
        /// <param name="versionId">Version identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The retired version.</returns>
        public Task<OntologyVersion> RetireOntologyVersionAsync(string versionId, CancellationToken token = default)
        {
            return SendAsync<OntologyVersion>(HttpMethod.Post, "/v1.0/ontology-versions/" + Escape(versionId) + "/retire", null, token);
        }

        /// <summary>Compare a version with another (by default the version it was copied from).</summary>
        /// <param name="versionId">Version identifier.</param>
        /// <param name="againstVersionId">Version to compare with, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The differences.</returns>
        public Task<OntologyVersionDiff> DiffOntologyVersionAsync(string versionId, string? againstVersionId = null, CancellationToken token = default)
        {
            return SendAsync<OntologyVersionDiff>(HttpMethod.Get, AppendQuery("/v1.0/ontology-versions/" + Escape(versionId) + "/diff", "against", againstVersionId), null, token);
        }

        /// <summary>Get the definition text the classifier sees for a version.</summary>
        /// <param name="versionId">Version identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The definition.</returns>
        public Task<OntologyDefinitionResponse> GetOntologyDefinitionAsync(string versionId, CancellationToken token = default)
        {
            return SendAsync<OntologyDefinitionResponse>(HttpMethod.Get, "/v1.0/ontology-versions/" + Escape(versionId) + "/definition", null, token);
        }

        /// <summary>Export a version as OWL and SKOS.</summary>
        /// <param name="versionId">Version identifier.</param>
        /// <param name="format">"turtle" (default) or "jsonld".</param>
        /// <param name="baseIri">Optional absolute base IRI.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The document.</returns>
        public async Task<string> ExportOntologyVersionAsync(string versionId, string format = "turtle", string? baseIri = null, CancellationToken token = default)
        {
            string path = AppendQuery(AppendQuery("/v1.0/ontology-versions/" + Escape(versionId) + "/export", "format", format), "baseIri", baseIri);
            return await SendCoreAsync(HttpMethod.Get, path, null, token).ConfigureAwait(false) ?? string.Empty;
        }

        /// <summary>Import a SKOS taxonomy into a draft.</summary>
        /// <param name="versionId">Draft version identifier.</param>
        /// <param name="document">The SKOS document (Turtle or JSON-LD).</param>
        /// <param name="format">"turtle" (default) or "jsonld".</param>
        /// <param name="mode">"merge" (default) or "replace".</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What was imported.</returns>
        public async Task<TaxonomyImportResult> ImportTaxonomyAsync(string versionId, string document, string format = "turtle", string mode = "merge", CancellationToken token = default)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            string path = AppendQuery(AppendQuery("/v1.0/ontology-versions/" + Escape(versionId) + "/taxonomy/import", "format", format), "mode", mode);
            string contentType = string.Equals(format, "jsonld", StringComparison.OrdinalIgnoreCase) ? "application/ld+json" : "text/turtle";
            string? body = await SendTextCoreAsync(HttpMethod.Post, path, document, contentType, token).ConfigureAwait(false);
            return JsonSerializer.Deserialize<TaxonomyImportResult>(body ?? "{}", _Json)!;
        }

        /// <summary>Show how a subject classifies: its pinned version, the definition the classifier sees, and its settings.</summary>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The view.</returns>
        public Task<SubjectOntologyView> GetSubjectOntologyAsync(string subjectId, CancellationToken token = default)
        {
            return SendAsync<SubjectOntologyView>(HttpMethod.Get, "/v1.0/subjects/" + Escape(subjectId) + "/ontology", null, token);
        }

        /// <summary>Pin a subject to an approved version, or unpin it (null).</summary>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="ontologyVersionId">Approved version, or null to unpin.</param>
        /// <param name="retag">Queue a retag when the taxonomy changes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The view.</returns>
        public Task<SubjectOntologyView> SetSubjectOntologyAsync(string subjectId, string? ontologyVersionId, bool retag = true, CancellationToken token = default)
        {
            return SendAsync<SubjectOntologyView>(HttpMethod.Put, "/v1.0/subjects/" + Escape(subjectId) + "/ontology", new SubjectOntologyRequest { OntologyVersionId = ontologyVersionId, Retag = retag }, token);
        }

        /// <summary>List a subject's ontology violations.</summary>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="status">Only this status, or null.</param>
        /// <param name="query">Optional paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of violations.</returns>
        public Task<EnumerationResult<OntologyViolation>> ListOntologyViolationsAsync(string subjectId, OntologyViolationStatusEnum? status = null, EnumerationQuery? query = null, CancellationToken token = default)
        {
            string path = AppendQuery("/v1.0/subjects/" + Escape(subjectId) + "/ontology-violations" + (query?.ToQueryString() ?? string.Empty), "status", status?.ToString());
            return SendAsync<EnumerationResult<OntologyViolation>>(HttpMethod.Get, path, null, token);
        }

        /// <summary>Release a quarantined element into the graph.</summary>
        /// <param name="violationId">Violation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The violation.</returns>
        public Task<OntologyViolation> ReleaseOntologyViolationAsync(string violationId, CancellationToken token = default)
        {
            return SendAsync<OntologyViolation>(HttpMethod.Post, "/v1.0/ontology-violations/" + Escape(violationId) + "/release", null, token);
        }

        /// <summary>Dismiss a quarantined element.</summary>
        /// <param name="violationId">Violation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The violation.</returns>
        public Task<OntologyViolation> DismissOntologyViolationAsync(string violationId, CancellationToken token = default)
        {
            return SendAsync<OntologyViolation>(HttpMethod.Post, "/v1.0/ontology-violations/" + Escape(violationId) + "/dismiss", null, token);
        }

        /// <summary>List a subject's ontology operations.</summary>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="query">Optional paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of operations.</returns>
        public Task<EnumerationResult<OntologyOperation>> ListOntologyOperationsAsync(string subjectId, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return SendAsync<EnumerationResult<OntologyOperation>>(HttpMethod.Get, "/v1.0/subjects/" + Escape(subjectId) + "/ontology-operations" + (query?.ToQueryString() ?? string.Empty), null, token);
        }

        /// <summary>Queue a background ontology operation (Validate, Retag, or DriftCheck).</summary>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="request">The operation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The queued operation.</returns>
        public Task<OntologyOperation> StartOntologyOperationAsync(string subjectId, OntologyOperationRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendAsync<OntologyOperation>(HttpMethod.Post, "/v1.0/subjects/" + Escape(subjectId) + "/ontology-operations", request, token);
        }

        /// <summary>Get an ontology operation with its items.</summary>
        /// <param name="operationId">Operation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The operation.</returns>
        public Task<OntologyOperationDetail> GetOntologyOperationAsync(string operationId, CancellationToken token = default)
        {
            return SendAsync<OntologyOperationDetail>(HttpMethod.Get, "/v1.0/ontology-operations/" + Escape(operationId), null, token);
        }

        /// <summary>Export a subject's graph.</summary>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="format">"json" (default), "jsonld", "turtle", or "graphml".</param>
        /// <param name="baseIri">Optional absolute base IRI for the RDF formats.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The document.</returns>
        public async Task<string> ExportSubjectGraphAsync(string subjectId, string format = "json", string? baseIri = null, CancellationToken token = default)
        {
            string path = AppendQuery(AppendQuery("/v1.0/subjects/" + Escape(subjectId) + "/graph/export", "format", format), "baseIri", baseIri);
            return await SendCoreAsync(HttpMethod.Get, path, null, token).ConfigureAwait(false) ?? string.Empty;
        }

        /// <summary>Remove the classification cache entries a subject stored.</summary>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>How many entries were removed.</returns>
        public Task<ClassificationCacheClearResult> ClearClassificationCacheAsync(string subjectId, CancellationToken token = default)
        {
            return SendAsync<ClassificationCacheClearResult>(HttpMethod.Delete, "/v1.0/subjects/" + Escape(subjectId) + "/classification-cache", null, token);
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

        private static string AppendQuery(string path, string name, string? value)
        {
            if (string.IsNullOrEmpty(value)) return path;
            return path + (path.Contains("?") ? "&" : "?") + name + "=" + Uri.EscapeDataString(value);
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

        private async Task<string?> SendTextCoreAsync(HttpMethod method, string path, string text, string contentType, CancellationToken token)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, BaseUrl + path))
            {
                if (!string.IsNullOrEmpty(Token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
                request.Content = new StringContent(text, Encoding.UTF8, contentType);

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
