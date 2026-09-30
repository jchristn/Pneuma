namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Integrations;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Security;

    /// <summary>
    /// Runs one claimed ontology operation to completion: Validate checks the stored graph against the pinned version's
    /// rules, Retag makes every stored cell's taxonomy links match the pinned version's taxonomy, and DriftCheck
    /// classifies a sample of cells twice (bypassing the cache) and reports how often the result changes.
    /// </summary>
    public class OntologyOperationProcessor
    {
        #region Private-Members

        private const int _MaxItems = 500;
        private readonly DatabaseDriverBase _Db;
        private readonly IGraphRepositoryFactory _GraphFactory;
        private readonly Aes256Cipher _Cipher;
        private readonly PolyPromptClassifier _Classifier;
        private readonly OntologySettings _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the processor.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="graphFactory">Per-tenant graph factory.</param>
        /// <param name="cipher">Cipher for model runner keys.</param>
        /// <param name="classifier">Classifier (drift checks).</param>
        /// <param name="settings">Ontology limits.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public OntologyOperationProcessor(DatabaseDriverBase db, IGraphRepositoryFactory graphFactory, Aes256Cipher cipher, PolyPromptClassifier classifier, OntologySettings settings)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _GraphFactory = graphFactory ?? throw new ArgumentNullException(nameof(graphFactory));
            _Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
            _Classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Public-Methods

        /// <summary>Run an operation (already marked running) and record its outcome.</summary>
        /// <param name="operation">The operation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The finished operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is null.</exception>
        public async Task<OntologyOperation> ProcessAsync(OntologyOperation operation, CancellationToken token = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            try
            {
                Subject? subject = await _Db.Subjects.ReadAsync(operation.TenantId, operation.SubjectId, token).ConfigureAwait(false);
                if (subject == null) throw new InvalidOperationException("The subject no longer exists.");
                operation.OntologyVersionId = subject.OntologyVersionId;
                OntologyVersion? version = String.IsNullOrWhiteSpace(subject.OntologyVersionId)
                    ? null
                    : await _Db.OntologyVersions.ReadAsync(subject.TenantId, subject.OntologyVersionId!, token).ConfigureAwait(false);
                IGraphRepository graph = await _GraphFactory.ForTenantAsync(subject.TenantId, token).ConfigureAwait(false);

                switch (operation.Kind)
                {
                    case OntologyOperationKindEnum.Validate:
                        if (version == null) throw new InvalidOperationException("The subject has no pinned ontology version to validate against.");
                        await ValidateAsync(operation, subject, version, graph, token).ConfigureAwait(false);
                        break;
                    case OntologyOperationKindEnum.Retag:
                        await RetagAsync(operation, subject, version, graph, token).ConfigureAwait(false);
                        break;
                    default:
                        await DriftCheckAsync(operation, subject, graph, token).ConfigureAwait(false);
                        break;
                }
                operation.Status = OntologyOperationStatusEnum.Succeeded;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                operation.Status = OntologyOperationStatusEnum.Failed;
                operation.Error = e.Message;
            }
            operation.FinishedUtc = DateTime.UtcNow;
            await _Db.OntologyOperations.UpdateAsync(operation, token).ConfigureAwait(false);
            PneumaMetrics.RecordOntologyOperation(operation.Kind.ToString(), operation.Status.ToString());
            return operation;
        }

        #endregion

        #region Private-Methods

        private async Task ValidateAsync(OntologyOperation operation, Subject subject, OntologyVersion version, IGraphRepository graph, CancellationToken token)
        {
            SubjectGraph stored = await SubjectGraphReader.ReadAsync(graph, subject.Id, _Settings.MaxGraphNodes, token).ConfigureAwait(false);
            operation.Total = stored.Nodes.Count;
            List<OntologyViolation> violations = new OntologyRuleEngine(version).ValidateGraph(stored.Nodes, stored.Edges);
            foreach (OntologyViolation violation in violations)
            {
                violation.TenantId = subject.TenantId;
                violation.SubjectId = subject.Id;
                violation.OperationId = operation.Id;
                violation.OntologyVersionId = version.Id;
                violation.Status = OntologyViolationStatusEnum.Recorded;
            }
            await _Db.OntologyViolations.DeleteValidationFindingsAsync(subject.TenantId, subject.Id, token).ConfigureAwait(false);
            await _Db.OntologyViolations.CreateManyAsync(violations, token).ConfigureAwait(false);
            operation.Processed = stored.Nodes.Count;
            operation.Changed = violations.Count;
            if (stored.Truncated) operation.Error = "Only the first " + _Settings.MaxGraphNodes.ToString(CultureInfo.InvariantCulture) + " nodes were checked (Ontology.MaxGraphNodes).";
        }

        private async Task RetagAsync(OntologyOperation operation, Subject subject, OntologyVersion? version, IGraphRepository graph, CancellationToken token)
        {
            OntologyVersion effective = version ?? new OntologyVersion();
            TaxonomyMatcher? matcher = effective.Concepts.Count > 0 ? new TaxonomyMatcher(effective.Concepts) : null;
            TaxonomyLinker linker = new TaxonomyLinker(graph, effective, subject.TenantId, subject.Id);
            List<GraphNode> cells = await SubjectGraphReader.ReadCellsAsync(graph, subject.Id, _Settings.MaxGraphNodes, token).ConfigureAwait(false);
            operation.Total = cells.Count;
            await _Db.OntologyOperations.UpdateAsync(operation, token).ConfigureAwait(false);

            List<OntologyOperationItem> items = new List<OntologyOperationItem>();
            foreach (GraphNode cell in cells)
            {
                token.ThrowIfCancellationRequested();
                List<string> keys = matcher == null ? new List<string>() : matcher.Match(cell.Content).Select(m => m.ConceptKey).Distinct(StringComparer.Ordinal).ToList();
                TaxonomyLinkResult result = await linker.RetagCellAsync(cell, keys, token).ConfigureAwait(false);
                operation.Processed++;
                operation.Added += result.Added;
                operation.Removed += result.Removed;
                if (result.Added + result.Removed > 0)
                {
                    operation.Changed++;
                    if (items.Count < _MaxItems)
                    {
                        items.Add(new OntologyOperationItem
                        {
                            OperationId = operation.Id,
                            Ordinal = items.Count,
                            NodeId = cell.Id,
                            Excerpt = Excerpt(cell.Content),
                            Changed = true,
                            Detail = "Added " + result.Added.ToString(CultureInfo.InvariantCulture) + " and removed " + result.Removed.ToString(CultureInfo.InvariantCulture) + " taxonomy link(s)."
                        });
                    }
                }
                if (operation.Processed % 50 == 0) await _Db.OntologyOperations.UpdateAsync(operation, token).ConfigureAwait(false);
            }
            await _Db.OntologyOperations.AddItemsAsync(subject.TenantId, items, token).ConfigureAwait(false);
            PneumaMetrics.RecordTaxonomyLinks("added", operation.Added);
            PneumaMetrics.RecordTaxonomyLinks("removed", operation.Removed);
        }

        private async Task DriftCheckAsync(OntologyOperation operation, Subject subject, IGraphRepository graph, CancellationToken token)
        {
            ClassificationSetup? setup = await ClassificationSetupBuilder.BuildAsync(_Db, _Cipher, subject.TenantId, subject, null, token).ConfigureAwait(false);
            if (setup == null) throw new InvalidOperationException("No completion model endpoint is available for classification.");

            List<GraphNode> cells = await SubjectGraphReader.ReadCellsAsync(graph, subject.Id, _Settings.MaxGraphNodes, token).ConfigureAwait(false);
            cells = cells.Where(c => !String.IsNullOrWhiteSpace(c.Content)).ToList();
            int size = Math.Min(Math.Min(operation.SampleSize, _Settings.MaxDriftSampleSize), cells.Count);
            Random random = new Random();
            List<GraphNode> sample = cells.OrderBy(c => random.Next()).Take(size).ToList();
            operation.Total = sample.Count;
            await _Db.OntologyOperations.UpdateAsync(operation, token).ConfigureAwait(false);

            List<OntologyOperationItem> items = new List<OntologyOperationItem>();
            int compared = 0;
            foreach (GraphNode cell in sample)
            {
                token.ThrowIfCancellationRequested();
                OntologyOperationItem item = new OntologyOperationItem { OperationId = operation.Id, Ordinal = items.Count, NodeId = cell.Id, Excerpt = Excerpt(cell.Content) };
                try
                {
                    List<ExtractedCell> window = new List<ExtractedCell> { new ExtractedCell { Type = "Text", Text = cell.Content ?? String.Empty } };
                    List<string> keys = setup.Matcher == null ? new List<string>() : setup.Matcher.Match(cell.Content).Select(m => m.ConceptKey).ToList();
                    string userPrompt = PolyPromptClassifier.BuildUserPrompt(window, subject.DisplayName, setup.Hint(keys));
                    CandidateSubgraph first = await _Classifier.ClassifyAsync(setup.SystemPrompt, userPrompt, setup.Runner, setup.ApiKey, setup.Temperature, token).ConfigureAwait(false);
                    CandidateSubgraph second = await _Classifier.ClassifyAsync(setup.SystemPrompt, userPrompt, setup.Runner, setup.ApiKey, setup.Temperature, token).ConfigureAwait(false);
                    ClassificationDrift drift = ClassificationDrift.Compare(first, second);
                    compared++;
                    item.Changed = drift.Changed;
                    item.Detail = drift.Describe();
                    if (drift.Changed) operation.Changed++;
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    item.Detail = "Classification failed: " + e.Message;
                }
                items.Add(item);
                operation.Processed++;
                operation.DriftRate = compared == 0 ? 0 : (double)operation.Changed / compared;
                await _Db.OntologyOperations.UpdateAsync(operation, token).ConfigureAwait(false);
            }
            await _Db.OntologyOperations.AddItemsAsync(subject.TenantId, items, token).ConfigureAwait(false);
            if (sample.Count > 0 && compared == 0) throw new InvalidOperationException("Every sampled classification failed; see the items for the errors.");
        }

        private static string? Excerpt(string? text)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            string trimmed = text!.Trim();
            return trimmed.Length <= 160 ? trimmed : trimmed.Substring(0, 160) + "...";
        }

        #endregion
    }
}
