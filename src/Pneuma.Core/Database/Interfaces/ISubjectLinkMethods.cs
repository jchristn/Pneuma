namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

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

        /// <summary>Update a link.</summary>
        /// <param name="link">Link to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated link.</returns>
        Task<SubjectLink> UpdateAsync(SubjectLink link, CancellationToken token = default);

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
    }
}
