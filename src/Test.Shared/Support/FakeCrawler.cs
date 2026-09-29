namespace Test.Shared.Support
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Crawling;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Ingestion.Stages;

    /// <summary>
    /// An in-memory source for crawl framework tests: objects are set by key with their content and version token,
    /// and the crawler serves them as the configured plan type. Enumeration can be made to fail or to wait until
    /// released, so tests can observe a running operation.
    /// </summary>
    public sealed class FakeCrawler : ICrawler
    {
        #region Public-Members

        /// <inheritdoc />
        public CrawlPlanTypeEnum Type { get; }

        /// <inheritdoc />
        public string DisplayName => "Fake " + Type;

        /// <inheritdoc />
        public string Description => "In-memory test source.";

        /// <summary>Keys whose content cannot be opened (the ingestion job fails).</summary>
        public ConcurrentDictionary<string, bool> FailOpen { get; } = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>When set, enumeration throws this message.</summary>
        public string? FailEnumeration { get; set; } = null;

        /// <summary>When set, enumeration waits for this gate before returning its objects.</summary>
        public TaskCompletionSource<bool>? Gate { get; set; } = null;

        /// <summary>Signalled when an enumeration starts.</summary>
        public TaskCompletionSource<bool> EnumerationStarted { get; private set; } = NewSignal();

        /// <summary>How many times objects were opened, by key.</summary>
        public ConcurrentDictionary<string, int> Opens { get; } = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The last plan the crawler saw (to check secrets were decrypted).</summary>
        public CrawlPlan? LastPlan { get; private set; } = null;

        #endregion

        #region Private-Members

        private readonly ConcurrentDictionary<string, FakeCrawlerObject> _Objects = new ConcurrentDictionary<string, FakeCrawlerObject>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate a fake crawler for a plan type.</summary>
        /// <param name="type">The plan type it serves.</param>
        public FakeCrawler(CrawlPlanTypeEnum type = CrawlPlanTypeEnum.Web)
        {
            Type = type;
        }

        #endregion

        #region Public-Methods

        /// <summary>Add or change an object.</summary>
        /// <param name="key">Key.</param>
        /// <param name="content">Text content.</param>
        /// <param name="version">Version token, or null for none.</param>
        /// <param name="contentType">Content type.</param>
        public void Set(string key, string content, string? version, string contentType = "text/plain")
        {
            _Objects[key] = new FakeCrawlerObject { Content = content, Version = version, ContentType = contentType };
        }

        /// <summary>Remove an object.</summary>
        /// <param name="key">Key.</param>
        public void Remove(string key)
        {
            FakeCrawlerObject? removed;
            _Objects.TryRemove(key, out removed);
        }

        /// <summary>Reset the enumeration-started signal.</summary>
        public void ResetSignal()
        {
            EnumerationStarted = NewSignal();
        }

        /// <inheritdoc />
        public Task<ConnectivityResult> TestAsync(CrawlPlan plan, CancellationToken token)
        {
            LastPlan = plan;
            ConnectivityResult result = new ConnectivityResult();
            if (result.Add("settings", true, "Settings are complete.") && result.Add("root", FailEnumeration == null, FailEnumeration ?? "Listed the source."))
            {
                result.Add("auth", true, "Signed in.");
            }
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<CrawledObject> EnumerateAsync(CrawlPlan plan, [EnumeratorCancellation] CancellationToken token)
        {
            LastPlan = plan;
            EnumerationStarted.TrySetResult(true);
            if (Gate != null) await Gate.Task.WaitAsync(token).ConfigureAwait(false);
            if (FailEnumeration != null) throw new InvalidOperationException(FailEnumeration);
            foreach (KeyValuePair<string, FakeCrawlerObject> entry in _Objects.OrderBy(e => e.Key, StringComparer.Ordinal).ToList())
            {
                token.ThrowIfCancellationRequested();
                yield return new CrawledObject
                {
                    Key = entry.Key,
                    Uri = "https://fake.example/" + entry.Key,
                    ContentType = entry.Value.ContentType,
                    SizeBytes = Encoding.UTF8.GetByteCount(entry.Value.Content),
                    VersionToken = entry.Value.Version
                };
            }
        }

        /// <inheritdoc />
        public Task<ResolvedContent> OpenAsync(CrawlPlan plan, string key, CancellationToken token)
        {
            LastPlan = plan;
            Opens.AddOrUpdate(key, 1, (k, n) => n + 1);
            if (FailOpen.ContainsKey(key)) throw new IngestionHardFailException(IngestionStageEnum.ContentRetrieval, IngestionFailureCategoryEnum.NoContent, "Test: " + key + " cannot be opened.");
            FakeCrawlerObject? obj;
            if (!_Objects.TryGetValue(key, out obj)) throw new InvalidOperationException("No object " + key);
            return Task.FromResult(new ResolvedContent { Bytes = Encoding.UTF8.GetBytes(obj.Content) });
        }

        #endregion

        #region Private-Methods

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        #endregion
    }
}
