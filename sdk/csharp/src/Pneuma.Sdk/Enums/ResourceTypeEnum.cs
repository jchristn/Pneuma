namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Resource types evaluated during authorization.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ResourceTypeEnum
    {
        /// <summary>Wildcard: matches any resource.</summary>
        All,
        /// <summary>Account resource.</summary>
        Account,
        /// <summary>Tenant resource.</summary>
        Tenant,
        /// <summary>Administrator resource.</summary>
        Admin,
        /// <summary>User resource.</summary>
        User,
        /// <summary>Credential resource.</summary>
        Credential,
        /// <summary>Authentication session resource.</summary>
        Session,
        /// <summary>Role resource.</summary>
        Role,
        /// <summary>Permission resource.</summary>
        Permission,
        /// <summary>Role/credential assignment resource.</summary>
        Assignment,
        /// <summary>Audit record resource.</summary>
        Audit,
        /// <summary>Subject archive resource.</summary>
        Subject,
        /// <summary>Ingestion job resource.</summary>
        IngestionJob,
        /// <summary>Model runner resource.</summary>
        ModelRunner,
        /// <summary>Prompt resource.</summary>
        Prompt,
        /// <summary>Knowledge-graph node resource.</summary>
        GraphNode,
        /// <summary>Search index resource.</summary>
        SearchIndex,
        /// <summary>Provenance source resource.</summary>
        Source
    }
}
