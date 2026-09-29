namespace Pneuma.Core.Crawling
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Models;

    /// <summary>
    /// A connector to one kind of source. Every method receives the plan with its secrets decrypted; a crawler never
    /// stores state of its own, so the same instance serves every plan of its type.
    /// </summary>
    public interface ICrawler
    {
        /// <summary>The plan type this crawler serves.</summary>
        CrawlPlanTypeEnum Type { get; }

        /// <summary>Display name for the type catalog.</summary>
        string DisplayName { get; }

        /// <summary>What the crawler does and how it detects changes, for the type catalog.</summary>
        string Description { get; }

        /// <summary>
        /// Problems with the plan's settings that the settings attributes cannot express (for example a folder outside
        /// the allowed roots). Checked when a plan is saved. The default finds none.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <returns>The problems; empty when the settings are acceptable.</returns>
        List<string> CheckSettings(CrawlPlan plan) => new List<string>();

        /// <summary>
        /// Test the plan's connection step by step (DNS, TCP, authentication, root access), stopping at the first
        /// failure. Never throws for a connection problem; the failure is a step in the result.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The steps.</returns>
        Task<ConnectivityResult> TestAsync(CrawlPlan plan, CancellationToken token);

        /// <summary>List the source's objects. Throws when the source cannot be listed, so a failed listing never looks empty.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The objects.</returns>
        IAsyncEnumerable<CrawledObject> EnumerateAsync(CrawlPlan plan, CancellationToken token);

        /// <summary>Read one object's content for ingestion.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="key">The object's key, as enumerated.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The content.</returns>
        Task<ResolvedContent> OpenAsync(CrawlPlan plan, string key, CancellationToken token);
    }
}
