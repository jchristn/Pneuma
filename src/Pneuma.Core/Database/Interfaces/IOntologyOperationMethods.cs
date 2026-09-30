namespace Pneuma.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ontologies;

    /// <summary>
    /// Ontology operation data access. Every method is scoped to a tenant except the worker's cross-tenant claim and
    /// startup recovery.
    /// </summary>
    public interface IOntologyOperationMethods
    {
        /// <summary>Create (queue) an operation.</summary>
        /// <param name="operation">The operation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is null.</exception>
        Task<OntologyOperation> CreateAsync(OntologyOperation operation, CancellationToken token = default);

        /// <summary>Read an operation.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Operation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The operation, or null.</returns>
        Task<OntologyOperation?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Enumerate a subject's operations, newest first.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The operations.</returns>
        Task<List<OntologyOperation>> EnumerateAsync(string tenantId, string subjectId, CancellationToken token = default);

        /// <summary>Whether a subject has a queued or running operation of a kind.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="kind">Operation kind.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when one is queued or running.</returns>
        Task<bool> ExistsActiveAsync(string tenantId, string subjectId, OntologyOperationKindEnum kind, CancellationToken token = default);

        /// <summary>
        /// Claim the oldest queued operation in any tenant: mark it running with the claim token, then read the token
        /// back so two workers never process the same operation.
        /// </summary>
        /// <param name="claimToken">This worker's claim token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The claimed operation, or null when none is queued or another worker won.</returns>
        Task<OntologyOperation?> ClaimNextQueuedAsync(string claimToken, CancellationToken token = default);

        /// <summary>Update an operation's status, progress, and results.</summary>
        /// <param name="operation">The operation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is null.</exception>
        Task UpdateAsync(OntologyOperation operation, CancellationToken token = default);

        /// <summary>Fail every running operation (used at startup, when no worker can still be processing them).</summary>
        /// <param name="error">The error to record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>How many operations were failed.</returns>
        Task<int> FailRunningAsync(string error, CancellationToken token = default);

        /// <summary>Add items to an operation.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="items">The items.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="items"/> is null.</exception>
        Task AddItemsAsync(string tenantId, List<OntologyOperationItem> items, CancellationToken token = default);

        /// <summary>Enumerate an operation's items, in order.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="operationId">Operation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The items.</returns>
        Task<List<OntologyOperationItem>> EnumerateItemsAsync(string tenantId, string operationId, CancellationToken token = default);
    }
}
