namespace Pneuma.Core.Integrations.Abstractions
{
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Models;

    /// <summary>Retrieves a link's content for ingestion according to where it comes from (its source kind).</summary>
    public interface IContentResolver
    {
        /// <summary>Retrieve a link's content.</summary>
        /// <param name="link">The link, or null for a job whose link row is gone (its URL is fetched).</param>
        /// <param name="sourceUrl">The job's source URL.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The content.</returns>
        Task<ResolvedContent> ResolveAsync(SubjectLink? link, string sourceUrl, CancellationToken token);
    }
}
