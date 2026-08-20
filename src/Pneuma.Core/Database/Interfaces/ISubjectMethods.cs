namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Subject archive data access methods.
    /// </summary>
    public interface ISubjectMethods
    {
        /// <summary>Create a subject.</summary>
        /// <param name="subject">Subject to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created subject.</returns>
        Task<Subject> CreateAsync(Subject subject, CancellationToken token = default);

        /// <summary>Read a subject by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Subject, or null if not found.</returns>
        Task<Subject?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Read a subject by identifier without tenant scoping.</summary>
        /// <param name="id">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Subject, or null if not found.</returns>
        Task<Subject?> ReadByIdAsync(string id, CancellationToken token = default);

        /// <summary>Read a subject by its URL slug within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="slug">URL slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Subject, or null if not found.</returns>
        Task<Subject?> ReadBySlugAsync(string tenantId, string slug, CancellationToken token = default);

        /// <summary>Enumerate subjects within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Subjects.</returns>
        Task<List<Subject>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>Enumerate, across all tenants, subjects whose cascade deletion is pending or in progress
        /// (so an interrupted deletion can be resumed). Used by the background deletion worker.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Subjects awaiting or undergoing deletion.</returns>
        Task<List<Subject>> EnumeratePendingDeletionAsync(CancellationToken token = default);

        /// <summary>Update a subject.</summary>
        /// <param name="subject">Subject to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated subject.</returns>
        Task<Subject> UpdateAsync(Subject subject, CancellationToken token = default);

        /// <summary>Delete a subject by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Atomically delete a subject together with all of its links, ingestion jobs, and job events in a
        /// single transaction, so a cascade never leaves subordinate rows orphaned by a partial failure.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="linkIds">Identifiers of the links to delete with the subject.</param>
        /// <param name="jobIds">Identifiers of the jobs (and their events) to delete with the subject.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the subject row existed and was deleted.</returns>
        Task<bool> DeleteWithSubordinatesAsync(string tenantId, string subjectId, IEnumerable<string> linkIds, IEnumerable<string> jobIds, CancellationToken token = default);
    }
}
