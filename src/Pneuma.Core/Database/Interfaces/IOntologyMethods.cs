namespace Pneuma.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Ontologies;

    /// <summary>Tenant ontology data access. Every method is scoped to a tenant.</summary>
    public interface IOntologyMethods
    {
        /// <summary>Create an ontology.</summary>
        /// <param name="ontology">The ontology.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created ontology.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="ontology"/> is null.</exception>
        Task<TenantOntology> CreateAsync(TenantOntology ontology, CancellationToken token = default);

        /// <summary>Read an ontology.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Ontology identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The ontology, or null when it does not exist in the tenant.</returns>
        Task<TenantOntology?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Read an ontology by name.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="name">Ontology name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The ontology, or null.</returns>
        Task<TenantOntology?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default);

        /// <summary>Enumerate a tenant's ontologies, by name.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The ontologies.</returns>
        Task<List<TenantOntology>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>Update an ontology's name and description.</summary>
        /// <param name="ontology">The ontology.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated ontology.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="ontology"/> is null.</exception>
        Task<TenantOntology> UpdateAsync(TenantOntology ontology, CancellationToken token = default);

        /// <summary>Delete an ontology with all of its versions and their contents.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Ontology identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the ontology existed.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);
    }
}
