namespace Pneuma.Core.Storage
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Disk-backed BLOB store. Artifacts are written under a configured directory, keyed by a
    /// sanitized filename.
    /// </summary>
    public class DiskBlobStore : IBlobStore
    {
        #region Private-Members

        private readonly string _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the disk blob store.</summary>
        /// <param name="directory">Storage directory (created if missing).</param>
        public DiskBlobStore(string directory)
        {
            if (String.IsNullOrWhiteSpace(directory)) throw new ArgumentNullException(nameof(directory));
            _Directory = directory;
            Directory.CreateDirectory(_Directory);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<string> WriteAsync(string key, byte[] data, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));
            if (data == null) throw new ArgumentNullException(nameof(data));

            string path = Path.Combine(_Directory, Sanitize(key));
            await File.WriteAllBytesAsync(path, data, token).ConfigureAwait(false);
            return key;
        }

        /// <inheritdoc />
        public async Task<byte[]?> ReadAsync(string key, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));

            string path = Path.Combine(_Directory, Sanitize(key));
            if (!File.Exists(path)) return null;
            return await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task DeleteAsync(string key, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));

            string path = Path.Combine(_Directory, Sanitize(key));
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }

        #endregion

        #region Private-Methods

        private static string Sanitize(string key)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                key = key.Replace(invalid, '_');
            }
            return key;
        }

        #endregion
    }
}
