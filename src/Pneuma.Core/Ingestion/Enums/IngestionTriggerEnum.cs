namespace Pneuma.Core.Ingestion.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>What created an ingestion job.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum IngestionTriggerEnum
    {
        /// <summary>A link or content was submitted (REST, SDK, MCP, or a dashboard).</summary>
        Submit,
        /// <summary>An operator asked to re-ingest a link.</summary>
        Reingest,
        /// <summary>The link's scheduled refresh found the source changed (or could not tell).</summary>
        Refresh,
        /// <summary>A crawl plan found the object new, changed, or failed last time.</summary>
        Crawl
    }
}
