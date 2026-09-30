namespace Pneuma.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Ontologies;

    /// <summary>
    /// Ontology version data access. A version is read with its node types, edge types, rules, and concepts; listings
    /// return headers with counts only. Every method is scoped to a tenant.
    /// </summary>
    public interface IOntologyVersionMethods
    {
        /// <summary>Create a version with its contents.</summary>
        /// <param name="version">The version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created version.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="version"/> is null.</exception>
        Task<OntologyVersion> CreateAsync(OntologyVersion version, CancellationToken token = default);

        /// <summary>Read a version with its contents.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Version identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The version, or null when it does not exist in the tenant.</returns>
        Task<OntologyVersion?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Enumerate an ontology's versions (headers with counts), newest first.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="ontologyId">Ontology identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The versions.</returns>
        Task<List<OntologyVersion>> EnumerateAsync(string tenantId, string ontologyId, CancellationToken token = default);

        /// <summary>The next free version number of an ontology.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="ontologyId">Ontology identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One more than the highest version number, or 1.</returns>
        Task<int> NextVersionNumberAsync(string tenantId, string ontologyId, CancellationToken token = default);

        /// <summary>Replace a version's header fields and its contents.</summary>
        /// <param name="version">The version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated version.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="version"/> is null.</exception>
        Task<OntologyVersion> UpdateAsync(OntologyVersion version, CancellationToken token = default);

        /// <summary>Update a version's lifecycle fields only (status, change summary, approval, and retirement).</summary>
        /// <param name="version">The version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="version"/> is null.</exception>
        Task UpdateStatusAsync(OntologyVersion version, CancellationToken token = default);

        /// <summary>Delete a version and its contents.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Version identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the version existed.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);
    }
}
