namespace Pneuma.Core.Crawling
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Ingestion.Stages;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Models;

    /// <summary>Opens a crawled link's content through the crawler of the plan that created it.</summary>
    public class CrawlContentSource : ICrawlContentSource
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly CrawlPlanService _Plans;
        private readonly CrawlerFactory _Crawlers;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the source.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="plans">Plan service (reads plans with their secrets).</param>
        /// <param name="crawlers">Registered crawlers.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public CrawlContentSource(DatabaseDriverBase db, CrawlPlanService plans, CrawlerFactory crawlers)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Plans = plans ?? throw new ArgumentNullException(nameof(plans));
            _Crawlers = crawlers ?? throw new ArgumentNullException(nameof(crawlers));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="link"/> is null.</exception>
        /// <exception cref="IngestionHardFailException">Thrown when the link's plan or object no longer exists, or no crawler serves its type.</exception>
        public async Task<ResolvedContent> OpenAsync(SubjectLink link, CancellationToken token)
        {
            if (link == null) throw new ArgumentNullException(nameof(link));
            if (String.IsNullOrEmpty(link.CrawlPlanId))
                throw Fail("Link " + link.Id + " is marked as crawled but has no crawl plan.");

            CrawlObject? obj = await _Db.CrawlObjects.ReadByLinkAsync(link.TenantId, link.Id, token).ConfigureAwait(false);
            if (obj == null) throw Fail("Link " + link.Id + " is no longer tracked by crawl plan " + link.CrawlPlanId + ".");

            CrawlPlan? plan = await _Plans.ReadWithSecretsAsync(link.TenantId, link.CrawlPlanId!, token).ConfigureAwait(false);
            if (plan == null) throw Fail("Crawl plan " + link.CrawlPlanId + " no longer exists.");
            if (!_Crawlers.IsAvailable(plan.Type)) throw Fail("No crawler is available for " + plan.Type + " plans on this server.");

            return await _Crawlers.Get(plan.Type).OpenAsync(plan, obj.ExternalKey, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static IngestionHardFailException Fail(string message)
        {
            return new IngestionHardFailException(IngestionStageEnum.ContentRetrieval, IngestionFailureCategoryEnum.Configuration, message);
        }

        #endregion
    }
}
