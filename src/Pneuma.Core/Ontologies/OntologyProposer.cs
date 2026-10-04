namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Ingestion.Prompts;
    using Pneuma.Core.Integrations;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Implementations;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Security;
    using Pneuma.Core.Serialization;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// The ontology authoring assistant. Samples a subject's cells (or supplied text), asks the inference model for node
    /// types, edge types, and allowed endpoints using the <c>ontology.propose</c> and <c>ontology.propose.format</c>
    /// prompts, and saves the proposal as a new draft version for a person to review and approve. When extending a
    /// version, its contents are kept and only new types and endpoints are added.
    /// </summary>
    public class OntologyProposer
    {
        #region Public-Members

        /// <summary>Default content of the <c>ontology.propose</c> prompt.</summary>
        public const string DefaultTask =
            "You are a knowledge engineer designing an ontology for a knowledge graph. From the sample content, propose the node " +
            "types (kinds of entities) and relationship types (how they relate) that best capture what the content is about. " +
            "Prefer a small, general set (typically 5 to 15 node types and 5 to 20 relationship types) over many narrow ones, and " +
            "give each type a one-sentence description a classifier can apply consistently. When an existing ontology is given, " +
            "keep its types and propose only the additions the content needs.";

        /// <summary>Default content of the <c>ontology.propose.format</c> prompt (the JSON shape the model must return).</summary>
        public const string DefaultFormat =
            "Respond with ONLY a JSON object of this exact shape and nothing else: {\"nodeTypes\":[{\"name\":\"Person\",\"description\":\"...\"}]," +
            "\"edgeTypes\":[{\"name\":\"WORKS_FOR\",\"description\":\"...\"}],\"edgeEndpoints\":[{\"edgeType\":\"WORKS_FOR\",\"fromNodeType\":\"Person\"," +
            "\"toNodeType\":\"Organization\"}],\"guidance\":\"...\",\"changeSummary\":\"...\"}. Name node types as singular nouns in PascalCase " +
            "and relationship types as verbs in UPPER_SNAKE_CASE. Only list endpoints between types you name.";

        #endregion

        #region Private-Members

        private const int _MaxSampleCharacters = 24000;
        private readonly DatabaseDriverBase _Db;
        private readonly IGraphRepositoryFactory _GraphFactory;
        private readonly Aes256Cipher _Cipher;
        private readonly OntologySettings _Settings;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the proposer.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="graphFactory">Per-tenant graph factory (sampling cells).</param>
        /// <param name="cipher">Cipher for model runner keys.</param>
        /// <param name="settings">Ontology limits.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public OntologyProposer(DatabaseDriverBase db, IGraphRepositoryFactory graphFactory, Aes256Cipher cipher, OntologySettings settings, LoggingModule logging)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _GraphFactory = graphFactory ?? throw new ArgumentNullException(nameof(graphFactory));
            _Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>Propose a new draft version of an ontology.</summary>
        /// <param name="ontology">The ontology.</param>
        /// <param name="request">The request.</param>
        /// <param name="userId">Requesting user, if known.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The new draft (201), or 400/404/502.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public async Task<OntologyResult<OntologyVersion>> ProposeAsync(TenantOntology ontology, OntologyProposeRequest request, string? userId, CancellationToken token = default)
        {
            if (ontology == null) throw new ArgumentNullException(nameof(ontology));
            if (request == null) throw new ArgumentNullException(nameof(request));

            Subject? subject = null;
            if (!String.IsNullOrWhiteSpace(request.SubjectId))
            {
                subject = await _Db.Subjects.ReadAsync(ontology.TenantId, request.SubjectId!, token).ConfigureAwait(false);
                if (subject == null) return OntologyResult<OntologyVersion>.Fail(404, "Subject not found.");
            }
            OntologyVersion? baseVersion = null;
            if (!String.IsNullOrWhiteSpace(request.BasedOnVersionId))
            {
                baseVersion = await _Db.OntologyVersions.ReadAsync(ontology.TenantId, request.BasedOnVersionId!, token).ConfigureAwait(false);
                if (baseVersion == null || baseVersion.OntologyId != ontology.Id) return OntologyResult<OntologyVersion>.Fail(404, "Version to extend not found in this ontology.");
            }

            List<string> samples = await SampleAsync(ontology.TenantId, subject, request, token).ConfigureAwait(false);
            if (samples.Count == 0) return OntologyResult<OntologyVersion>.Fail(400, "There is no sample content: give a subject with ingested content or sample text.");

            ModelRunner? runner = await ClassificationSetupBuilder.ResolveRunnerAsync(_Db, ontology.TenantId, request.ModelRunnerId, subject?.InferenceModel, token).ConfigureAwait(false);
            if (runner == null) return OntologyResult<OntologyVersion>.Fail(400, "No completion model endpoint is available.");

            PromptResolver resolver = new PromptResolver(_Db);
            ResolvedPrompt task = await resolver.ResolveAsync(ontology.TenantId, subject?.Id, "ontology.propose", null, token).ConfigureAwait(false);
            ResolvedPrompt format = await resolver.ResolveAsync(ontology.TenantId, subject?.Id, "ontology.propose.format", null, token).ConfigureAwait(false);
            string system = (String.IsNullOrWhiteSpace(task.EffectiveContent) ? DefaultTask : task.EffectiveContent) + "\n\n" +
                (String.IsNullOrWhiteSpace(format.EffectiveContent) ? DefaultFormat : format.EffectiveContent);
            string user = BuildUserPrompt(subject, request, baseVersion, samples);

            OntologyProposal proposal;
            try
            {
                proposal = await CompleteAsync(runner, system, user, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _Logging.Warn("[OntologyProposer] proposal failed: " + e.Message);
                return OntologyResult<OntologyVersion>.Fail(502, "The model did not return a usable proposal: " + e.Message);
            }

            int number = await _Db.OntologyVersions.NextVersionNumberAsync(ontology.TenantId, ontology.Id, token).ConfigureAwait(false);
            OntologyVersion draft = new OntologyVersion
            {
                TenantId = ontology.TenantId,
                OntologyId = ontology.Id,
                VersionNumber = number,
                Status = OntologyVersionStatusEnum.Draft,
                BasedOnVersionId = baseVersion?.Id,
                CreatedByUserId = userId
            };
            OntologyService.CopyContents(baseVersion, draft);
            int added = Merge(draft, proposal);
            draft.ChangeSummary = "Proposed by " + runner.Name + " from " + samples.Count.ToString(CultureInfo.InvariantCulture) + " sample(s): " + added.ToString(CultureInfo.InvariantCulture) +
                " addition(s)." + (String.IsNullOrWhiteSpace(proposal.ChangeSummary) ? String.Empty : " " + proposal.ChangeSummary!.Trim());

            List<string> errors = OntologyVersionValidator.Errors(draft);
            if (errors.Count > 0) return OntologyResult<OntologyVersion>.Fail(502, "The proposal could not be saved: " + String.Join(" ", errors), errors);
            await _Db.OntologyVersions.CreateAsync(draft, token).ConfigureAwait(false);
            draft.Problems = OntologyVersionValidator.Problems(draft);
            return OntologyResult<OntologyVersion>.Ok(draft, 201);
        }

        #endregion

        #region Private-Methods

        private async Task<List<string>> SampleAsync(string tenantId, Subject? subject, OntologyProposeRequest request, CancellationToken token)
        {
            List<string> samples = new List<string>();
            int wanted = Math.Clamp(request.SampleCells, 1, _Settings.MaxProposalSampleCells);
            if (!String.IsNullOrWhiteSpace(request.SampleText))
            {
                foreach (string paragraph in request.SampleText!.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!String.IsNullOrWhiteSpace(paragraph)) samples.Add(paragraph.Trim());
                }
            }
            if (subject != null && samples.Count < wanted)
            {
                IGraphRepository graph = await _GraphFactory.ForTenantAsync(tenantId, token).ConfigureAwait(false);
                List<GraphNode> cells = await SubjectGraphReader.ReadCellsAsync(graph, subject.Id, 2000, token).ConfigureAwait(false);
                List<GraphNode> withText = cells.Where(c => !String.IsNullOrWhiteSpace(c.Content)).ToList();
                int take = Math.Min(wanted - samples.Count, withText.Count);
                for (int i = 0; i < take; i++)
                {
                    // Spread the picks across the subject's cells rather than taking the first few documents only.
                    int index = (int)((long)i * withText.Count / Math.Max(1, take));
                    samples.Add(withText[index].Content!.Trim());
                }
            }

            List<string> bounded = new List<string>();
            int total = 0;
            foreach (string sample in samples)
            {
                if (total >= _MaxSampleCharacters) break;
                string piece = sample.Length > 2000 ? sample.Substring(0, 2000) : sample;
                bounded.Add(piece);
                total += piece.Length;
            }
            return bounded;
        }

        private static string BuildUserPrompt(Subject? subject, OntologyProposeRequest request, OntologyVersion? baseVersion, List<string> samples)
        {
            StringBuilder sb = new StringBuilder();
            if (subject != null) sb.AppendLine("Subject: " + subject.DisplayName + (String.IsNullOrWhiteSpace(subject.Description) ? String.Empty : " — " + subject.Description));
            sb.AppendLine("Write the descriptions in " + (String.IsNullOrWhiteSpace(request.Language) ? "English" : request.Language!.Trim()) + ".");
            if (!String.IsNullOrWhiteSpace(request.Instructions)) sb.AppendLine("Additional instructions: " + request.Instructions!.Trim());
            if (baseVersion != null)
            {
                sb.AppendLine();
                sb.AppendLine("Existing ontology (keep these types; propose only additions):");
                sb.AppendLine(OntologyDefinitionRenderer.Render(baseVersion));
            }
            sb.AppendLine();
            sb.AppendLine("Sample content:");
            for (int i = 0; i < samples.Count; i++) sb.AppendLine("[" + (i + 1).ToString(CultureInfo.InvariantCulture) + "] " + samples[i]);
            return sb.ToString();
        }

        private async Task<OntologyProposal> CompleteAsync(ModelRunner runner, string system, string user, CancellationToken token)
        {
            string? apiKey = null;
            if (!String.IsNullOrEmpty(runner.AuthMaterialEncrypted))
            {
                try { apiKey = _Cipher.Decrypt(runner.AuthMaterialEncrypted); }
                catch (Exception) { apiKey = null; }
            }
            CompletionClientBase client = ModelClientFactory.Create(runner, apiKey, _Logging);
            CompletionOptions options = new CompletionOptions { Temperature = 0.2, MaxTokens = 4096, SystemPrompt = system };
            ChatResponse response = await client.ChatAsync(user, options, token).ConfigureAwait(false);
            if (response == null || !response.Success || String.IsNullOrWhiteSpace(response.Text)) throw ModelResponseErrors.ToException("ontology proposal", response?.Error);

            string text = response.Text.Trim();
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start < 0 || end <= start) throw new InvalidOperationException("The response contained no JSON object.");
            OntologyProposal? proposal = Json.Deserialize<OntologyProposal>(text.Substring(start, end - start + 1));
            if (proposal == null) throw new InvalidOperationException("The response was empty.");
            return proposal;
        }

        private static int Merge(OntologyVersion draft, OntologyProposal proposal)
        {
            int added = 0;
            foreach (OntologyProposalType type in proposal.NodeTypes ?? new List<OntologyProposalType>())
            {
                if (String.IsNullOrWhiteSpace(type.Name)) continue;
                string name = Truncate(type.Name!.Trim(), 128);
                OntologyNodeType? existing = draft.NodeTypes.FirstOrDefault(t => OntologyTypeResolver.Same(t.Name, name));
                if (existing == null)
                {
                    draft.NodeTypes.Add(new OntologyNodeType { Name = name, Description = type.Description });
                    added++;
                }
                else if (String.IsNullOrWhiteSpace(existing.Description)) existing.Description = type.Description;
            }
            foreach (OntologyProposalType type in proposal.EdgeTypes ?? new List<OntologyProposalType>())
            {
                if (String.IsNullOrWhiteSpace(type.Name)) continue;
                string name = Truncate(Ontology.ToUpperSnake(type.Name!), 128);
                if (name.Length == 0) continue;
                OntologyEdgeType? existing = draft.EdgeTypes.FirstOrDefault(t => OntologyTypeResolver.Same(t.Name, name));
                if (existing == null)
                {
                    draft.EdgeTypes.Add(new OntologyEdgeType { Name = name, Description = type.Description });
                    added++;
                }
                else if (String.IsNullOrWhiteSpace(existing.Description)) existing.Description = type.Description;
            }
            OntologyTypeResolver types = new OntologyTypeResolver(draft);
            foreach (OntologyProposalEndpoint endpoint in proposal.EdgeEndpoints ?? new List<OntologyProposalEndpoint>())
            {
                string? edge = types.DeclaredEdge(endpoint.EdgeType);
                string? from = types.DeclaredNode(endpoint.FromNodeType);
                string? to = types.DeclaredNode(endpoint.ToNodeType);
                if (edge == null || from == null || to == null) continue;
                bool exists = draft.Rules.Any(r => r.RuleType == OntologyRuleTypeEnum.EdgeEndpoints && OntologyTypeResolver.Same(r.EdgeType, edge)
                    && OntologyTypeResolver.Same(r.FromNodeType, from) && OntologyTypeResolver.Same(r.ToNodeType, to));
                if (exists) continue;
                draft.Rules.Add(new OntologyRule { RuleType = OntologyRuleTypeEnum.EdgeEndpoints, EdgeType = edge, FromNodeType = from, ToNodeType = to, Action = OntologyRuleActionEnum.Warn });
                added++;
            }
            if (String.IsNullOrWhiteSpace(draft.Guidance) && !String.IsNullOrWhiteSpace(proposal.Guidance)) draft.Guidance = proposal.Guidance!.Trim();
            return added;
        }

        private static string Truncate(string value, int length)
        {
            return value.Length <= length ? value : value.Substring(0, length);
        }

        #endregion
    }
}
