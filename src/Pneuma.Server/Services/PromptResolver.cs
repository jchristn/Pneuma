namespace Pneuma.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;

    /// <summary>
    /// Resolves the effective content of a prompt for a tenant and (optionally) a subject: the global default,
    /// combined with any per-subject override (Append or Replace) and then any legacy per-subject addendum
    /// (the original <c>Subject</c> prompt columns) so pre-existing behavior is preserved. Global is the
    /// fallback when no override exists.
    /// </summary>
    public class PromptResolver
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the resolver.</summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public PromptResolver(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the effective prompt content for a key, applying a per-subject override and legacy addendum.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier, or null to resolve the global default only.</param>
        /// <param name="promptKey">Prompt key.</param>
        /// <param name="legacyOverride">Legacy per-subject addendum (an original <c>Subject</c> prompt column), appended last. May be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The resolved prompt.</returns>
        public async Task<ResolvedPrompt> ResolveAsync(string tenantId, string? subjectId, string promptKey, string? legacyOverride, CancellationToken token = default)
        {
            Prompt? global = await _Database.Prompts.ReadByKeyAsync(tenantId, promptKey, token).ConfigureAwait(false);
            string globalContent = global?.Content ?? String.Empty;

            ResolvedPrompt resolved = new ResolvedPrompt
            {
                GlobalContent = globalContent,
                EffectiveContent = globalContent,
                Source = PromptSourceEnum.Global,
                MergeMode = PromptMergeModeEnum.Append
            };

            if (!String.IsNullOrWhiteSpace(subjectId))
            {
                SubjectPrompt? subjectPrompt = await _Database.SubjectPrompts.ReadAsync(tenantId, subjectId!, promptKey, token).ConfigureAwait(false);
                if (subjectPrompt != null && !String.IsNullOrWhiteSpace(subjectPrompt.Content))
                {
                    resolved.Source = PromptSourceEnum.SubjectOverride;
                    resolved.MergeMode = subjectPrompt.MergeMode;
                    resolved.EffectiveContent = subjectPrompt.MergeMode == PromptMergeModeEnum.Replace
                        ? subjectPrompt.Content!.Trim()
                        : Combine(globalContent, subjectPrompt.Content!);
                }
            }

            // The legacy per-subject prompt columns (Subject.SystemPrompt, etc.) are always appended after,
            // preserving the exact behavior that existed before per-key overrides were introduced.
            if (!String.IsNullOrWhiteSpace(legacyOverride))
            {
                resolved.EffectiveContent = Combine(resolved.EffectiveContent, legacyOverride!);
                if (resolved.Source == PromptSourceEnum.Global) resolved.Source = PromptSourceEnum.SubjectOverride;
            }

            return resolved;
        }

        #endregion

        #region Private-Methods

        private static string Combine(string baseText, string addition)
        {
            if (String.IsNullOrWhiteSpace(baseText)) return addition.Trim();
            return baseText + "\n\n" + addition.Trim();
        }

        #endregion
    }
}
