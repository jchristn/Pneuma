namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Prompts;
    using Pneuma.Core.Integrations;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Security;

    /// <summary>
    /// Builds a subject's <see cref="ClassificationSetup"/>: resolves the completion runner, the classification prompts
    /// (system default, tenant override, subject override), the pinned ontology version and its rendered definition, and
    /// the taxonomy matcher.
    /// </summary>
    public static class ClassificationSetupBuilder
    {
        #region Public-Members

        /// <summary>Default content of the <c>taxonomy.hint</c> prompt, used when it resolves to nothing.</summary>
        public const string DefaultTaxonomyHint =
            "These taxonomy concepts were found in the cells. When a cell is about one of them, create its node with exactly " +
            "this name and node type instead of a variant:";

        #endregion

        #region Public-Methods

        /// <summary>Build the setup, or return null when no completion runner is available.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="cipher">Cipher for the runner's stored key.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subject">The subject (null uses global prompts and no ontology version).</param>
        /// <param name="preferredRunnerId">The runner to prefer (the job's completion runner), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The setup, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="db"/> or <paramref name="cipher"/> is null.</exception>
        public static async Task<ClassificationSetup?> BuildAsync(DatabaseDriverBase db, Aes256Cipher cipher, string tenantId, Subject? subject, string? preferredRunnerId, CancellationToken token = default)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (cipher == null) throw new ArgumentNullException(nameof(cipher));

            ModelRunner? runner = await ResolveRunnerAsync(db, tenantId, preferredRunnerId, subject?.InferenceModel, token).ConfigureAwait(false);
            if (runner == null) return null;

            ClassificationSetup setup = new ClassificationSetup
            {
                Runner = runner,
                ApiKey = Decrypt(cipher, runner.AuthMaterialEncrypted),
                Temperature = subject?.ClassificationTemperature ?? 0,
                CacheEnabled = subject?.ClassificationCacheEnabled ?? true
            };

            PromptResolver resolver = new PromptResolver(db);
            string? subjectId = subject?.Id;
            ResolvedPrompt task = await resolver.ResolveAsync(tenantId, subjectId, "ontology.classify", subject?.OntologyClassifyPrompt, token).ConfigureAwait(false);
            ResolvedPrompt format = await resolver.ResolveAsync(tenantId, subjectId, "ontology.classify.format", null, token).ConfigureAwait(false);
            ResolvedPrompt hint = await resolver.ResolveAsync(tenantId, subjectId, "taxonomy.hint", null, token).ConfigureAwait(false);
            setup.TaxonomyHintPrompt = String.IsNullOrWhiteSpace(hint.EffectiveContent) ? DefaultTaxonomyHint : hint.EffectiveContent;

            string ontologyLine;
            if (subject != null && !String.IsNullOrWhiteSpace(subject.OntologyVersionId))
                setup.Version = await db.OntologyVersions.ReadAsync(tenantId, subject.OntologyVersionId!, token).ConfigureAwait(false);
            if (setup.Version != null)
            {
                TenantOntology? ontology = await db.Ontologies.ReadAsync(tenantId, setup.Version.OntologyId, token).ConfigureAwait(false);
                setup.OntologyName = ontology?.Name;
                setup.OntologyDefinition = OntologyDefinitionRenderer.Render(setup.Version);
                if (setup.Version.Concepts.Count > 0) setup.Matcher = new TaxonomyMatcher(setup.Version.Concepts);
                ontologyLine = "ontology " + (setup.OntologyName ?? setup.Version.OntologyId) + " v" + setup.Version.VersionNumber.ToString(CultureInfo.InvariantCulture) + " (" + setup.Version.Id + ")";
            }
            else
            {
                ResolvedPrompt definition = await resolver.ResolveAsync(tenantId, subjectId, "ontology.definition", subject?.OntologyDefinitionPrompt, token).ConfigureAwait(false);
                setup.OntologyDefinition = definition.EffectiveContent;
                ontologyLine = "ontology.definition@" + ClassificationCache.ShortHash(setup.OntologyDefinition);
            }

            setup.SystemPrompt = PolyPromptClassifier.BuildSystemPrompt(task.EffectiveContent, setup.OntologyDefinition, format.EffectiveContent);
            setup.Provenance = ontologyLine +
                ", ontology.classify@" + ClassificationCache.ShortHash(task.EffectiveContent) +
                ", ontology.classify.format@" + ClassificationCache.ShortHash(format.EffectiveContent) +
                (setup.Matcher != null ? ", taxonomy.hint@" + ClassificationCache.ShortHash(setup.TaxonomyHintPrompt) : String.Empty) +
                ", model " + runner.Name + (String.IsNullOrWhiteSpace(runner.DefaultModel) ? String.Empty : "/" + runner.DefaultModel) +
                ", temperature " + setup.Temperature.ToString("0.##", CultureInfo.InvariantCulture);
            return setup;
        }

        /// <summary>
        /// Resolve a completion runner: the first active runner among the preferred ids, else the tenant's first active
        /// completion runner.
        /// </summary>
        /// <param name="db">Database driver.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="preferredRunnerId">First choice, or null.</param>
        /// <param name="subjectRunnerId">Second choice (the subject's inference model), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The runner, or null when none is available.</returns>
        public static async Task<ModelRunner?> ResolveRunnerAsync(DatabaseDriverBase db, string tenantId, string? preferredRunnerId, string? subjectRunnerId, CancellationToken token)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            foreach (string? candidate in new List<string?> { preferredRunnerId, subjectRunnerId })
            {
                if (String.IsNullOrWhiteSpace(candidate)) continue;
                ModelRunner? byId = await db.ModelRunners.ReadAsync(candidate!, token).ConfigureAwait(false);
                if (byId != null && byId.Active) return byId;
            }
            List<ModelRunner> runners = await db.ModelRunners.EnumerateAsync(tenantId, token).ConfigureAwait(false);
            foreach (ModelRunner runner in runners)
            {
                if (runner.Active && runner.Capabilities.Contains(ModelCapabilityEnum.Completion)) return runner;
            }
            return null;
        }

        #endregion

        #region Private-Methods

        private static string? Decrypt(Aes256Cipher cipher, string? encrypted)
        {
            if (String.IsNullOrEmpty(encrypted)) return null;
            try { return cipher.Decrypt(encrypted!); }
            catch (Exception) { return null; }
        }

        #endregion
    }
}
