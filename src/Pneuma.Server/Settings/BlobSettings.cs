namespace Pneuma.Server.Settings
{
    using System;
    using Pneuma.Core.Enums;

    /// <summary>
    /// Blob store settings. The blob store holds uploaded files, pushed text, each job's copy of its source, and the
    /// classification cache. <see cref="Provider"/> chooses the backend for all of it; there is no fallback between them.
    /// </summary>
    public class BlobSettings
    {
        #region Public-Members

        /// <summary>
        /// Storage backend: <c>Disk</c> (files under <see cref="Directory"/>) or <c>S3</c> (objects in the bucket named by
        /// <c>S3.Buckets.Blobs</c>, reached with the <c>S3</c> section's endpoint, region, credentials, and addressing style).
        /// Default Disk. Changing it requires a restart and does not move existing objects.
        /// </summary>
        public BlobProviderEnum Provider { get; set; } = BlobProviderEnum.Disk;

        /// <summary>Directory for the Disk provider. Default <c>blobs</c>.</summary>
        public string Directory
        {
            get { return _Directory; }
            set { _Directory = String.IsNullOrWhiteSpace(value) ? throw new ArgumentNullException(nameof(Directory)) : value; }
        }

        #endregion

        #region Private-Members

        private string _Directory = "blobs";

        #endregion
    }
}
