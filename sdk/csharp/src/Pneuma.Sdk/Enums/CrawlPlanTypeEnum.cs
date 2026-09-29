namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>What a crawl plan crawls.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CrawlPlanTypeEnum
    {
        /// <summary>A web site: start URLs, followed links, and the site map, within a scope.</summary>
        Web,
        /// <summary>The URLs listed in sitemaps and sitemap indexes, with their last-modified dates.</summary>
        Sitemap,
        /// <summary>Objects in an Amazon S3 or S3-compatible bucket.</summary>
        S3,
        /// <summary>Files on a CIFS/SMB share.</summary>
        Cifs,
        /// <summary>Files on an NFS export.</summary>
        Nfs,
        /// <summary>Files in a GitHub repository (default branch).</summary>
        GitHub,
        /// <summary>Blobs in an Azure Blob Storage container.</summary>
        AzureBlob,
        /// <summary>Objects in a Google Cloud Storage bucket.</summary>
        GoogleCloud,
        /// <summary>Files in a folder on the Pneuma server, under an administrator-allowed root.</summary>
        LocalFolder
    }
}
