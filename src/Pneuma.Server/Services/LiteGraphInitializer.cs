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
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Interfaces;
    using SyslogLogging;

    /// <summary>
    /// Idempotent, best-effort startup initializer for the LiteGraph knowledge-graph store. Ensures the
    /// Pneuma tenant, a default user, a default credential, and the "Pneuma" graph exist. All steps swallow
    /// and log errors so the Pneuma server still boots when LiteGraph is unavailable. Request bodies are
    /// PascalCase to match LiteGraph's case-sensitive deserialization.
    /// </summary>
    public class LiteGraphInitializer
    {
        #region Private-Members

        // LiteGraph deserializes request bodies case-sensitively and expects PascalCase, mirroring the
        // convention in LiteGraphClient.
        private static readonly JsonSerializerOptions _RequestJson = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private readonly string _BaseUrl;
        private readonly string? _BearerToken;
        private readonly string _TenantGuid;
        private readonly IGraphRepository _Graph;
        private readonly LoggingModule _Logging;
        private readonly string _Header = "[LiteGraphInitializer] ";

        private const string _DefaultUserEmail = "admin@pneuma.local";
        private const string _DefaultCredentialName = "Pneuma default credential";

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the initializer.</summary>
        /// <param name="baseUrl">Base URL of the LiteGraph server.</param>
        /// <param name="bearerToken">Admin bearer token; applied only when non-empty.</param>
        /// <param name="tenantGuid">Tenant GUID to ensure and scope users/credentials under.</param>
        /// <param name="graph">LiteGraph client, used to ensure the "Pneuma" graph exists.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public LiteGraphInitializer(string baseUrl, string? bearerToken, string tenantGuid, IGraphRepository graph, LoggingModule logging)
        {
            if (String.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
            _BaseUrl = baseUrl.TrimEnd('/');
            _BearerToken = bearerToken;
            _TenantGuid = tenantGuid ?? String.Empty;
            _Graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>Run the initialization steps. Best-effort; never throws.</summary>
        /// <param name="token">Cancellation token.</param>
        public async Task InitializeAsync(CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(_TenantGuid))
            {
                _Logging.Warn(_Header + "no LiteGraph tenant GUID configured; skipping tenant/user/credential initialization");
            }
            else
            {
                using (HttpClient client = BuildClient())
                {
                    await EnsureTenantAsync(client, token).ConfigureAwait(false);
                    string? userGuid = await EnsureUserAsync(client, token).ConfigureAwait(false);
                    await EnsureCredentialAsync(client, userGuid, token).ConfigureAwait(false);
                }
            }

            await EnsureGraphAsync(token).ConfigureAwait(false);
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

        private async Task EnsureTenantAsync(HttpClient client, CancellationToken token)
        {
            try
            {
                string url = _BaseUrl + "/v1.0/tenants/" + _TenantGuid;
                HttpStatusCode status = HttpStatusCode.OK;
                await SendAsync(client, HttpMethod.Get, url, null, token, s => status = s).ConfigureAwait(false);
                if (status == HttpStatusCode.OK)
                {
                    _Logging.Debug(_Header + "tenant " + _TenantGuid + " already exists");
                    return;
                }

                object request = new { GUID = _TenantGuid, Name = "Pneuma" };
                await SendAsync(client, HttpMethod.Put, _BaseUrl + "/v1.0/tenants", JsonSerializer.Serialize(request, _RequestJson), token, s => status = s).ConfigureAwait(false);
                _Logging.Info(_Header + "created tenant " + _TenantGuid);
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "tenant initialization failed (continuing): " + e.Message);
            }
        }

        private async Task<string?> EnsureUserAsync(HttpClient client, CancellationToken token)
        {
            try
            {
                string listUrl = _BaseUrl + "/v1.0/tenants/" + _TenantGuid + "/users";
                string listBody = await SendAsync(client, HttpMethod.Get, listUrl, null, token).ConfigureAwait(false);

                string? existing = FindGuidByStringProperty(listBody, "Email", _DefaultUserEmail);
                if (existing != null)
                {
                    _Logging.Debug(_Header + "default user already exists (" + existing + ")");
                    return existing;
                }

                object request = new
                {
                    TenantGUID = _TenantGuid,
                    FirstName = "Pneuma",
                    LastName = "Admin",
                    Email = _DefaultUserEmail,
                    Password = "password",
                    Active = true,
                    IsTenantAdmin = true
                };
                string createBody = await SendAsync(client, HttpMethod.Put, listUrl, JsonSerializer.Serialize(request, _RequestJson), token).ConfigureAwait(false);
                string? created = GetStringProperty(createBody, "GUID", "Guid", "guid");
                _Logging.Info(_Header + "created default user" + (created != null ? " (" + created + ")" : String.Empty));
                return created;
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "user initialization failed (continuing): " + e.Message);
                return null;
            }
        }

        private async Task EnsureCredentialAsync(HttpClient client, string? userGuid, CancellationToken token)
        {
            try
            {
                string listUrl = _BaseUrl + "/v1.0/tenants/" + _TenantGuid + "/credentials";
                string listBody = await SendAsync(client, HttpMethod.Get, listUrl, null, token).ConfigureAwait(false);

                string? existing = FindGuidByStringProperty(listBody, "Name", _DefaultCredentialName);
                if (existing != null)
                {
                    _Logging.Debug(_Header + "default credential already exists (" + existing + ")");
                    return;
                }

                Dictionary<string, object> request = new Dictionary<string, object>
                {
                    { "TenantGUID", _TenantGuid },
                    { "Name", _DefaultCredentialName },
                    { "Active", true }
                };
                if (!String.IsNullOrEmpty(userGuid)) request["UserGUID"] = userGuid!;

                await SendAsync(client, HttpMethod.Put, listUrl, JsonSerializer.Serialize(request, _RequestJson), token).ConfigureAwait(false);
                _Logging.Info(_Header + "created default credential");
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "credential initialization failed (continuing): " + e.Message);
            }
        }

        private async Task EnsureGraphAsync(CancellationToken token)
        {
            try
            {
                string graphGuid = await _Graph.EnsureGraphAsync(token).ConfigureAwait(false);
                _Logging.Info(_Header + "ensured Pneuma graph (" + graphGuid + ")");
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "graph initialization failed (continuing): " + e.Message);
            }
        }

        private async Task<string> SendAsync(HttpClient client, HttpMethod method, string url, string? json, CancellationToken token, System.Action<HttpStatusCode>? onStatus = null)
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
                string[] preferred = new[] { "Objects", "Users", "Credentials", "Data", "Results" };
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
