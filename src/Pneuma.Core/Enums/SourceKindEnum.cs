namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>Where a link's content comes from, which decides how ingestion retrieves it.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SourceKindEnum
    {
        /// <summary>A URL fetched over HTTP(S) through the fetch-safety policy.</summary>
        Url,
        /// <summary>Content pushed through the API and kept in the blob store.</summary>
        Inline,
        /// <summary>An object a crawl plan found (a crawled page, a file on a share, an object in a bucket).</summary>
        Crawl
    }
}
