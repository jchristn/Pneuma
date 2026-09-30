namespace Pneuma.Server.Services
{
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Models;

    /// <summary>
    /// The rules every new subject goes through, whether it comes from the subject form or the new subject wizard:
    /// tenant, create-time defaults, graph root, and a unique URL slug.
    /// </summary>
    public static class SubjectCreation
    {
        #region Public-Methods

        /// <summary>
        /// Prepare a subject for creation: set the tenant, clear the pinned ontology version (pinning has its own audited
        /// route), fill the graph root, tagline, and prompt defaults, and resolve a unique URL slug.
        /// </summary>
        /// <param name="db">Database driver.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subject">The subject to prepare.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Null when ready; otherwise the reason (an explicit URL slug that is already taken).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="db"/> or <paramref name="subject"/> is null.</exception>
        public static async Task<string?> PrepareAsync(DatabaseDriverBase db, string tenantId, Subject subject, CancellationToken token)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (subject == null) throw new ArgumentNullException(nameof(subject));
            subject.TenantId = tenantId;
            subject.OntologyVersionId = null;
            if (String.IsNullOrWhiteSpace(subject.GraphRootNodeId)) subject.GraphRootNodeId = SlugHelper.Slugify(subject.DisplayName);
            if (String.IsNullOrWhiteSpace(subject.Tagline)) subject.Tagline = Subject.DefaultTagline;
            if (String.IsNullOrWhiteSpace(subject.RerankingPrompt)) subject.RerankingPrompt = Subject.DefaultRerankingPrompt;
            if (String.IsNullOrWhiteSpace(subject.PromptRewritePrompt)) subject.PromptRewritePrompt = Subject.DefaultPromptRewritePrompt;

            // An explicit, already-taken slug is a conflict; an auto-generated one is de-duplicated with a numeric suffix
            // so subject creation never fails on a name clash.
            bool explicitSlug = !String.IsNullOrWhiteSpace(subject.UrlSlug);
            string desiredSlug = SlugHelper.Slugify(explicitSlug ? subject.UrlSlug : subject.DisplayName);
            if (String.IsNullOrWhiteSpace(desiredSlug)) desiredSlug = "subject";
            Subject? clash = await db.Subjects.ReadBySlugAsync(tenantId, desiredSlug, token).ConfigureAwait(false);
            if (clash != null)
            {
                if (explicitSlug) return "A subject with URL slug '" + desiredSlug + "' already exists.";
                desiredSlug = await NextAvailableSlugAsync(db, tenantId, desiredSlug, token).ConfigureAwait(false);
            }
            subject.UrlSlug = desiredSlug;
            return null;
        }

        #endregion

        #region Private-Methods

        private static async Task<string> NextAvailableSlugAsync(DatabaseDriverBase db, string tenantId, string baseSlug, CancellationToken token)
        {
            for (int suffix = 2; suffix < 10000; suffix++)
            {
                string candidate = baseSlug + "-" + suffix.ToString(CultureInfo.InvariantCulture);
                if (await db.Subjects.ReadBySlugAsync(tenantId, candidate, token).ConfigureAwait(false) == null) return candidate;
            }
            return baseSlug + "-" + IdGenerator.GenerateSubjectId();
        }

        #endregion
    }
}
