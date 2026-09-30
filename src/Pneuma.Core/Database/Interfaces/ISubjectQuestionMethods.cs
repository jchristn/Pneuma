namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>Persistence for a subject's starter questions.</summary>
    public interface ISubjectQuestionMethods
    {
        /// <summary>List a subject's questions in display order.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The questions, ordered by position.</returns>
        Task<List<SubjectQuestion>> EnumerateBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default);

        /// <summary>Replace a subject's questions with the given list in one transaction.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="questions">The new questions; tenant, subject, and position are set from the arguments and list order.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored questions.</returns>
        Task<List<SubjectQuestion>> ReplaceAsync(string tenantId, string subjectId, List<SubjectQuestion> questions, CancellationToken token = default);

        /// <summary>Delete all of a subject's questions.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        Task DeleteBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default);
    }
}
