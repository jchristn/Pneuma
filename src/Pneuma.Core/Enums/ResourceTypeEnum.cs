namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Resource types evaluated during authorization. Includes Pneuma-specific resources.
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
        Source,
        /// <summary>Crawl plan resource (a scheduled source a subject is kept in sync with).</summary>
        CrawlPlan,
        /// <summary>Crawl operation resource (one run of a crawl plan).</summary>
        CrawlOperation,
        /// <summary>Ontology resource (a tenant's governed ontology and its versions). Execute approves and retires versions.</summary>
        Ontology
    }
}
