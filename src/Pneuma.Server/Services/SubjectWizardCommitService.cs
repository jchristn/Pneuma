namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ontologies;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Wizard;
    using SyslogLogging;

    /// <summary>
    /// Creates a subject from a finished new subject wizard draft: the subject with its brief, models, collection, and
    /// prompt additions; its starter questions; and its ontology, as prompt text or as a governed ontology (a draft, or
    /// an approved version pinned to the subject). Either everything is created or, on failure, what was created is
    /// removed again.
    /// </summary>
    public class SubjectWizardCommitService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly OntologyService _Ontologies;
        private readonly ICollectionStore _Collections;
        private readonly string? _DefaultCollectionId;
        private readonly WizardSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly string _Header = "[SubjectWizardCommitService] ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the service.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="ontologies">Ontology service.</param>
        /// <param name="collections">Collection store (default collection).</param>
        /// <param name="defaultCollectionId">Configured default collection, or null.</param>
        /// <param name="settings">Wizard limits.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public SubjectWizardCommitService(DatabaseDriverBase db, OntologyService ontologies, ICollectionStore collections, string? defaultCollectionId, WizardSettings settings, LoggingModule logging)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Ontologies = ontologies ?? throw new ArgumentNullException(nameof(ontologies));
            _Collections = collections ?? throw new ArgumentNullException(nameof(collections));
            _DefaultCollectionId = defaultCollectionId;
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>Create the subject, its questions, and its ontology.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="userId">Creating user, if known.</param>
        /// <param name="request">The request; its ontology mode must already be one the caller may use.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What was created (201), or 400/409/502.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public async Task<WizardResult<WizardCommitResult>> CommitAsync(string tenantId, string? userId, WizardCommitRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            SubjectWizardDraft draft = request.Draft;
            WizardBrief brief = draft.Brief ?? new WizardBrief();
            string displayName = (brief.DisplayName ?? String.Empty).Trim();
            if (displayName.Length == 0) return WizardResult<WizardCommitResult>.Fail(400, "The brief needs a display name.");
            List<WizardQuestion> questions = draft.Questions.Where(q => q != null && !String.IsNullOrWhiteSpace(q.Question)).ToList();
            if (questions.Count > _Settings.MaxQuestions) return WizardResult<WizardCommitResult>.Fail(400, "The draft has too many questions (" + _Settings.MaxQuestions + " at most).");

            WizardResult<WizardCommitResult> result = new WizardResult<WizardCommitResult> { StatusCode = 201 };
            WizardCommitResult created = new WizardCommitResult { OntologyMode = request.OntologyMode };
            WizardOntology? ontology = null;
            if (draft.Ontology != null && draft.Ontology.NodeTypes.Count > 0)
            {
                ontology = WizardOntologyBuilder.Normalize(draft.Ontology, questions.Count, _Settings.MaxOntologyTypes, created.Warnings);
                created.RenderedOntology = WizardOntologyBuilder.Render(ontology);
            }
            else created.OntologyMode = WizardOntologyModeEnum.Prompt;

            string? inference = await ResolveRunnerAsync(tenantId, request.InferenceModel, ModelCapabilityEnum.Completion, token).ConfigureAwait(false);
            string? embedding = await ResolveRunnerAsync(tenantId, request.EmbeddingModel, ModelCapabilityEnum.Embedding, token).ConfigureAwait(false);
            if (!String.IsNullOrWhiteSpace(request.InferenceModel) && inference == null) return WizardResult<WizardCommitResult>.Fail(400, "The inference model endpoint was not found or cannot complete text.");
            if (!String.IsNullOrWhiteSpace(request.EmbeddingModel) && embedding == null) return WizardResult<WizardCommitResult>.Fail(400, "The embedding model endpoint was not found or cannot embed.");
            string? collection = await CollectionResolver.ResolveAsync(_Collections, tenantId, request.Collection, _DefaultCollectionId, token).ConfigureAwait(false);
            if (inference == null) created.Warnings.Add("No completion model endpoint is set; choose one on the subject before adding content.");
            if (embedding == null) created.Warnings.Add("No embedding model endpoint is set; choose one on the subject before adding content.");
            if (collection == null) created.Warnings.Add("No collection is set; choose one on the subject before adding content.");

            WizardPrompts prompts = draft.Prompts ?? new WizardPrompts();
            Subject subject = new Subject
            {
                DisplayName = displayName,
                Type = String.IsNullOrWhiteSpace(brief.Type) ? "Subject" : brief.Type!.Trim(),
                Description = Blank(brief.Description),
                Tagline = Blank(brief.Tagline),
                SystemPrompt = Blank(prompts.SystemPrompt),
                OntologyClassifyPrompt = Blank(prompts.ClassifyPrompt),
                OntologyDefinitionPrompt = created.RenderedOntology == null ? null : "Domain types for this subject:\n" + created.RenderedOntology,
                PromptRewritePrompt = Blank(prompts.RewritePrompt),
                RerankingPrompt = Blank(prompts.RerankingPrompt),
                InferenceModel = inference,
                EmbeddingModel = embedding,
                Collection = collection,
                PublishedForChat = request.PublishedForChat
            };
            string? slugProblem = await SubjectCreation.PrepareAsync(_Db, tenantId, subject, token).ConfigureAwait(false);
            if (slugProblem != null) return WizardResult<WizardCommitResult>.Fail(409, slugProblem);

            Subject stored = await _Db.Subjects.CreateAsync(subject, token).ConfigureAwait(false);
            TenantOntology? tenantOntology = null;
            try
            {
                List<SubjectQuestion> rows = questions.Select(q => new SubjectQuestion
                {
                    TenantId = tenantId,
                    SubjectId = stored.Id,
                    Question = q.Question.Trim().Length > 500 ? q.Question.Trim().Substring(0, 500) : q.Question.Trim(),
                    Kind = q.Kind,
                    Origin = q.Origin
                }).ToList();
                created.Questions = await _Db.SubjectQuestions.ReplaceAsync(tenantId, stored.Id, rows, token).ConfigureAwait(false);

                if (ontology != null && created.OntologyMode != WizardOntologyModeEnum.Prompt)
                {
                    tenantOntology = await SaveOntologyAsync(tenantId, userId, stored, ontology, created, token).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _Logging.Warn(_Header + "commit of " + stored.Id + " failed, removing what was created: " + e.Message);
                await RollbackAsync(tenantId, stored.Id, tenantOntology, token).ConfigureAwait(false);
                return WizardResult<WizardCommitResult>.Fail(502, "The subject could not be created: " + e.Message);
            }

            created.Subject = await _Db.Subjects.ReadAsync(tenantId, stored.Id, token).ConfigureAwait(false) ?? stored;
            result.Value = created;
            result.Warnings = created.Warnings;
            return result;
        }

        #endregion

        #region Private-Methods

        private async Task<TenantOntology?> SaveOntologyAsync(string tenantId, string? userId, Subject subject, WizardOntology ontology, WizardCommitResult created, CancellationToken token)
        {
            string name = await UniqueOntologyNameAsync(tenantId, subject.DisplayName + " ontology", token).ConfigureAwait(false);
            OntologyResult<TenantOntology> made = await _Ontologies.CreateAsync(tenantId, new OntologyCreateRequest
            {
                Name = name,
                Description = "Created by the new subject wizard for " + subject.DisplayName + "."
            }, userId, token).ConfigureAwait(false);
            if (made.Value == null) throw new InvalidOperationException(made.Error ?? "the ontology could not be created.");
            TenantOntology tenantOntology = made.Value;
            created.OntologyId = tenantOntology.Id;

            List<OntologyVersion> versions = await _Db.OntologyVersions.EnumerateAsync(tenantId, tenantOntology.Id, token).ConfigureAwait(false);
            OntologyVersion? draft = versions.Count == 0 ? null : await _Db.OntologyVersions.ReadAsync(tenantId, versions[0].Id, token).ConfigureAwait(false);
            if (draft == null) throw new InvalidOperationException("the ontology's first draft was not found.");
            OntologyVersion contents = WizardOntologyBuilder.ToVersion(ontology);
            contents.ChangeSummary = "Drafted by the new subject wizard from " + subject.DisplayName + "'s example questions.";
            OntologyResult<OntologyVersion> saved = await _Ontologies.SaveDraftAsync(draft, contents, token).ConfigureAwait(false);
            if (saved.Value == null) throw new InvalidOperationException(saved.Error ?? "the ontology draft could not be saved.");
            created.OntologyVersionId = saved.Value.Id;

            if (created.OntologyMode != WizardOntologyModeEnum.Approve) return tenantOntology;
            OntologyResult<OntologyVersion> approved = await _Ontologies.ApproveAsync(saved.Value, userId, "Approved from the new subject wizard.", token).ConfigureAwait(false);
            if (approved.Value == null)
            {
                created.OntologyMode = WizardOntologyModeEnum.Draft;
                created.Warnings.Add("The ontology was saved as a draft and not approved: " + (approved.Error ?? "it did not pass approval checks") + " The subject uses the ontology prompt until a version is approved and pinned.");
                return tenantOntology;
            }
            OntologyResult<SubjectPinResult> pinned = await _Ontologies.PinAsync(subject, approved.Value.Id, token).ConfigureAwait(false);
            if (pinned.Value == null)
            {
                created.Warnings.Add("The ontology was approved but not pinned: " + (pinned.Error ?? "unknown reason") + ".");
                created.OntologyMode = WizardOntologyModeEnum.Draft;
            }
            return tenantOntology;
        }

        private async Task<string> UniqueOntologyNameAsync(string tenantId, string baseName, CancellationToken token)
        {
            string trimmed = baseName.Length > 180 ? baseName.Substring(0, 180) : baseName;
            if (await _Db.Ontologies.ReadByNameAsync(tenantId, trimmed, token).ConfigureAwait(false) == null) return trimmed;
            for (int i = 2; i < 1000; i++)
            {
                string candidate = trimmed + " " + i;
                if (await _Db.Ontologies.ReadByNameAsync(tenantId, candidate, token).ConfigureAwait(false) == null) return candidate;
            }
            return trimmed + " " + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private async Task<string?> ResolveRunnerAsync(string tenantId, string? requested, ModelCapabilityEnum capability, CancellationToken token)
        {
            List<ModelRunner> runners = await _Db.ModelRunners.EnumerateAsync(tenantId, token).ConfigureAwait(false);
            List<ModelRunner> usable = runners.Where(r => r.Active && r.Capabilities.Contains(capability)).ToList();
            if (!String.IsNullOrWhiteSpace(requested)) return usable.FirstOrDefault(r => r.Id == requested)?.Id;
            return usable.FirstOrDefault()?.Id;
        }

        private async Task RollbackAsync(string tenantId, string subjectId, TenantOntology? ontology, CancellationToken token)
        {
            try
            {
                await _Db.SubjectQuestions.DeleteBySubjectAsync(tenantId, subjectId, CancellationToken.None).ConfigureAwait(false);
                await _Db.Subjects.DeleteWithSubordinatesAsync(tenantId, subjectId, new List<string>(), new List<string>(), CancellationToken.None).ConfigureAwait(false);
                if (ontology != null) await _Ontologies.DeleteAsync(ontology, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "rollback of " + subjectId + " was incomplete: " + e.Message);
            }
        }

        private static string? Blank(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
