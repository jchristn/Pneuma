namespace Pneuma.Core.Storage
{
    using System;
    using System.IO;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.Runtime;
    using Amazon.S3;
    using Amazon.S3.Model;
    using Amazon.S3.Util;
    using Pneuma.Core.Observability;
    using SyslogLogging;

    /// <summary>
    /// Blob store backed by one bucket of an S3-compatible service (Less3, MinIO, AWS S3). Each blob is an object whose
    /// key is the blob key unchanged (for example <c>inline/{tenantId}/{linkId}</c>), so it can be inspected and backed up
    /// with ordinary S3 tooling. Path-style addressing and an explicit service URL are supported for self-hosted servers.
    /// Thread-safe.
    /// </summary>
    public class S3BlobStore : IBlobStore, IDisposable
    {
        #region Public-Members

        /// <summary>The bucket that holds the blobs.</summary>
        public string Bucket
        {
            get { return _Bucket; }
        }

        #endregion

        #region Private-Members

        private readonly AmazonS3Client _Client;
        private readonly string _Bucket;
        private readonly LoggingModule? _Logging;
        private readonly string _Header = "[S3BlobStore] ";
        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the S3 blob store.</summary>
        /// <param name="endpoint">Service URL of the S3-compatible store, for example <c>http://less3:8000</c>.</param>
        /// <param name="region">Region reported to the S3 API; null or empty for none.</param>
        /// <param name="accessKey">Access key.</param>
        /// <param name="secretKey">Secret key.</param>
        /// <param name="forcePathStyle">Whether to use path-style addressing (required by most self-hosted servers).</param>
        /// <param name="bucket">Bucket that holds the blobs.</param>
        /// <param name="logging">Logging module, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null or empty.</exception>
        public S3BlobStore(string endpoint, string? region, string accessKey, string secretKey, bool forcePathStyle, string bucket, LoggingModule? logging = null)
        {
            if (String.IsNullOrWhiteSpace(endpoint)) throw new ArgumentNullException(nameof(endpoint));
            if (String.IsNullOrWhiteSpace(accessKey)) throw new ArgumentNullException(nameof(accessKey));
            if (String.IsNullOrWhiteSpace(secretKey)) throw new ArgumentNullException(nameof(secretKey));
            if (String.IsNullOrWhiteSpace(bucket)) throw new ArgumentNullException(nameof(bucket));

            _Bucket = bucket;
            _Logging = logging;

            AmazonS3Config config = new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = forcePathStyle,
                AuthenticationRegion = String.IsNullOrWhiteSpace(region) ? null : region
            };

            _Client = new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), config);
        }

        #endregion

        #region Public-Methods

        /// <summary>Create the bucket when it does not exist. Idempotent.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        /// <exception cref="AmazonS3Exception">Thrown when the service refuses the request.</exception>
        public async Task EnsureBucketAsync(CancellationToken token = default)
        {
            bool exists = await AmazonS3Util.DoesS3BucketExistV2Async(_Client, _Bucket).ConfigureAwait(false);
            if (exists)
            {
                _Logging?.Debug(_Header + "bucket '" + _Bucket + "' already exists");
                return;
            }

            await _Client.PutBucketAsync(new PutBucketRequest { BucketName = _Bucket }, token).ConfigureAwait(false);
            _Logging?.Info(_Header + "created bucket '" + _Bucket + "'");
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> or <paramref name="data"/> is null.</exception>
        public async Task<string> WriteAsync(string key, byte[] data, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));
            if (data == null) throw new ArgumentNullException(nameof(data));

            string outcome = "ok";
            try
            {
                try
                {
                    await PutAsync(key, data, token).ConfigureAwait(false);
                }
                catch (AmazonS3Exception e) when (String.Equals(e.ErrorCode, "NoSuchBucket", StringComparison.OrdinalIgnoreCase))
                {
                    // The bucket was not created at startup (the service was unreachable) or was removed: create it and
                    // write once more.
                    await EnsureBucketAsync(token).ConfigureAwait(false);
                    await PutAsync(key, data, token).ConfigureAwait(false);
                }

                return key;
            }
            catch
            {
                outcome = "error";
                throw;
            }
            finally
            {
                PneumaMetrics.RecordIntegration("blob-s3", "PUT", outcome, 0);
            }
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null or empty.</exception>
        public async Task<byte[]?> ReadAsync(string key, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));

            string outcome = "ok";
            try
            {
                GetObjectRequest request = new GetObjectRequest { BucketName = _Bucket, Key = key };
                using (GetObjectResponse response = await _Client.GetObjectAsync(request, token).ConfigureAwait(false))
                using (MemoryStream buffer = new MemoryStream())
                {
                    await response.ResponseStream.CopyToAsync(buffer, token).ConfigureAwait(false);
                    return buffer.ToArray();
                }
            }
            catch (AmazonS3Exception e) when (IsNotFound(e))
            {
                outcome = "not-found";
                return null;
            }
            catch
            {
                outcome = "error";
                throw;
            }
            finally
            {
                PneumaMetrics.RecordIntegration("blob-s3", "GET", outcome, 0);
            }
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null or empty.</exception>
        public async Task DeleteAsync(string key, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));

            string outcome = "ok";
            try
            {
                await _Client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _Bucket, Key = key }, token).ConfigureAwait(false);
            }
            catch (AmazonS3Exception e) when (IsNotFound(e))
            {
                // Already absent: deletion is idempotent.
                outcome = "not-found";
            }
            catch
            {
                outcome = "error";
                throw;
            }
            finally
            {
                PneumaMetrics.RecordIntegration("blob-s3", "DELETE", outcome, 0);
            }
        }

        /// <summary>Dispose the underlying S3 client.</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        /// <summary>Dispose managed resources.</summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) _Client.Dispose();
            _Disposed = true;
        }

        private async Task PutAsync(string key, byte[] data, CancellationToken token)
        {
            using (MemoryStream stream = new MemoryStream(data, false))
            {
                PutObjectRequest request = new PutObjectRequest
                {
                    BucketName = _Bucket,
                    Key = key,
                    InputStream = stream,
                    ContentType = "application/octet-stream",
                    AutoCloseStream = false
                };
                await _Client.PutObjectAsync(request, token).ConfigureAwait(false);
            }
        }

        private static bool IsNotFound(AmazonS3Exception e)
        {
            return e.StatusCode == HttpStatusCode.NotFound
                || String.Equals(e.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase)
                || String.Equals(e.ErrorCode, "NoSuchBucket", StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
