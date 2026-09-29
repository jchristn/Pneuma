namespace Test.Shared.Support
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Serialization;

    /// <summary>
    /// Shared HTTP helpers for suites that drive a <see cref="TestServer"/> over REST: log in as a principal and send
    /// JSON requests with a bearer token. One shared <see cref="HttpClient"/> serves every suite.
    /// </summary>
    public static class ApiClientHelper
    {
        #region Private-Members

        private static readonly HttpClient _Http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        #endregion

        #region Public-Methods

        /// <summary>Log in with email and password and return the bearer token.</summary>
        /// <param name="baseUrl">Server base URL.</param>
        /// <param name="email">Account email.</param>
        /// <param name="password">Account password.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The session token.</returns>
        /// <exception cref="InvalidOperationException">Thrown when login fails or returns no token.</exception>
        public static async Task<string> LoginAsync(string baseUrl, string email, string password, CancellationToken ct)
        {
            string body = "{\"email\":\"" + email + "\",\"password\":\"" + password + "\"}";
            using (HttpResponseMessage response = await SendAsync(HttpMethod.Post, baseUrl + "/v1.0/token", null, body, ct).ConfigureAwait(false))
            {
                if (response.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("login failed: " + (int)response.StatusCode);
                string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                TokenResponse? token = Json.Deserialize<TokenResponse>(text);
                if (token == null || String.IsNullOrEmpty(token.Token)) throw new InvalidOperationException("login returned no token");
                return token.Token;
            }
        }

        /// <summary>Send a request with an optional bearer token and JSON body. The caller disposes the response.</summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="url">Absolute URL.</param>
        /// <param name="token">Bearer token, or null.</param>
        /// <param name="body">JSON body, or null.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The response.</returns>
        public static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token, string? body, CancellationToken ct)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, url))
            {
                if (!String.IsNullOrEmpty(token)) request.Headers.Add("Authorization", "Bearer " + token);
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                return await _Http.SendAsync(request, ct).ConfigureAwait(false);
            }
        }

        /// <summary>Send a request and return the status code and body text.</summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="url">Absolute URL.</param>
        /// <param name="token">Bearer token, or null.</param>
        /// <param name="body">JSON body, or null.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The status code and body.</returns>
        public static async Task<ApiResult> CallAsync(HttpMethod method, string url, string? token, string? body, CancellationToken ct)
        {
            using (HttpResponseMessage response = await SendAsync(method, url, token, body, ct).ConfigureAwait(false))
            {
                string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return new ApiResult { StatusCode = (int)response.StatusCode, Body = text };
            }
        }

        /// <summary>Read a top-level string property from a JSON object, or empty when absent.</summary>
        /// <param name="json">JSON text.</param>
        /// <param name="property">Property name (case-sensitive, as serialized).</param>
        /// <returns>The value, or empty.</returns>
        public static string ExtractString(string json, string property)
        {
            if (String.IsNullOrWhiteSpace(json)) return String.Empty;
            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json))
            {
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                    && doc.RootElement.TryGetProperty(property, out System.Text.Json.JsonElement value)
                    && value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return value.GetString() ?? String.Empty;
                }
            }

            return String.Empty;
        }

        /// <summary>Create a collection and a subject configured with it (default models), returning the subject id.</summary>
        /// <param name="baseUrl">Server base URL.</param>
        /// <param name="token">Bearer token.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The subject id.</returns>
        /// <exception cref="InvalidOperationException">Thrown when either create fails.</exception>
        public static async Task<string> CreateConfiguredSubjectAsync(string baseUrl, string token, CancellationToken ct)
        {
            ApiResult collection = await CallAsync(HttpMethod.Put, baseUrl + "/v1.0/collections", token, "{\"name\":\"test\",\"dimensionality\":8}", ct).ConfigureAwait(false);
            if (collection.StatusCode != 201) throw new InvalidOperationException("collection create failed: " + collection.StatusCode + " " + collection.Body);
            string collectionId = ExtractString(collection.Body, "id");

            ApiResult subject = await CallAsync(HttpMethod.Post, baseUrl + "/v1.0/subjects", token,
                "{\"displayName\":\"Example Subject\",\"type\":\"Person\",\"embeddingModel\":\"default\",\"inferenceModel\":\"default\",\"collection\":\"" + collectionId + "\"}", ct).ConfigureAwait(false);
            string subjectId = ExtractString(subject.Body, "id");
            if (String.IsNullOrEmpty(subjectId)) throw new InvalidOperationException("subject create failed: " + subject.StatusCode + " " + subject.Body);
            return subjectId;
        }

        #endregion
    }
}
