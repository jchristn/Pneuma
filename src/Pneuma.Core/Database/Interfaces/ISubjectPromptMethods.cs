namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Per-subject prompt override data access methods.
    /// </summary>
    public interface ISubjectPromptMethods
    {
        /// <summary>Create or replace the override for a (tenant, subject, prompt key).</summary>
        /// <param name="prompt">The override to persist.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The persisted override.</returns>
        Task<SubjectPrompt> UpsertAsync(SubjectPrompt prompt, CancellationToken token = default);

        /// <summary>Read the override for a (tenant, subject, prompt key), or null if none exists.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="promptKey">Prompt key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The override, or null.</returns>
        Task<SubjectPrompt?> ReadAsync(string tenantId, string subjectId, string promptKey, CancellationToken token = default);

        /// <summary>Enumerate all overrides for a subject.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The subject's overrides.</returns>
        Task<List<SubjectPrompt>> EnumerateBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default);

        /// <summary>Delete the override for a (tenant, subject, prompt key).</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="promptKey">Prompt key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string tenantId, string subjectId, string promptKey, CancellationToken token = default);
    }
}
