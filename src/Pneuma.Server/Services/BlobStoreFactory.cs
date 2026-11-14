namespace Pneuma.Server.Services
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Storage;
    using Pneuma.Server.Settings;
    using SyslogLogging;

    /// <summary>
    /// Builds the blob store the settings call for: a local directory (<c>Integrations.Blob.Provider</c> = <c>Disk</c>) or a
    /// bucket (<c>S3</c>) reached with the <c>S3</c> section's endpoint, region, credentials, and addressing style, named by
    /// <c>S3.Buckets.Blobs</c>. The S3 choice does not depend on <c>S3.Enabled</c>, which governs the pipeline artifact
    /// buckets. There is no fallback between providers.
    /// </summary>
    public static class BlobStoreFactory
    {
        #region Public-Methods

        /// <summary>Create the configured blob store.</summary>
        /// <param name="settings">Application settings.</param>
        /// <param name="logging">Logging module, or null.</param>
        /// <returns>The blob store. An <see cref="S3BlobStore"/> is disposable and its bucket should be ensured at startup.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for an unknown provider.</exception>
        public static IBlobStore Create(AppSettings settings, LoggingModule? logging)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            BlobSettings blob = settings.Integrations.Blob;

            switch (blob.Provider)
            {
                case BlobProviderEnum.S3:
                    logging?.Info("[BlobStoreFactory] blob store: S3 bucket '" + settings.S3.Buckets.Blobs + "' at " + settings.S3.Endpoint);
                    return new S3BlobStore(
                        settings.S3.Endpoint,
                        settings.S3.Region,
                        settings.S3.AccessKey,
                        settings.S3.SecretKey,
                        settings.S3.ForcePathStyle,
                        settings.S3.Buckets.Blobs,
                        logging);

                case BlobProviderEnum.Disk:
                    logging?.Info("[BlobStoreFactory] blob store: disk directory '" + blob.Directory + "'");
                    return new DiskBlobStore(blob.Directory);

                default:
                    throw new ArgumentOutOfRangeException(nameof(settings), "Unknown blob provider '" + blob.Provider + "'.");
            }
        }

        #endregion
    }
}
