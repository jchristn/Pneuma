namespace Pneuma.Core.Storage
{
    using System;
    using System.IO;
    using System.Net;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.Runtime;
    using Amazon.S3;
    using Amazon.S3.Model;
    using Amazon.S3.Util;
    using Pneuma.Core.Observability;
    using SyslogLogging;

    /// <summary>
    /// S3-compatible artifact store (Less3) backed by <see cref="AmazonS3Client"/>. Each pipeline
    /// artifact is written to a dedicated bucket under a key derived from the originating link id, and
    /// can be read back for the artifact-view endpoints. Path-style addressing and an explicit service
    /// URL are used so the client can target Less3 / MinIO-style servers.
    /// </summary>
    public class S3ArtifactStore : IArtifactStore, IDisposable
    {
        #region Private-Members

        private readonly AmazonS3Client _Client;
        private readonly string _SourceBucket;
        private readonly string _AtomsBucket;
        private readonly string _ChunksBucket;
        private readonly string _EmbeddingsBucket;
        private readonly string _SubgraphBucket;
        private readonly LoggingModule _Logging;
        private readonly string _Header = "[S3ArtifactStore] ";
        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the S3 artifact store.</summary>
        /// <param name="endpoint">Service endpoint URL of the S3-compatible store.</param>
        /// <param name="region">Region string reported to the S3 API.</param>
        /// <param name="accessKey">Access key.</param>
        /// <param name="secretKey">Secret key.</param>
        /// <param name="forcePathStyle">Whether to use path-style addressing.</param>
        /// <param name="sourceBucket">Bucket for the raw source asset.</param>
        /// <param name="atomsBucket">Bucket for atomized documents.</param>
        /// <param name="chunksBucket">Bucket for chunked documents.</param>
        /// <param name="embeddingsBucket">Bucket for embedding vectors.</param>
        /// <param name="subgraphBucket">Bucket for candidate-subgraph JSON.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null or empty.</exception>
        public S3ArtifactStore(
            string endpoint,
            string region,
            string accessKey,
            string secretKey,
            bool forcePathStyle,
            string sourceBucket,
            string atomsBucket,
            string chunksBucket,
            string embeddingsBucket,
            string subgraphBucket,
            LoggingModule logging)
        {
            if (String.IsNullOrWhiteSpace(endpoint)) throw new ArgumentNullException(nameof(endpoint));
            if (String.IsNullOrWhiteSpace(accessKey)) throw new ArgumentNullException(nameof(accessKey));
            if (String.IsNullOrWhiteSpace(secretKey)) throw new ArgumentNullException(nameof(secretKey));
            if (String.IsNullOrWhiteSpace(sourceBucket)) throw new ArgumentNullException(nameof(sourceBucket));
            if (String.IsNullOrWhiteSpace(atomsBucket)) throw new ArgumentNullException(nameof(atomsBucket));
            if (String.IsNullOrWhiteSpace(chunksBucket)) throw new ArgumentNullException(nameof(chunksBucket));
            if (String.IsNullOrWhiteSpace(embeddingsBucket)) throw new ArgumentNullException(nameof(embeddingsBucket));
            if (String.IsNullOrWhiteSpace(subgraphBucket)) throw new ArgumentNullException(nameof(subgraphBucket));

            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _SourceBucket = sourceBucket;
            _AtomsBucket = atomsBucket;
            _ChunksBucket = chunksBucket;
            _EmbeddingsBucket = embeddingsBucket;
            _SubgraphBucket = subgraphBucket;

            AmazonS3Config config = new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = forcePathStyle,
                AuthenticationRegion = String.IsNullOrWhiteSpace(region) ? null : region
            };

            BasicAWSCredentials credentials = new BasicAWSCredentials(accessKey, secretKey);
            _Client = new AmazonS3Client(credentials, config);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task EnsureBucketsAsync(CancellationToken token = default)
        {
            await EnsureBucketAsync(_SourceBucket, token).ConfigureAwait(false);
            await EnsureBucketAsync(_AtomsBucket, token).ConfigureAwait(false);
            await EnsureBucketAsync(_ChunksBucket, token).ConfigureAwait(false);
            await EnsureBucketAsync(_EmbeddingsBucket, token).ConfigureAwait(false);
            await EnsureBucketAsync(_SubgraphBucket, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task PutSourceAsync(string linkId, byte[] data, string? contentType, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(linkId)) throw new ArgumentNullException(nameof(linkId));
            if (data == null) throw new ArgumentNullException(nameof(data));
            await PutAsync(_SourceBucket, linkId, data, String.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType!, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task PutAtomsAsync(string linkId, string json, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(linkId)) throw new ArgumentNullException(nameof(linkId));
            if (json == null) throw new ArgumentNullException(nameof(json));
            await PutAsync(_AtomsBucket, linkId, Encoding.UTF8.GetBytes(json), "application/json", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task PutChunksAsync(string linkId, string json, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(linkId)) throw new ArgumentNullException(nameof(linkId));
            if (json == null) throw new ArgumentNullException(nameof(json));
            await PutAsync(_ChunksBucket, linkId, Encoding.UTF8.GetBytes(json), "application/json", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task PutEmbeddingsAsync(string linkId, string json, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(linkId)) throw new ArgumentNullException(nameof(linkId));
            if (json == null) throw new ArgumentNullException(nameof(json));
            await PutAsync(_EmbeddingsBucket, linkId, Encoding.UTF8.GetBytes(json), "application/json", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task PutSubgraphAsync(string linkId, string json, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(linkId)) throw new ArgumentNullException(nameof(linkId));
            if (json == null) throw new ArgumentNullException(nameof(json));
            await PutAsync(_SubgraphBucket, linkId, Encoding.UTF8.GetBytes(json), "application/json", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<S3ArtifactResult?> GetSourceAsync(string linkId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(linkId)) throw new ArgumentNullException(nameof(linkId));
            return await GetAsync(_SourceBucket, linkId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<S3ArtifactResult?> GetAtomsAsync(string linkId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(linkId)) throw new ArgumentNullException(nameof(linkId));
            return await GetAsync(_AtomsBucket, linkId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<S3ArtifactResult?> GetChunksAsync(string linkId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(linkId)) throw new ArgumentNullException(nameof(linkId));
            return await GetAsync(_ChunksBucket, linkId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<S3ArtifactResult?> GetEmbeddingsAsync(string linkId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(linkId)) throw new ArgumentNullException(nameof(linkId));
            return await GetAsync(_EmbeddingsBucket, linkId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<S3ArtifactResult?> GetSubgraphAsync(string linkId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(linkId)) throw new ArgumentNullException(nameof(linkId));
            return await GetAsync(_SubgraphBucket, linkId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAllForLinkAsync(string linkId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(linkId)) throw new ArgumentNullException(nameof(linkId));
            await DeleteAsync(_SourceBucket, linkId, token).ConfigureAwait(false);
            await DeleteAsync(_AtomsBucket, linkId, token).ConfigureAwait(false);
            await DeleteAsync(_ChunksBucket, linkId, token).ConfigureAwait(false);
            await DeleteAsync(_EmbeddingsBucket, linkId, token).ConfigureAwait(false);
            await DeleteAsync(_SubgraphBucket, linkId, token).ConfigureAwait(false);
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
        /// <param name="disposing">Whether managed resources should be released.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                _Client.Dispose();
            }
            _Disposed = true;
        }

        private async Task EnsureBucketAsync(string bucket, CancellationToken token)
        {
            bool exists = await AmazonS3Util.DoesS3BucketExistV2Async(_Client, bucket).ConfigureAwait(false);
            if (exists)
            {
                _Logging.Debug(_Header + "bucket '" + bucket + "' already exists");
                return;
            }

            PutBucketRequest request = new PutBucketRequest
            {
                BucketName = bucket
            };
            await _Client.PutBucketAsync(request, token).ConfigureAwait(false);
            _Logging.Info(_Header + "created bucket '" + bucket + "'");
        }

        private async Task PutAsync(string bucket, string key, byte[] bytes, string contentType, CancellationToken token)
        {
            string outcome = "ok";
            try
            {
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    PutObjectRequest request = new PutObjectRequest
                    {
                        BucketName = bucket,
                        Key = key,
                        InputStream = stream,
                        ContentType = contentType,
                        AutoCloseStream = false
                    };
                    await _Client.PutObjectAsync(request, token).ConfigureAwait(false);
                }
            }
            catch
            {
                outcome = "error";
                throw;
            }
            finally
            {
                PneumaMetrics.RecordIntegration("less3", "PUT " + bucket, outcome, 0);
            }
        }

        private async Task<S3ArtifactResult?> GetAsync(string bucket, string key, CancellationToken token)
        {
            string outcome = "ok";
            try
            {
                GetObjectRequest request = new GetObjectRequest
                {
                    BucketName = bucket,
                    Key = key
                };

                using (GetObjectResponse response = await _Client.GetObjectAsync(request, token).ConfigureAwait(false))
                using (MemoryStream buffer = new MemoryStream())
                {
                    await response.ResponseStream.CopyToAsync(buffer, token).ConfigureAwait(false);
                    string contentType = response.Headers.ContentType ?? "application/octet-stream";
                    return new S3ArtifactResult(buffer.ToArray(), contentType);
                }
            }
            catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound || String.Equals(e.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase) || String.Equals(e.ErrorCode, "NoSuchBucket", StringComparison.OrdinalIgnoreCase))
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
                PneumaMetrics.RecordIntegration("less3", "GET " + bucket, outcome, 0);
            }
        }

        private async Task DeleteAsync(string bucket, string key, CancellationToken token)
        {
            string outcome = "ok";
            try
            {
                DeleteObjectRequest request = new DeleteObjectRequest
                {
                    BucketName = bucket,
                    Key = key
                };
                await _Client.DeleteObjectAsync(request, token).ConfigureAwait(false);
            }
            catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound || String.Equals(e.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase) || String.Equals(e.ErrorCode, "NoSuchBucket", StringComparison.OrdinalIgnoreCase))
            {
                // Already absent — deletion is idempotent.
                outcome = "not-found";
            }
            catch
            {
                outcome = "error";
                throw;
            }
            finally
            {
                PneumaMetrics.RecordIntegration("less3", "DELETE " + bucket, outcome, 0);
            }
        }

        #endregion
    }
}
