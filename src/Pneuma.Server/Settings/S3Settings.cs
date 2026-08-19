namespace Pneuma.Server.Settings
{
    /// <summary>
    /// S3-compatible object-storage settings (Less3). Holds the endpoint and credentials plus the
    /// per-stage bucket names used to persist pipeline artifacts.
    /// </summary>
    public class S3Settings
    {
        #region Public-Members

        /// <summary>Whether pipeline artifacts are persisted to S3-compatible object storage.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Service endpoint URL of the S3-compatible store (e.g. http://less3:8000).</summary>
        public string Endpoint { get; set; } = "http://less3:8000";

        /// <summary>Region string reported to the S3 API.</summary>
        public string Region { get; set; } = "us-west-1";

        /// <summary>Access key (Less3 seeds a "default" access key on first boot).</summary>
        public string AccessKey { get; set; } = "default";

        /// <summary>Secret key (Less3 seeds a "default" secret key on first boot).</summary>
        public string SecretKey { get; set; } = "default";

        /// <summary>Use path-style addressing (required for Less3 / MinIO-style servers).</summary>
        public bool ForcePathStyle { get; set; } = true;

        /// <summary>Per-stage bucket names.</summary>
        public S3BucketSettings Buckets { get; set; } = new S3BucketSettings();

        #endregion
    }
}
