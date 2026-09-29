namespace Pneuma.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Crawling;
    using Pneuma.Core.Enums;

    /// <summary>
    /// Crawl plan data access. Plans are read with their settings rows applied and the names of their stored secrets
    /// in <see cref="CrawlPlan.SecretsSet"/>; secret values are only ever read through <see cref="ReadSecretsAsync"/>.
    /// Every method is scoped to a tenant except the scheduler's cross-tenant reads.
    /// </summary>
    public interface ICrawlPlanMethods
    {
        /// <summary>Create a plan and its settings rows.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created plan.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="plan"/> is null.</exception>
        Task<CrawlPlan> CreateAsync(CrawlPlan plan, CancellationToken token = default);

        /// <summary>Read a plan.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Plan identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The plan, or null when it does not exist in the tenant.</returns>
        Task<CrawlPlan?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Enumerate a tenant's plans, newest first.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The plans.</returns>
        Task<List<CrawlPlan>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>Enumerate a subject's plans, newest first.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The plans.</returns>
        Task<List<CrawlPlan>> EnumerateBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default);

        /// <summary>
        /// Enumerate plans in every tenant that are enabled, idle (or whose claim expired), scheduled, and due at or
        /// before <paramref name="nowUtc"/>. Settings are not loaded; read each plan before running it.
        /// </summary>
        /// <param name="nowUtc">The current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The due plans (tenant, id, and scalar columns only).</returns>
        Task<List<CrawlPlan>> EnumerateDueAsync(DateTime nowUtc, CancellationToken token = default);

        /// <summary>Enumerate plans in every tenant whose status is not Idle (for startup recovery).</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The plans (scalar columns only).</returns>
        Task<List<CrawlPlan>> EnumerateBusyAsync(CancellationToken token = default);

        /// <summary>
        /// Update a plan's configuration (name, settings, filter, schedule, flags, labels, tags, next run). Does not
        /// change the status, the run state, the claim, or the secrets.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated plan.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="plan"/> is null.</exception>
        Task<CrawlPlan> UpdateAsync(CrawlPlan plan, CancellationToken token = default);

        /// <summary>
        /// Update a plan's run state: status, last operation, last run, last success, and next run. Setting the status
        /// to Idle also releases the claim.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="plan"/> is null.</exception>
        Task UpdateRunStateAsync(CrawlPlan plan, CancellationToken token = default);

        /// <summary>
        /// Claim a plan for a run: sets the status to Running with the claim token when the plan is Idle or its claim
        /// expired, then reads the token back. Exactly one of several concurrent callers succeeds.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Plan identifier.</param>
        /// <param name="claimToken">A token unique to the caller.</param>
        /// <param name="expiresUtc">When the claim lapses if never released.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the caller holds the claim.</returns>
        Task<bool> TryClaimAsync(string tenantId, string id, string claimToken, DateTime expiresUtc, CancellationToken token = default);

        /// <summary>Extend a held claim.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Plan identifier.</param>
        /// <param name="claimToken">The token the claim was taken with.</param>
        /// <param name="expiresUtc">The new expiry.</param>
        /// <param name="token">Cancellation token.</param>
        Task RenewClaimAsync(string tenantId, string id, string claimToken, DateTime expiresUtc, CancellationToken token = default);

        /// <summary>Set a plan's status (for example Stopping) without touching anything else.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Plan identifier.</param>
        /// <param name="status">The status.</param>
        /// <param name="token">Cancellation token.</param>
        Task SetStatusAsync(string tenantId, string id, CrawlPlanStatusEnum status, CancellationToken token = default);

        /// <summary>Delete a plan with its settings, secrets, objects, operations, and operation objects. Links are not deleted.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Plan identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the plan existed.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Read a plan's stored secrets as ciphertext by name.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="planId">Plan identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The ciphertexts by name.</returns>
        Task<Dictionary<string, string>> ReadSecretsAsync(string tenantId, string planId, CancellationToken token = default);

        /// <summary>Store (or replace) one secret's ciphertext.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="planId">Plan identifier.</param>
        /// <param name="name">Secret name (camelCase setting name).</param>
        /// <param name="ciphertext">Encrypted value.</param>
        /// <param name="token">Cancellation token.</param>
        Task SetSecretAsync(string tenantId, string planId, string name, string ciphertext, CancellationToken token = default);

        /// <summary>Delete one secret. Idempotent.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="planId">Plan identifier.</param>
        /// <param name="name">Secret name.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteSecretAsync(string tenantId, string planId, string name, CancellationToken token = default);
    }
}
