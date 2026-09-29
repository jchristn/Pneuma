namespace Pneuma.Core.Crawling.Crawlers
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Blobject.Core;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations.Implementations;

    /// <summary>
    /// Shared logic for crawlers over Blobject storage (buckets, containers, CIFS and NFS shares, folders): listing under a prefix
    /// (optionally without subfolders), version tokens from the ETag or size and modification time, reading an object
    /// within the download limit, and the settings, DNS, TCP, and listing steps of a connectivity test.
    /// </summary>
    public abstract class BlobCrawlerBase : ICrawler
    {
        #region Public-Members

        /// <inheritdoc />
        public abstract CrawlPlanTypeEnum Type { get; }

        /// <inheritdoc />
        public abstract string DisplayName { get; }

        /// <inheritdoc />
        public abstract string Description { get; }

        /// <summary>True when the storage is reached over the network (the test then checks DNS and TCP). Default true.</summary>
        protected virtual bool UsesNetwork => true;

        /// <summary>
        /// True when signing in is a separate step of the connectivity test (file shares): the test lists the share or
        /// export root first and reports a failure there as the "auth" step. Default false.
        /// </summary>
        protected virtual bool SignInStep => false;

        #endregion

        #region Private-Members

        private readonly long _MaxDownloadBytes;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="maxDownloadBytes">Largest object read for ingestion (the fetch-safety download limit); 0 for no limit.</param>
        protected BlobCrawlerBase(long maxDownloadBytes)
        {
            _MaxDownloadBytes = Math.Max(0L, maxDownloadBytes);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<ConnectivityResult> TestAsync(CrawlPlan plan, CancellationToken token)
        {
            ConnectivityResult result = new ConnectivityResult();
            string? problem = SettingsProblem(plan);
            if (!result.Add("settings", problem == null, problem ?? "Settings are complete.")) return result;

            if (UsesNetwork)
            {
                string host = Host(plan);
                if (!await ConnectivityProbe.ResolveAsync(result, host, token).ConfigureAwait(false)) return result;
                if (!await ConnectivityProbe.ConnectAsync(result, host, Port(plan), 10000, token).ConfigureAwait(false)) return result;
            }

            BlobClientBase? client = null;
            try
            {
                client = CreateClient(plan);
                if (SignInStep)
                {
                    try
                    {
                        await foreach (BlobMetadata item in client.EnumerateAsync(new EnumerationFilter { Prefix = String.Empty }, token).ConfigureAwait(false))
                        {
                            break;
                        }
                        result.Add("auth", true, "Signed in and opened " + Describe(plan) + ".");
                    }
                    catch (Exception e) when (!(e is OperationCanceledException))
                    {
                        result.Add("auth", false, "Opening " + Describe(plan) + " failed: " + e.Message + " Check the credentials and the share or export name.");
                        return result;
                    }
                }
                int listed = 0;
                await foreach (BlobMetadata item in client.EnumerateAsync(new EnumerationFilter { Prefix = Prefix(plan) }, token).ConfigureAwait(false))
                {
                    listed++;
                    if (listed >= 5) break;
                }
                if (!SignInStep) result.Add("auth", true, "Signed in.");
                result.Add("root", true, listed == 0 ? "Listed " + Describe(plan) + "; it is empty." : "Listed " + Describe(plan) + ".");
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                result.Add("root", false, "Listing " + Describe(plan) + " failed: " + e.Message + " Check the credentials and the path.");
            }
            finally
            {
                (client as IDisposable)?.Dispose();
            }
            return result;
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<CrawledObject> EnumerateAsync(CrawlPlan plan, [EnumeratorCancellation] CancellationToken token)
        {
            string? problem = SettingsProblem(plan);
            if (problem != null) throw new ArgumentException(problem);
            string prefix = Prefix(plan);
            bool includeSubfolders = IncludeSubfolders(plan);
            BlobClientBase client = CreateClient(plan);
            try
            {
                await foreach (BlobMetadata item in client.EnumerateAsync(new EnumerationFilter { Prefix = prefix }, token).ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();
                    if (item == null || String.IsNullOrEmpty(item.Key)) continue;
                    if (item.IsFolder) continue;
                    if (IsSkippedName(item.Key)) continue;
                    if (!includeSubfolders && IsNested(item.Key, prefix)) continue;
                    yield return ToObject(plan, item);
                }
            }
            finally
            {
                (client as IDisposable)?.Dispose();
            }
        }

        /// <inheritdoc />
        /// <exception cref="ContentTooLargeException">Thrown when the object exceeds the download limit.</exception>
        public async Task<ResolvedContent> OpenAsync(CrawlPlan plan, string key, CancellationToken token)
        {
            BlobClientBase client = CreateClient(plan);
            try
            {
                if (_MaxDownloadBytes > 0)
                {
                    BlobMetadata? metadata = await client.GetMetadataAsync(key, token).ConfigureAwait(false);
                    if (metadata != null && metadata.ContentLength > _MaxDownloadBytes)
                        throw new ContentTooLargeException(_MaxDownloadBytes, key + " is " + metadata.ContentLength + " bytes; the limit is " + _MaxDownloadBytes + " (Ingestion.FetchSafety.MaxDownloadBytes).");
                }
                byte[] bytes = await client.GetAsync(key, token).ConfigureAwait(false);
                if (_MaxDownloadBytes > 0 && bytes.LongLength > _MaxDownloadBytes)
                    throw new ContentTooLargeException(_MaxDownloadBytes, key + " is " + bytes.LongLength + " bytes; the limit is " + _MaxDownloadBytes + " (Ingestion.FetchSafety.MaxDownloadBytes).");
                return new ResolvedContent { Bytes = bytes };
            }
            finally
            {
                (client as IDisposable)?.Dispose();
            }
        }

        /// <summary>
        /// True for files that are never content: Windows and macOS metadata (Thumbs.db, desktop.ini, .DS_Store,
        /// AppleDouble ._ files), Office lock files (~$), temporary files, and anything under a hidden or system folder
        /// (.snapshot, $RECYCLE.BIN, System Volume Information).
        /// </summary>
        /// <param name="key">The object key.</param>
        /// <returns>True to skip it.</returns>
        public static bool IsSkippedName(string key)
        {
            if (String.IsNullOrEmpty(key)) return true;
            string[] parts = key.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return true;
            string name = parts[parts.Length - 1];
            if (String.Equals(name, "Thumbs.db", StringComparison.OrdinalIgnoreCase)
                || String.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase)
                || String.Equals(name, ".DS_Store", StringComparison.Ordinal)
                || name.StartsWith("._", StringComparison.Ordinal)
                || name.StartsWith("~$", StringComparison.Ordinal)
                || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                return true;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string folder = parts[i];
                if (folder.StartsWith(".", StringComparison.Ordinal)
                    || String.Equals(folder, "$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(folder, "System Volume Information", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>The version token for an object: its ETag, or its size and modification time.</summary>
        /// <param name="item">The object.</param>
        /// <returns>The token, or null when the storage reports neither.</returns>
        public static string? VersionOf(BlobMetadata item)
        {
            if (item == null) return null;
            if (!String.IsNullOrWhiteSpace(item.ETag)) return "etag:" + item.ETag.Trim('"');
            DateTime? modified = item.LastUpdateUtc ?? item.CreatedUtc;
            if (modified == null) return null;
            return "size:" + item.ContentLength.ToString(CultureInfo.InvariantCulture) + ";modified:" + modified.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        #endregion

        #region Private-Methods

        /// <summary>Create a storage client for the plan. The caller disposes it when it is disposable.</summary>
        /// <param name="plan">The plan (secrets decrypted).</param>
        /// <returns>The client.</returns>
        protected abstract BlobClientBase CreateClient(CrawlPlan plan);

        /// <summary>What is missing from the plan's settings, or null when they are complete.</summary>
        /// <param name="plan">The plan.</param>
        /// <returns>The problem, or null.</returns>
        protected abstract string? SettingsProblem(CrawlPlan plan);

        /// <summary>The host the storage lives on (for the DNS and TCP steps).</summary>
        /// <param name="plan">The plan.</param>
        /// <returns>The host.</returns>
        protected abstract string Host(CrawlPlan plan);

        /// <summary>The port the storage listens on.</summary>
        /// <param name="plan">The plan.</param>
        /// <returns>The port.</returns>
        protected abstract int Port(CrawlPlan plan);

        /// <summary>The key prefix to list under ("" for everything).</summary>
        /// <param name="plan">The plan.</param>
        /// <returns>The prefix.</returns>
        protected abstract string Prefix(CrawlPlan plan);

        /// <summary>True to include objects under sub-prefixes or subfolders.</summary>
        /// <param name="plan">The plan.</param>
        /// <returns>True to include them.</returns>
        protected abstract bool IncludeSubfolders(CrawlPlan plan);

        /// <summary>The URI a link carries for an object (for example s3://bucket/key).</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="key">The object key.</param>
        /// <returns>The URI.</returns>
        protected abstract string UriFor(CrawlPlan plan, string key);

        /// <summary>A short description of the location (for test messages).</summary>
        /// <param name="plan">The plan.</param>
        /// <returns>The description.</returns>
        protected abstract string Describe(CrawlPlan plan);

        /// <summary>Normalize a configured folder into a key prefix: forward slashes, no leading slash, a trailing slash.</summary>
        /// <param name="path">The folder, or null.</param>
        /// <returns>The prefix, or "".</returns>
        protected static string FolderPrefix(string? path)
        {
            if (String.IsNullOrWhiteSpace(path)) return String.Empty;
            string p = path.Replace('\\', '/').Trim().Trim('/');
            return p.Length == 0 ? String.Empty : p + "/";
        }

        private CrawledObject ToObject(CrawlPlan plan, BlobMetadata item)
        {
            DateTime? modified = item.LastUpdateUtc ?? item.CreatedUtc;
            return new CrawledObject
            {
                Key = item.Key,
                Uri = UriFor(plan, item.Key),
                Title = CrawlKeys.TitleFor(item.Key),
                ContentType = String.IsNullOrWhiteSpace(item.ContentType) || item.ContentType == "application/octet-stream"
                    ? (CrawlContentTypes.FromName(item.Key) ?? item.ContentType)
                    : item.ContentType,
                SizeBytes = Math.Max(0L, item.ContentLength),
                VersionToken = VersionOf(item),
                ModifiedUtc = modified?.ToUniversalTime()
            };
        }

        private static bool IsNested(string key, string prefix)
        {
            string rest = key.Replace('\\', '/');
            if (!String.IsNullOrEmpty(prefix) && rest.StartsWith(prefix, StringComparison.Ordinal)) rest = rest.Substring(prefix.Length);
            return rest.Trim('/').Contains('/');
        }

        #endregion
    }
}
