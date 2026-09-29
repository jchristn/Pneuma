namespace Pneuma.Core.Integrations.Implementations
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Ingestion.Stages;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Models;
    using Pneuma.Core.Storage;

    /// <summary>
    /// Retrieves a link's content by its source kind: a URL through the content fetcher (and so the fetch-safety policy),
    /// pushed content from the blob store with its declared type, and a crawled object through its crawl plan's crawler.
    /// </summary>
    public class ContentResolver : IContentResolver
    {
        #region Public-Members

        /// <summary>
        /// Opens content for links crawl plans created. Null until the crawl framework is wired; a crawled link then fails
        /// with a configuration error.
        /// </summary>
        public ICrawlContentSource? CrawlSource { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly IContentFetcher _Fetcher;
        private readonly IBlobStore _Blobs;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the resolver.</summary>
        /// <param name="fetcher">Fetcher for URL links.</param>
        /// <param name="blobs">Blob store holding pushed content.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public ContentResolver(IContentFetcher fetcher, IBlobStore blobs)
        {
            _Fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
            _Blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        /// <exception cref="IngestionHardFailException">Thrown when pushed content is missing or a crawled link has no crawl source.</exception>
        public async Task<ResolvedContent> ResolveAsync(SubjectLink? link, string sourceUrl, CancellationToken token)
        {
            SourceKindEnum kind = link?.SourceKind ?? SourceKindEnum.Url;
            switch (kind)
            {
                case SourceKindEnum.Inline:
                    byte[]? stored = await _Blobs.ReadAsync(InlineContentKeys.KeyFor(link!.TenantId, link.Id), token).ConfigureAwait(false);
                    if (stored == null) throw new IngestionHardFailException(IngestionStageEnum.ContentRetrieval, IngestionFailureCategoryEnum.NoContent, "The pushed content for link " + link.Id + " is missing from the blob store.");
                    return new ResolvedContent { Bytes = stored, DeclaredDocumentType = InlineContentKeys.DocumentTypeFor(link.ContentType) };

                case SourceKindEnum.Crawl:
                    if (CrawlSource == null) throw new IngestionHardFailException(IngestionStageEnum.ContentRetrieval, IngestionFailureCategoryEnum.Configuration, "Link " + link!.Id + " came from a crawl plan, but no crawl content source is configured.");
                    return await CrawlSource.OpenAsync(link!, token).ConfigureAwait(false);

                default:
                    byte[] fetched = await _Fetcher.FetchAsync(sourceUrl, token).ConfigureAwait(false);
                    return new ResolvedContent { Bytes = fetched };
            }
        }

        #endregion
    }
}
