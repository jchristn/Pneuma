namespace Pneuma.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ontologies;

    /// <summary>Ontology violation data access. Every method is scoped to a tenant.</summary>
    public interface IOntologyViolationMethods
    {
        /// <summary>Create violations in one transaction.</summary>
        /// <param name="violations">The violations.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="violations"/> is null.</exception>
        Task CreateManyAsync(List<OntologyViolation> violations, CancellationToken token = default);

        /// <summary>Read a violation.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Violation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The violation, or null.</returns>
        Task<OntologyViolation?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Enumerate a subject's violations, newest first, optionally filtered.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="status">Only this status, or null for all.</param>
        /// <param name="jobId">Only this job's, or null.</param>
        /// <param name="operationId">Only this operation's, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The violations.</returns>
        Task<List<OntologyViolation>> EnumerateAsync(string tenantId, string subjectId, OntologyViolationStatusEnum? status, string? jobId, string? operationId, CancellationToken token = default);

        /// <summary>Update a violation's review fields (status, resolver, and time).</summary>
        /// <param name="violation">The violation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="violation"/> is null.</exception>
        Task UpdateStatusAsync(OntologyViolation violation, CancellationToken token = default);

        /// <summary>Delete a subject's findings from earlier validation operations that are not under review (quarantined findings stay).</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteValidationFindingsAsync(string tenantId, string subjectId, CancellationToken token = default);
    }
}
