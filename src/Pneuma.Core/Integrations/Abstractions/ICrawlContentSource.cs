namespace Pneuma.Core.Integrations.Abstractions
{
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Models;

    /// <summary>Opens the content of a link a crawl plan created, through the plan's crawler.</summary>
    public interface ICrawlContentSource
    {
        /// <summary>Open a crawled link's content.</summary>
        /// <param name="link">The link (its crawl plan id and external key identify the object).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The content.</returns>
        Task<ResolvedContent> OpenAsync(SubjectLink link, CancellationToken token);
    }
}
