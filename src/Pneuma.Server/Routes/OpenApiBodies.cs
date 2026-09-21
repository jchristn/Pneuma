namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.Json.Serialization;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Builds OpenAPI request-body metadata (schema + a representative JSON example) for write routes so the
    /// API Explorer can show a filled-in placeholder body instead of an empty editor. Examples are trimmed to
    /// the fields a client is expected to supply: server-managed members (auto-generated ids, audit timestamps,
    /// path-derived tenant ids, issued tokens, and computed/transient values) are stripped so the example is a
    /// valid request rather than a dump of the full stored object. This is an API-presentation policy, so it
    /// lives here rather than being baked into the shared Core models.
    /// </summary>
    public static class OpenApiBodies
    {
        #region Private-Members

        // Keep null-valued properties so a client-settable field with no default (e.g. a required string) still
        // appears in the example rather than the whole body collapsing; enums render as their string names.
        private static readonly JsonSerializerOptions _ExampleOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        // Namespace of stored entities. When one is used directly as a request body, its server-managed members
        // (below) are stripped from the example. Request DTOs live in Pneuma.Core.Requests and are already
        // input-shaped, so they are serialized verbatim.
        private const string _ModelsNamespace = "Pneuma.Core.Models";

        // Property names a client should never supply: auto-generated ids, audit timestamps, path-derived scope
        // ids, lifecycle status, provisioning handles, and issued secrets. Stripped from entity examples only.
        private static readonly HashSet<string> _ServerManagedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Id", "TenantId", "SubjectId", "CreatedUtc", "LastUpdateUtc", "Status", "DeletionStatus",
            "GraphRootNodeId", "IsProtected", "LiteGraphTenantGuid", "LiteGraphGraphGuid",
            "BearerToken", "AccessKey", "SecretKey"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build request-body metadata for a type with a parameterless constructor, using a fresh instance as the
        /// example (server-managed members stripped).
        /// </summary>
        /// <typeparam name="T">Request body type.</typeparam>
        /// <param name="description">Human-readable description of the body.</param>
        /// <param name="required">Whether the body is required. Defaults to true.</param>
        /// <returns>Request-body metadata carrying a schema and a representative example.</returns>
        public static OpenApiRequestBodyMetadata Json<T>(string description, bool required = true)
            where T : new()
        {
            return Build(new T(), description, required);
        }

        /// <summary>
        /// Build request-body metadata from an explicit sample instance (for bodies without a convenient
        /// parameterless type, e.g. a pre-populated example).
        /// </summary>
        /// <param name="sample">Sample instance to serialize as the example.</param>
        /// <param name="description">Human-readable description of the body.</param>
        /// <param name="required">Whether the body is required. Defaults to true.</param>
        /// <returns>Request-body metadata carrying a schema and a representative example.</returns>
        public static OpenApiRequestBodyMetadata Build(object sample, string description, bool required = true)
        {
            OpenApiRequestBodyMetadata body = OpenApiRequestBodyMetadata.Json(OpenApiSchemaMetadata.Create("object"), description, required);

            // Serialize the sample, strip server-managed members, and embed the result as a JsonElement. The
            // OpenAPI generator serializes with a camelCase policy that does not rewrite JsonElement content, so
            // the example's shape is preserved verbatim.
            try
            {
                if (sample != null)
                {
                    string json = JsonSerializer.Serialize(sample, _ExampleOptions);
                    if (!String.IsNullOrEmpty(json))
                    {
                        JsonNode? node = JsonNode.Parse(json);
                        if (node != null)
                        {
                            StripServerManagedMembers(node, sample);
                            body.Content["application/json"].Example = JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());
                        }
                    }
                }
            }
            catch
            {
                // Best-effort: a body without an example is still a valid, useful spec entry.
            }

            return body;
        }

        #endregion

        #region Private-Methods

        private static void StripServerManagedMembers(JsonNode node, object sample)
        {
            if (node == null || sample == null) return;

            if (node is JsonArray array)
            {
                Type sampleType = sample.GetType();
                Type? elementType = sampleType.IsGenericType ? sampleType.GetGenericArguments()[0] : null;
                foreach (JsonNode? item in array) RemoveMembers(item as JsonObject, elementType);
                return;
            }

            RemoveMembers(node as JsonObject, sample.GetType());
        }

        private static void RemoveMembers(JsonObject? obj, Type? type)
        {
            if (obj == null || type == null) return;
            // Only strip stored entities; request DTOs are already input-shaped.
            if (!String.Equals(type.Namespace, _ModelsNamespace, StringComparison.Ordinal)) return;

            List<string> toRemove = new List<string>();
            foreach (KeyValuePair<string, JsonNode?> member in obj)
            {
                if (_ServerManagedNames.Contains(member.Key)) toRemove.Add(member.Key);
            }
            foreach (string name in toRemove) obj.Remove(name);
        }

        #endregion
    }
}
