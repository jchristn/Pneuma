namespace Pneuma.Server.Settings
{
    /// <summary>
    /// BLOB storage settings (Blobject).
    /// </summary>
    public class BlobSettings
    {
        /// <summary>Storage provider (e.g. Disk).</summary>
        public string Provider { get; set; } = "Disk";

        /// <summary>Directory for the disk provider.</summary>
        public string Directory { get; set; } = "blobs";
    }
}
