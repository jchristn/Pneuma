namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>Where the blob store keeps its objects (uploaded files, pushed text, job source copies, classification cache).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlobProviderEnum
    {
        /// <summary>Files in a local directory (<c>Integrations.Blob.Directory</c>).</summary>
        Disk,
        /// <summary>Objects in an S3-compatible bucket (<c>S3.Buckets.Blobs</c>, using the <c>S3</c> connection settings).</summary>
        S3
    }
}
