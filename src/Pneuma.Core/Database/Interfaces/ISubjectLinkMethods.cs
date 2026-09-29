namespace Pneuma.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ingestion.Models;

    /// <summary>
    /// Subject link data access methods.
    /// </summary>
    public interface ISubjectLinkMethods
    {
        /// <summary>Create a link.</summary>
        /// <param name="link">Link to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created link.</returns>
        Task<SubjectLink> CreateAsync(SubjectLink link, CancellationToken token = default);

        /// <summary>Read a link by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Link identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Link, or null if not found.</returns>
        Task<SubjectLink?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Enumerate links within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Links.</returns>
        Task<List<SubjectLink>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>Enumerate links for a subject within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Links.</returns>
        Task<List<SubjectLink>> EnumerateBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default);

        /// <summary>
        /// Enumerate, across all tenants, links whose deletion status is Pending or Deleting, so the background
        /// deletion worker can claim them (and resume interrupted deletions).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Links awaiting or undergoing background cascade deletion.</returns>
        Task<List<SubjectLink>> EnumeratePendingDeletionAsync(CancellationToken token = default);

        /// <summary>Update a link.</summary>
        /// <param name="link">Link to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated link.</returns>
        Task<SubjectLink> UpdateAsync(SubjectLink link, CancellationToken token = default);

        /// <summary>Read a subject's link by its external key (pushed content upserts by key).</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="externalKey">External key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The link, or null.</returns>
        Task<SubjectLink?> ReadByExternalKeyAsync(string tenantId, string subjectId, string externalKey, CancellationToken token = default);

        /// <summary>Enumerate the links a crawl plan created.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="crawlPlanId">Crawl plan identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The links.</returns>
        Task<List<SubjectLink>> EnumerateByCrawlPlanAsync(string tenantId, string crawlPlanId, CancellationToken token = default);

        /// <summary>Delete a link by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Link identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Atomically create a link together with its initial ingestion job in a single transaction, so a
        /// submitted link never persists without the job that ingests it (nor the reverse).
        /// </summary>
        /// <param name="link">Link to create.</param>
        /// <param name="job">Ingestion job to create alongside the link.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created link.</returns>
        Task<SubjectLink> CreateWithJobAsync(SubjectLink link, IngestionJob job, CancellationToken token = default);

        /// <summary>
        /// Atomically delete a link together with the ingestion jobs and job events it owns in a single
        /// transaction, so a cascade never leaves the database in a partially-deleted state.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="linkId">Link identifier.</param>
        /// <param name="jobIds">Identifiers of the jobs (and their events) to delete with the link.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the link row existed and was deleted.</returns>
        Task<bool> DeleteWithJobsAsync(string tenantId, string linkId, IEnumerable<string> jobIds, CancellationToken token = default);
            /// <summary>
        /// Enumerate links in every tenant whose scheduled refresh is due: active, not being deleted, URL links that no
        /// crawl plan owns, with <c>NextRefreshUtc</c> at or before <paramref name="nowUtc"/>, oldest first.
        /// </summary>
        /// <param name="nowUtc">The current time.</param>
        /// <param name="maxResults">Most links to return.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The due links.</returns>
        Task<List<SubjectLink>> EnumerateDueForRefreshAsync(DateTime nowUtc, int maxResults, CancellationToken token = default);

        /// <summary>
        /// Claim a due link for a refresh check by moving its next refresh to <paramref name="claimUntilUtc"/> only
        /// when it still equals <paramref name="expectedNextUtc"/>. Exactly one of several concurrent callers succeeds.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Link identifier.</param>
        /// <param name="expectedNextUtc">The next refresh the caller read.</param>
        /// <param name="claimUntilUtc">When the claim lapses if the check never finishes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the caller holds the claim.</returns>
        Task<bool> TryClaimRefreshAsync(string tenantId, string id, DateTime? expectedNextUtc, DateTime claimUntilUtc, CancellationToken token = default);

        /// <summary>Update only a link's refresh columns (interval, next and last refresh, failures, source ETag and Last-Modified).</summary>
        /// <param name="link">The link.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="link"/> is null.</exception>
        Task UpdateRefreshStateAsync(SubjectLink link, CancellationToken token = default);

        /// <summary>Set the next refresh of a subject's URL links that use the subject's default interval.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="nextRefreshUtc">The next refresh, or null to stop refreshing them.</param>
        /// <param name="token">Cancellation token.</param>
        Task SetInheritedNextRefreshAsync(string tenantId, string subjectId, DateTime? nextRefreshUtc, CancellationToken token = default);
    }
}
