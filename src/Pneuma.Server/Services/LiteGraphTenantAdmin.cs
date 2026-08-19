namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;

    /// <summary>
    /// Provisions and hydrates an isolated LiteGraph tenant for a single Pneuma tenant: the LiteGraph tenant,
    /// a default user and credential, and a "Pneuma" graph to hold that tenant's knowledge graph. Returns the
    /// graph GUID so it can be recorded on the Pneuma tenant and used to bind a per-tenant graph client. All
    /// steps are idempotent (existence is checked first) and best-effort (errors are logged, not thrown), and
    /// request bodies are PascalCase to match LiteGraph's case-sensitive deserialization.
    /// </summary>
    public class LiteGraphTenantAdmin : ILiteGraphTenantAdmin
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _RequestJson = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private readonly string _BaseUrl;
        private readonly string? _BearerToken;
        private readonly LoggingModule _Logging;
        private readonly string _Header = "[LiteGraphTenantAdmin] ";

        private const string _GraphName = "Pneuma";
        private const string _UserEmail = "admin@pneuma.local";
        private const string _CredentialName = "Pneuma default credential";

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the LiteGraph tenant admin.</summary>
        /// <param name="baseUrl">Base URL of the LiteGraph server.</param>
        /// <param name="bearerToken">Admin bearer token; applied only when non-empty.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public LiteGraphTenantAdmin(string baseUrl, string? bearerToken, LoggingModule logging)
        {
            if (String.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
            _BaseUrl = baseUrl.TrimEnd('/');
            _BearerToken = bearerToken;
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Ensure the LiteGraph tenant, its default user/credential, and its "Pneuma" graph exist. Idempotent.
        /// </summary>
        /// <param name="tenantGuid">GUID of the LiteGraph tenant to provision.</param>
        /// <param name="name">Display name for the tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The graph GUID, or null when the graph could not be resolved/created.</returns>
        /// <inheritdoc />
        public async Task<string?> ProvisionAsync(string tenantGuid, string name, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantGuid)) return null;

            using (HttpClient client = BuildClient())
            {
                await EnsureTenantAsync(client, tenantGuid, name, token).ConfigureAwait(false);
                string? userGuid = await EnsureUserAsync(client, tenantGuid, token).ConfigureAwait(false);
                await EnsureCredentialAsync(client, tenantGuid, userGuid, token).ConfigureAwait(false);
                return await EnsureGraphAsync(client, tenantGuid, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private HttpClient BuildClient()
        {
            HttpClient client = new HttpClient();
            if (!String.IsNullOrEmpty(_BearerToken))
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _BearerToken);
            }
            return client;
        }

        private async Task EnsureTenantAsync(HttpClient client, string tenantGuid, string name, CancellationToken token)
        {
            string readUrl = _BaseUrl + "/v1.0/tenants/" + tenantGuid;
            HttpStatusCode status = HttpStatusCode.OK;
            await SendAsync(client, HttpMethod.Get, readUrl, null, token, s => status = s).ConfigureAwait(false);
            if (status == HttpStatusCode.OK) return;

            object request = new { GUID = tenantGuid, Name = String.IsNullOrWhiteSpace(name) ? tenantGuid : name };
            await SendAsync(client, HttpMethod.Put, _BaseUrl + "/v1.0/tenants", JsonSerializer.Serialize(request, _RequestJson), token).ConfigureAwait(false);
            _Logging.Info(_Header + "created LiteGraph tenant " + tenantGuid);
        }

        private async Task<string?> EnsureUserAsync(HttpClient client, string tenantGuid, CancellationToken token)
        {
            string listUrl = _BaseUrl + "/v1.0/tenants/" + tenantGuid + "/users";
            string listBody = await SendAsync(client, HttpMethod.Get, listUrl, null, token).ConfigureAwait(false);

            string? existing = FindGuidByStringProperty(listBody, "Email", _UserEmail);
            if (existing != null) return existing;

            object request = new
            {
                TenantGUID = tenantGuid,
                FirstName = "Pneuma",
                LastName = "Admin",
                Email = _UserEmail,
                Password = "password",
                Active = true
            };
            string createBody = await SendAsync(client, HttpMethod.Put, listUrl, JsonSerializer.Serialize(request, _RequestJson), token).ConfigureAwait(false);
            return GetStringProperty(createBody, "GUID", "Guid", "guid");
        }

        private async Task EnsureCredentialAsync(HttpClient client, string tenantGuid, string? userGuid, CancellationToken token)
        {
            string listUrl = _BaseUrl + "/v1.0/tenants/" + tenantGuid + "/credentials";
            string listBody = await SendAsync(client, HttpMethod.Get, listUrl, null, token).ConfigureAwait(false);

            string? existing = FindGuidByStringProperty(listBody, "Name", _CredentialName);
            if (existing != null) return;

            Dictionary<string, object> request = new Dictionary<string, object>
            {
                { "TenantGUID", tenantGuid },
                { "Name", _CredentialName },
                { "Active", true }
            };
            if (!String.IsNullOrEmpty(userGuid)) request["UserGUID"] = userGuid!;

            await SendAsync(client, HttpMethod.Put, listUrl, JsonSerializer.Serialize(request, _RequestJson), token).ConfigureAwait(false);
        }

        private async Task<string?> EnsureGraphAsync(HttpClient client, string tenantGuid, CancellationToken token)
        {
            string listUrl = _BaseUrl + "/v1.0/tenants/" + tenantGuid + "/graphs";
            string listBody = await SendAsync(client, HttpMethod.Get, listUrl, null, token).ConfigureAwait(false);

            string? existing = FindGuidByStringProperty(listBody, "Name", _GraphName);
            if (existing != null) return existing;

            object request = new { Name = _GraphName };
            string createBody = await SendAsync(client, HttpMethod.Put, listUrl, JsonSerializer.Serialize(request, _RequestJson), token).ConfigureAwait(false);
            string? created = GetStringProperty(createBody, "GUID", "Guid", "guid");
            _Logging.Info(_Header + "created graph " + (created ?? "?") + " in LiteGraph tenant " + tenantGuid);
            return created;
        }

        private async Task<string> SendAsync(HttpClient client, HttpMethod method, string url, string? json, CancellationToken token, Action<HttpStatusCode>? onStatus = null)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, url))
            {
                if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await client.SendAsync(request, token).ConfigureAwait(false))
                {
                    onStatus?.Invoke(response.StatusCode);
                    return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                }
            }
        }

        private static string? FindGuidByStringProperty(string body, string propertyName, string propertyValue)
        {
            if (String.IsNullOrWhiteSpace(body)) return null;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement array = FindArray(doc.RootElement);
                    if (array.ValueKind != JsonValueKind.Array) return null;
                    foreach (JsonElement item in array.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        if (item.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
                            && String.Equals(value.GetString(), propertyValue, StringComparison.OrdinalIgnoreCase))
                        {
                            return GetGuid(item);
                        }
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
            return null;
        }

        private static string? GetStringProperty(string body, params string[] names)
        {
            if (String.IsNullOrWhiteSpace(body)) return null;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                    foreach (string name in names)
                    {
                        if (doc.RootElement.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                        {
                            return value.GetString();
                        }
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
            return null;
        }

        private static string? GetGuid(JsonElement element)
        {
            string[] names = new[] { "GUID", "Guid", "guid" };
            foreach (string name in names)
            {
                if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
            return null;
        }

        private static JsonElement FindArray(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array) return root;
            if (root.ValueKind == JsonValueKind.Object)
            {
                string[] preferred = new[] { "Objects", "Graphs", "Users", "Credentials", "Data", "Results" };
                foreach (string name in preferred)
                {
                    if (root.TryGetProperty(name, out JsonElement named) && named.ValueKind == JsonValueKind.Array) return named;
                }
                foreach (JsonProperty prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array) return prop.Value;
                }
            }
            return default;
        }

        #endregion
    }
}
