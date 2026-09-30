namespace Pneuma.Core.Ingestion.Stages
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Ontologies;

    /// <summary>
    /// Canonicalizes the candidate subgraph's node and edge types in place, then enforces the subject's pinned ontology
    /// version. Types are coerced to the version's declared types (or the built-in types when no version is pinned) so
    /// casing and spelling variance does not fragment the ontology. With a pinned version, its rules are applied:
    /// breaking elements are kept with a warning, reversed, dropped, or quarantined, and each is recorded as a violation.
    /// </summary>
    public class OntologyCanonicalizationStage : IStage
    {
        #region Private-Members

        private readonly StageDependencies _Deps;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the stage.</summary>
        /// <param name="deps">Shared stage dependencies.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="deps"/> is null.</exception>
        public OntologyCanonicalizationStage(StageDependencies deps)
        {
            _Deps = deps ?? throw new ArgumentNullException(nameof(deps));
        }

        #endregion

        #region Public-Members

        /// <inheritdoc />
        public IngestionStageEnum Stage => IngestionStageEnum.OntologyCanonicalization;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task ExecuteAsync(StageContext context, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            OntologyVersion? version = context.Classification?.Version;
            if (version == null)
            {
                int normalized = SubgraphMerger.Canonicalize(context.Subgraph);
                context.Message = "Ontology canonicalization complete — normalized " + normalized + " node/edge type(s) to the canonical ontology.";
                return;
            }

            int canonicalized = Canonicalize(context.Subgraph, new OntologyTypeResolver(version));
            OntologyRuleOutcome outcome = new OntologyRuleEngine(version).ApplyToCandidate(context.Subgraph);
            await RecordAsync(context, version, outcome, token).ConfigureAwait(false);

            string name = context.Classification?.OntologyName ?? version.OntologyId;
            context.Message = "Ontology canonicalization and validation complete against " + name + " v" + version.VersionNumber.ToString(CultureInfo.InvariantCulture) +
                " — normalized " + canonicalized + " type(s); " + outcome.Violations.Count + " violation(s): " + outcome.Warned + " warned, " +
                outcome.Reversed + " reversed, " + outcome.Dropped + " dropped, " + outcome.Quarantined + " quarantined" +
                (outcome.DroppedWithNode > 0 ? ", " + outcome.DroppedWithNode + " relationship(s) removed with their node" : String.Empty) + ".";
        }

        #endregion

        #region Private-Methods

        private static int Canonicalize(CandidateSubgraph subgraph, OntologyTypeResolver types)
        {
            int count = 0;
            foreach (CandidateNode node in subgraph.Nodes)
            {
                if (String.IsNullOrWhiteSpace(node.NodeType) || String.IsNullOrWhiteSpace(node.Name)) continue;
                node.NodeType = types.CanonicalNode(node.NodeType);
                count++;
            }
            foreach (CandidateEdge edge in subgraph.Edges)
            {
                if (String.IsNullOrWhiteSpace(edge.EdgeType)) continue;
                edge.EdgeType = types.CanonicalEdge(edge.EdgeType);
                count++;
            }
            return count;
        }

        private async Task RecordAsync(StageContext context, OntologyVersion version, OntologyRuleOutcome outcome, CancellationToken token)
        {
            if (outcome.Violations.Count == 0) return;
            context.Job.Completeness.OntologyViolations = outcome.Violations.Count;

            Dictionary<string, int> byTypeAndAction = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (OntologyViolation violation in outcome.Violations)
            {
                string metricKey = (violation.RuleType?.ToString() ?? "Undeclared") + "|" + violation.Action;
                int current;
                byTypeAndAction[metricKey] = byTypeAndAction.TryGetValue(metricKey, out current) ? current + 1 : 1;
            }
            foreach (KeyValuePair<string, int> pair in byTypeAndAction)
            {
                string[] parts = pair.Key.Split('|');
                PneumaMetrics.RecordOntologyViolations(parts[0], parts[1], pair.Value);
            }

            int limit = _Deps.Ontology.MaxViolationsPerJob;
            List<OntologyViolation> kept = new List<OntologyViolation>();
            foreach (OntologyViolation violation in outcome.Violations)
            {
                if (kept.Count >= limit) break;
                violation.TenantId = context.Job.TenantId;
                violation.SubjectId = context.Job.SubjectId;
                violation.JobId = context.Job.Id;
                violation.LinkId = context.Job.LinkId;
                violation.OntologyVersionId = version.Id;
                kept.Add(violation);
            }
            try
            {
                await _Deps.Db.OntologyViolations.CreateManyAsync(kept, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _Deps.Logging.Warn("[OntologyCanonicalizationStage] could not record violations: " + e.Message);
            }

            if (outcome.Dropped + outcome.Quarantined > 0)
            {
                context.AddWarning(outcome.Dropped.ToString(CultureInfo.InvariantCulture) + " element(s) were dropped and " +
                    outcome.Quarantined.ToString(CultureInfo.InvariantCulture) + " quarantined by the ontology's rules; see the subject's ontology violations.");
            }
            if (outcome.Violations.Count > kept.Count)
            {
                context.AddWarning((outcome.Violations.Count - kept.Count).ToString(CultureInfo.InvariantCulture) + " further ontology violation(s) were counted but not recorded (the limit is " +
                    limit.ToString(CultureInfo.InvariantCulture) + " per job).");
            }
        }

        #endregion
    }
}
