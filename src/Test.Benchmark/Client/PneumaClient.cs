namespace Test.Benchmark.Client
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Datasets;

    /// <summary>
    /// Typed REST client for Pneuma, used by every benchmark command. It logs in with email and password to get a
    /// tenant-scoped session token (the admin API key carries no tenant), and transparently logs in again when a
    /// long run outlives the token.
    /// </summary>
    public class PneumaClient : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Pneuma base URL, without a trailing slash.
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// Tenant of the logged-in session.
        /// </summary>
        public string? TenantId { get; private set; } = null;

        /// <summary>
        /// The current session token (used to authenticate the MCP endpoint for the agent benchmark).
        /// </summary>
        public string? SessionToken
        {
            get
            {
                return _Token;
            }
        }

        /// <summary>
        /// The underlying HTTP client (shared by metric scrapes).
        /// </summary>
        public HttpClient Http
        {
            get
            {
                return _Http;
            }
        }

        #endregion

        #region Private-Members

        private readonly HttpClient _Http;
        private readonly string _Email;
        private readonly string _Password;
        private readonly SemaphoreSlim _LoginLock = new SemaphoreSlim(1, 1);
        private string? _Token = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate (call <see cref="LoginAsync"/> before other calls).
        /// </summary>
        /// <param name="baseUrl">Pneuma base URL.</param>
        /// <param name="email">Login email.</param>
        /// <param name="password">Login password.</param>
        /// <param name="timeout">Per-request timeout.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public PneumaClient(string baseUrl, string email, string password, TimeSpan timeout)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
            BaseUrl = baseUrl.TrimEnd('/');
            _Email = email ?? throw new ArgumentNullException(nameof(email));
            _Password = password ?? throw new ArgumentNullException(nameof(password));
            SocketsHttpHandler handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 256
            };
            _Http = new HttpClient(handler) { Timeout = timeout };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Log in and remember the session token.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the login is rejected.</exception>
        public async Task LoginAsync(CancellationToken token)
        {
            await _LoginLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                string body = JsonSerializer.Serialize(new Dictionary<string, string> { { "email", _Email }, { "password", _Password } }, HarnessJson.Options);
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/v1.0/token"))
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
                    {
                        string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Login to " + BaseUrl + " failed (" + (int)response.StatusCode + "): " + text);
                        TokenResult? result = JsonSerializer.Deserialize<TokenResult>(text, HarnessJson.Options);
                        if (result == null || string.IsNullOrEmpty(result.Token)) throw new InvalidOperationException("Login to " + BaseUrl + " returned no token.");
                        _Token = result.Token;
                        TenantId = result.TenantId;
                    }
                }
            }
            finally
            {
                _LoginLock.Release();
            }
        }

        /// <summary>
        /// Wait until the server answers its health route.
        /// </summary>
        /// <param name="timeout">How long to wait.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when healthy within the timeout.</returns>
        public async Task<bool> WaitForHealthAsync(TimeSpan timeout, CancellationToken token)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                try
                {
                    using (HttpResponseMessage response = await _Http.GetAsync(BaseUrl + "/v1.0/api/health", token).ConfigureAwait(false))
                    {
                        if (response.IsSuccessStatusCode) return true;
                    }
                }
                catch (HttpRequestException)
                {
                }
                catch (TaskCanceledException) when (!token.IsCancellationRequested)
                {
                }

                await Task.Delay(1000, token).ConfigureAwait(false);
            }

            return false;
        }

        /// <summary>
        /// List every model endpoint.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The endpoints.</returns>
        public Task<List<ModelRunnerInfo>> ListModelRunnersAsync(CancellationToken token)
        {
            return ListAllAsync<ModelRunnerInfo>("/v1.0/model-runners", token);
        }

        /// <summary>
        /// Create a model endpoint.
        /// </summary>
        /// <param name="body">Endpoint definition.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created endpoint.</returns>
        public async Task<ModelRunnerInfo> CreateModelRunnerAsync(CreateModelRunnerBody body, CancellationToken token)
        {
            TimedResponse<ModelRunnerInfo> response = await SendAsync<ModelRunnerInfo>(HttpMethod.Post, "/v1.0/model-runners", body, token).ConfigureAwait(false);
            return Require(response, "create model endpoint '" + body.Name + "'");
        }

        /// <summary>
        /// List collections.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The collections.</returns>
        public Task<List<CollectionInfo>> ListCollectionsAsync(CancellationToken token)
        {
            return ListAllAsync<CollectionInfo>("/v1.0/collections", token);
        }

        /// <summary>
        /// Create a collection.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="dimensionality">Vector dimensionality.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The collection.</returns>
        public async Task<CollectionInfo> CreateCollectionAsync(string name, int dimensionality, CancellationToken token)
        {
            Dictionary<string, object> body = new Dictionary<string, object> { { "name", name }, { "description", "Pneuma benchmark collection" }, { "dimensionality", dimensionality } };
            TimedResponse<CollectionInfo> response = await SendAsync<CollectionInfo>(HttpMethod.Put, "/v1.0/collections", body, token).ConfigureAwait(false);
            return Require(response, "create collection '" + name + "'");
        }

        /// <summary>
        /// Delete a collection and its documents.
        /// </summary>
        /// <param name="id">Collection id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DeleteCollectionAsync(string id, CancellationToken token)
        {
            await SendAsync<object>(HttpMethod.Delete, "/v1.0/collections/" + Uri.EscapeDataString(id), null, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Find subjects whose name contains a string.
        /// </summary>
        /// <param name="search">Substring.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Matching subjects.</returns>
        public Task<List<SubjectBody>> SearchSubjectsAsync(string search, CancellationToken token)
        {
            return ListAllAsync<SubjectBody>("/v1.0/subjects?search=" + Uri.EscapeDataString(search), token);
        }

        /// <summary>
        /// Read a subject, or null when it does not exist.
        /// </summary>
        /// <param name="id">Subject id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The subject or null.</returns>
        public async Task<SubjectBody?> GetSubjectAsync(string id, CancellationToken token)
        {
            TimedResponse<SubjectBody> response = await SendAsync<SubjectBody>(HttpMethod.Get, "/v1.0/subjects/" + Uri.EscapeDataString(id), null, token).ConfigureAwait(false);
            return response.IsSuccess ? response.Value : null;
        }

        /// <summary>
        /// Create a subject.
        /// </summary>
        /// <param name="subject">Subject definition.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created subject.</returns>
        public async Task<SubjectBody> CreateSubjectAsync(SubjectBody subject, CancellationToken token)
        {
            TimedResponse<SubjectBody> response = await SendAsync<SubjectBody>(HttpMethod.Post, "/v1.0/subjects", subject, token).ConfigureAwait(false);
            return Require(response, "create subject '" + subject.DisplayName + "'");
        }

        /// <summary>
        /// Update a subject (the whole record is sent back).
        /// </summary>
        /// <param name="subject">Subject with its id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated subject.</returns>
        public async Task<SubjectBody> UpdateSubjectAsync(SubjectBody subject, CancellationToken token)
        {
            TimedResponse<SubjectBody> response = await SendAsync<SubjectBody>(HttpMethod.Put, "/v1.0/subjects/" + Uri.EscapeDataString(subject.Id ?? string.Empty), subject, token).ConfigureAwait(false);
            return Require(response, "update subject '" + subject.DisplayName + "'");
        }

        /// <summary>
        /// Delete a subject (asynchronous server-side cascade).
        /// </summary>
        /// <param name="id">Subject id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DeleteSubjectAsync(string id, CancellationToken token)
        {
            await SendAsync<object>(HttpMethod.Delete, "/v1.0/subjects/" + Uri.EscapeDataString(id), null, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Submit a link for ingestion.
        /// </summary>
        /// <param name="subjectId">Subject id.</param>
        /// <param name="body">Link definition.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created link.</returns>
        public async Task<LinkInfo> SubmitLinkAsync(string subjectId, SubmitLinkBody body, CancellationToken token)
        {
            TimedResponse<LinkInfo> response = await SendAsync<LinkInfo>(HttpMethod.Post, "/v1.0/subjects/" + Uri.EscapeDataString(subjectId) + "/links", body, token).ConfigureAwait(false);
            return Require(response, "submit link " + body.Url);
        }

        /// <summary>
        /// List every link of a subject.
        /// </summary>
        /// <param name="subjectId">Subject id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The links.</returns>
        public Task<List<LinkInfo>> ListSubjectLinksAsync(string subjectId, CancellationToken token)
        {
            return ListAllAsync<LinkInfo>("/v1.0/subjects/" + Uri.EscapeDataString(subjectId) + "/links", token);
        }

        /// <summary>
        /// Queue a fresh ingestion job for each link (the bulk reingest route).
        /// </summary>
        /// <param name="linkIds">Link ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>HTTP status code.</returns>
        public async Task<int> ReingestLinksAsync(List<string> linkIds, CancellationToken token)
        {
            TimedResponse<object> response = await SendAsync<object>(HttpMethod.Post, "/v1.0/links/reingest", new Dictionary<string, List<string>> { { "ids", linkIds } }, token).ConfigureAwait(false);
            return response.StatusCode;
        }

        /// <summary>
        /// List every ingestion job of a subject.
        /// </summary>
        /// <param name="subjectId">Subject id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The jobs.</returns>
        public Task<List<JobInfo>> ListSubjectJobsAsync(string subjectId, CancellationToken token)
        {
            return ListAllAsync<JobInfo>("/v1.0/jobs?subjectId=" + Uri.EscapeDataString(subjectId), token);
        }

        /// <summary>
        /// Read a job with its per-stage events.
        /// </summary>
        /// <param name="jobId">Job id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The job detail, or null.</returns>
        public async Task<JobDetail?> GetJobAsync(string jobId, CancellationToken token)
        {
            TimedResponse<JobDetail> response = await SendAsync<JobDetail>(HttpMethod.Get, "/v1.0/jobs/" + Uri.EscapeDataString(jobId), null, token).ConfigureAwait(false);
            return response.IsSuccess ? response.Value : null;
        }

        /// <summary>
        /// Fetch a pipeline artifact of a link (atoms, chunks, ...) as raw text.
        /// </summary>
        /// <param name="linkId">Link id.</param>
        /// <param name="artifact">atoms, chunks, vectors, subgraph, or source.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The artifact text, or null when absent.</returns>
        public async Task<string?> GetLinkArtifactAsync(string linkId, string artifact, CancellationToken token)
        {
            TimedResponse<string> response = await SendRawAsync(HttpMethod.Get, "/v1.0/links/" + Uri.EscapeDataString(linkId) + "/" + artifact, null, token).ConfigureAwait(false);
            return response.IsSuccess ? response.Value : null;
        }

        /// <summary>
        /// Search a subject.
        /// </summary>
        /// <param name="subjectId">Subject id.</param>
        /// <param name="query">Query text.</param>
        /// <param name="options">Search options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The timed page of hits.</returns>
        public Task<TimedResponse<EnumerationPage<SubjectSearchHit>>> SubjectSearchAsync(string subjectId, string query, SearchOptions options, CancellationToken token)
        {
            // Query values are form-encoded (spaces as '+'), exactly as the dashboards' URLSearchParams does, so the
            // benchmark measures what real clients get.
            StringBuilder path = new StringBuilder();
            path.Append("/v1.0/subjects/").Append(Uri.EscapeDataString(subjectId)).Append("/search?q=").Append(WebUtility.UrlEncode(query));
            path.Append("&mode=").Append(WebUtility.UrlEncode(options.Mode));
            path.Append("&maxResults=").Append(options.MaxResults);
            if (options.Filter != null) path.Append("&filter=").Append(WebUtility.UrlEncode(JsonSerializer.Serialize(options.Filter, HarnessJson.Options)));
            foreach (KeyValuePair<string, string> extra in options.Extra) path.Append('&').Append(WebUtility.UrlEncode(extra.Key)).Append('=').Append(WebUtility.UrlEncode(extra.Value));
            return SendAsync<EnumerationPage<SubjectSearchHit>>(HttpMethod.Get, path.ToString(), null, token);
        }

        /// <summary>
        /// Warm a subject's embedding model.
        /// </summary>
        /// <param name="subjectId">Subject id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task WarmSearchAsync(string subjectId, CancellationToken token)
        {
            await SendAsync<object>(HttpMethod.Post, "/v1.0/subjects/" + Uri.EscapeDataString(subjectId) + "/search/warmup", new Dictionary<string, string>(), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Warm a subject's answering model.
        /// </summary>
        /// <param name="subjectId">Subject id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task WarmAnswerAsync(string subjectId, CancellationToken token)
        {
            await SendAsync<object>(HttpMethod.Post, "/v1.0/warmup", new Dictionary<string, string> { { "subjectId", subjectId } }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Ask a grounded question.
        /// </summary>
        /// <param name="body">Query body.</param>
        /// <param name="global">True for <c>/v1.0/query/global</c>.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The timed answer.</returns>
        public Task<TimedResponse<QueryResult>> QueryAsync(QueryBody body, bool global, CancellationToken token)
        {
            return SendAsync<QueryResult>(HttpMethod.Post, global ? "/v1.0/query/global" : "/v1.0/query", body, token);
        }

        /// <summary>
        /// Build a subject's community summaries.
        /// </summary>
        /// <param name="subjectId">Subject id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>HTTP status code.</returns>
        public async Task<int> BuildCommunitiesAsync(string subjectId, CancellationToken token)
        {
            TimedResponse<string> response = await SendRawAsync(HttpMethod.Post, "/v1.0/subjects/" + Uri.EscapeDataString(subjectId) + "/communities/build", "{}", token).ConfigureAwait(false);
            return response.StatusCode;
        }

        /// <summary>
        /// Run one agentic chat turn and return its final <c>complete</c> (or <c>error</c>) event.
        /// </summary>
        /// <param name="body">Chat body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The timed final event.</returns>
        public async Task<TimedResponse<ChatEvent>> ChatAsync(ChatBody body, CancellationToken token)
        {
            TimedResponse<ChatEvent> result = new TimedResponse<ChatEvent>();
            Stopwatch sw = Stopwatch.StartNew();
            for (int attempt = 0; attempt < 2; attempt++)
            {
                await EnsureLoggedInAsync(token).ConfigureAwait(false);
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/v1.0/chat/stream"))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _Token);
                    request.Content = new StringContent(JsonSerializer.Serialize(body, HarnessJson.Options), Encoding.UTF8, "application/json");
                    try
                    {
                        using (HttpResponseMessage response = await _Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                        {
                            result.StatusCode = (int)response.StatusCode;
                            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                            {
                                await LoginAsync(token).ConfigureAwait(false);
                                continue;
                            }

                            if (!response.IsSuccessStatusCode)
                            {
                                result.Error = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                                break;
                            }

                            using (Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                string? line;
                                while ((line = await reader.ReadLineAsync(token).ConfigureAwait(false)) != null)
                                {
                                    if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                                    string json = line.Substring(5).Trim();
                                    if (json.Length == 0 || json[0] != '{') continue;
                                    ChatEvent? evt = null;
                                    try
                                    {
                                        evt = JsonSerializer.Deserialize<ChatEvent>(json, HarnessJson.Options);
                                    }
                                    catch (JsonException)
                                    {
                                    }

                                    if (evt == null) continue;
                                    if (string.Equals(evt.Type, "complete", StringComparison.Ordinal)) result.Value = evt;
                                    else if (string.Equals(evt.Type, "error", StringComparison.Ordinal)) result.Error = evt.Message ?? "error event";
                                }
                            }
                        }
                    }
                    catch (HttpRequestException e)
                    {
                        result.Error = e.Message;
                    }
                    catch (TaskCanceledException e) when (!token.IsCancellationRequested)
                    {
                        result.Error = "timeout: " + e.Message;
                    }
                }

                break;
            }

            result.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            return result;
        }

        /// <summary>
        /// Read the Prometheus exposition text.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The text, or null when unavailable.</returns>
        public async Task<string?> GetMetricsTextAsync(CancellationToken token)
        {
            try
            {
                return await _Http.GetStringAsync(BaseUrl + "/metrics", token).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private async Task<List<T>> ListAllAsync<T>(string path, CancellationToken token)
        {
            List<T> all = new List<T>();
            int skip = 0;
            string separator = path.Contains('?') ? "&" : "?";
            while (true)
            {
                TimedResponse<EnumerationPage<T>> page = await SendAsync<EnumerationPage<T>>(HttpMethod.Get, path + separator + "maxResults=1000&skip=" + skip, null, token).ConfigureAwait(false);
                EnumerationPage<T> value = Require(page, "list " + path);
                all.AddRange(value.Objects);
                if (value.EndOfResults || value.Objects.Count == 0) break;
                skip += value.Objects.Count;
            }

            return all;
        }

        private static T Require<T>(TimedResponse<T> response, string what)
        {
            if (!response.IsSuccess || response.Value == null)
                throw new InvalidOperationException("Pneuma could not " + what + " (HTTP " + response.StatusCode + "): " + response.Error);
            return response.Value;
        }

        private async Task EnsureLoggedInAsync(CancellationToken token)
        {
            if (_Token == null) await LoginAsync(token).ConfigureAwait(false);
        }

        private async Task<TimedResponse<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken token)
        {
            string? json = body == null ? null : JsonSerializer.Serialize(body, body.GetType(), HarnessJson.Options);
            TimedResponse<string> raw = await SendRawAsync(method, path, json, token).ConfigureAwait(false);
            TimedResponse<T> result = new TimedResponse<T> { StatusCode = raw.StatusCode, ElapsedMs = raw.ElapsedMs, Error = raw.Error };
            if (raw.StatusCode >= 200 && raw.StatusCode < 300)
            {
                if (string.IsNullOrWhiteSpace(raw.Value))
                {
                    if (typeof(T) == typeof(object)) result.Value = (T)(object)string.Empty;
                    return result;
                }

                try
                {
                    result.Value = JsonSerializer.Deserialize<T>(raw.Value!, HarnessJson.Options);
                }
                catch (JsonException e)
                {
                    result.Error = "unparseable response: " + e.Message;
                }
            }
            else if (result.Error == null)
            {
                result.Error = raw.Value;
            }

            return result;
        }

        private async Task<TimedResponse<string>> SendRawAsync(HttpMethod method, string path, string? json, CancellationToken token)
        {
            TimedResponse<string> result = new TimedResponse<string>();
            for (int attempt = 0; attempt < 2; attempt++)
            {
                await EnsureLoggedInAsync(token).ConfigureAwait(false);
                Stopwatch sw = Stopwatch.StartNew();
                using (HttpRequestMessage request = new HttpRequestMessage(method, BaseUrl + path))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _Token);
                    if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    try
                    {
                        using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
                        {
                            string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                            result.ElapsedMs = sw.Elapsed.TotalMilliseconds;
                            result.StatusCode = (int)response.StatusCode;
                            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                            {
                                await LoginAsync(token).ConfigureAwait(false);
                                continue;
                            }

                            result.Value = text;
                            if (!response.IsSuccessStatusCode) result.Error = text;
                        }
                    }
                    catch (HttpRequestException e)
                    {
                        result.ElapsedMs = sw.Elapsed.TotalMilliseconds;
                        result.Error = e.Message;
                    }
                    catch (TaskCanceledException e) when (!token.IsCancellationRequested)
                    {
                        result.ElapsedMs = sw.Elapsed.TotalMilliseconds;
                        result.Error = "timeout: " + e.Message;
                    }
                }

                break;
            }

            return result;
        }

        /// <summary>
        /// Dispose resources.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                _Http.Dispose();
                _LoginLock.Dispose();
            }

            _Disposed = true;
        }

        #endregion
    }
}
