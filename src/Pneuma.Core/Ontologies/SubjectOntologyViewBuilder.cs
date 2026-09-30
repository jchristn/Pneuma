namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Prompts;
    using Pneuma.Core.Models;
    using Pneuma.Core.Responses;

    /// <summary>Builds the <see cref="SubjectOntologyView"/> of a subject (shared by the REST routes and MCP tools).</summary>
    public static class SubjectOntologyViewBuilder
    {
        #region Public-Methods

        /// <summary>Build the view.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="cache">Classification cache (entry count).</param>
        /// <param name="subject">The subject.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The view.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static async Task<SubjectOntologyView> BuildAsync(DatabaseDriverBase db, ClassificationCache cache, Subject subject, CancellationToken token = default)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            if (subject == null) throw new ArgumentNullException(nameof(subject));

            SubjectOntologyView view = new SubjectOntologyView
            {
                SubjectId = subject.Id,
                ClassificationTemperature = subject.ClassificationTemperature,
                ClassificationCacheEnabled = subject.ClassificationCacheEnabled,
                CacheEntries = await cache.CountAsync(subject.TenantId, subject.Id, token).ConfigureAwait(false)
            };

            OntologyVersion? version = String.IsNullOrWhiteSpace(subject.OntologyVersionId)
                ? null
                : await db.OntologyVersions.ReadAsync(subject.TenantId, subject.OntologyVersionId!, token).ConfigureAwait(false);
            if (version != null)
            {
                view.Source = "Version";
                view.Ontology = await db.Ontologies.ReadAsync(subject.TenantId, version.OntologyId, token).ConfigureAwait(false);
                view.EffectiveDefinition = OntologyDefinitionRenderer.Render(version);
                view.ConceptCount = version.Concepts.Count;
                version.NodeTypeCount = version.NodeTypes.Count;
                version.EdgeTypeCount = version.EdgeTypes.Count;
                version.RuleCount = version.Rules.Count;
                version.ConceptCount = version.Concepts.Count;
                version.NodeTypes = new List<OntologyNodeType>();
                version.EdgeTypes = new List<OntologyEdgeType>();
                version.Rules = new List<OntologyRule>();
                version.Concepts = new List<OntologyConcept>();
                view.Version = version;
            }
            else
            {
                ResolvedPrompt definition = await new PromptResolver(db).ResolveAsync(subject.TenantId, subject.Id, "ontology.definition", subject.OntologyDefinitionPrompt, token).ConfigureAwait(false);
                view.Source = "Prompt";
                view.EffectiveDefinition = definition.EffectiveContent;
            }

            List<OntologyViolation> quarantined = await db.OntologyViolations.EnumerateAsync(subject.TenantId, subject.Id, OntologyViolationStatusEnum.Quarantined, null, null, token).ConfigureAwait(false);
            view.QuarantinedCount = quarantined.Count;
            return view;
        }

        #endregion
    }
}
