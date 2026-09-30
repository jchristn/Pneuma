namespace Pneuma.Server.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Security;
    using Pneuma.Core.Serialization;
    using Pneuma.Core.Wizard;
    using Pneuma.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// New subject wizard tools for agents: draft a subject (brief, questions, ontology, prompts) in one call, then create
    /// it from the draft. An agent can show the draft to its user and edit it before creating, exactly like the dashboards.
    /// </summary>
    public class McpWizardTools
    {
        #region Private-Members

        private readonly SubjectWizardService _Wizard;
        private readonly SubjectWizardCommitService _Commit;
        private readonly AuthorizationService _Authz;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the tools.</summary>
        /// <param name="wizard">Drafting service.</param>
        /// <param name="commit">Commit service.</param>
        /// <param name="authz">Authorization service (ontology modes the caller may use).</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public McpWizardTools(SubjectWizardService wizard, SubjectWizardCommitService commit, AuthorizationService authz)
        {
            _Wizard = wizard ?? throw new ArgumentNullException(nameof(wizard));
            _Commit = commit ?? throw new ArgumentNullException(nameof(commit));
            _Authz = authz ?? throw new ArgumentNullException(nameof(authz));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// <c>pneuma_draft_subject</c>: run the brief, questions, ontology, and prompts steps in order and return the draft.
        /// </summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">description (required), groundingText, groundingUrl, modelRunnerId, questionCount, guidance.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The draft and what each step reported, or null when an error response was sent.</returns>
        public async Task<object?> DraftSubjectAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            WizardGenerateRequest request = new WizardGenerateRequest
            {
                ModelRunnerId = NullIfEmpty(McpJsonRpc.GetStringArgument(arguments, "modelRunnerId")),
                Guidance = NullIfEmpty(McpJsonRpc.GetStringArgument(arguments, "guidance")),
                Count = Math.Max(0, McpJsonRpc.GetIntArgument(arguments, "questionCount", 0)),
                Draft = new SubjectWizardDraft
                {
                    Description = McpJsonRpc.GetStringArgument(arguments, "description"),
                    GroundingText = NullIfEmpty(McpJsonRpc.GetStringArgument(arguments, "groundingText")),
                    GroundingUrl = NullIfEmpty(McpJsonRpc.GetStringArgument(arguments, "groundingUrl"))
                }
            };
            List<string> warnings = new List<string>();
            long elapsed = 0;

            WizardResult<WizardBrief> brief = await _Wizard.BriefAsync(tenantId, request, token).ConfigureAwait(false);
            if (!await OkAsync(ctx, id, "brief", brief).ConfigureAwait(false)) return null;
            request.Draft.Brief = brief.Value;
            if (!String.IsNullOrWhiteSpace(brief.GroundingExcerpt)) request.Draft.GroundingText = brief.GroundingExcerpt;
            request.Draft.GroundingUrl = null;
            warnings.AddRange(brief.Warnings);
            elapsed += brief.ElapsedMs;

            WizardResult<List<WizardQuestion>> questions = await _Wizard.QuestionsAsync(tenantId, request, token).ConfigureAwait(false);
            if (!await OkAsync(ctx, id, "questions", questions).ConfigureAwait(false)) return null;
            request.Draft.Questions = questions.Value!;
            warnings.AddRange(questions.Warnings);
            elapsed += questions.ElapsedMs;

            WizardResult<WizardOntology> ontology = await _Wizard.OntologyAsync(tenantId, request, token).ConfigureAwait(false);
            if (!await OkAsync(ctx, id, "ontology", ontology).ConfigureAwait(false)) return null;
            request.Draft.Ontology = ontology.Value;
            warnings.AddRange(ontology.Warnings);
            elapsed += ontology.ElapsedMs;

            WizardResult<WizardPrompts> prompts = await _Wizard.PromptsAsync(tenantId, request, token).ConfigureAwait(false);
            if (!await OkAsync(ctx, id, "prompts", prompts).ConfigureAwait(false)) return null;
            request.Draft.Prompts = prompts.Value;
            warnings.AddRange(prompts.Warnings);
            elapsed += prompts.ElapsedMs;

            return new
            {
                draft = request.Draft,
                renderedOntology = WizardOntologyBuilder.Render(request.Draft.Ontology!),
                model = prompts.Model,
                modelRunnerId = prompts.ModelRunnerId,
                elapsedMs = elapsed,
                warnings,
                next = "Show the draft to the user, apply their changes, then call pneuma_create_subject_from_draft with it."
            };
        }

        /// <summary>
        /// <c>pneuma_create_subject_from_draft</c>: create the subject, its starter questions, and its ontology from a draft.
        /// The ontology mode is lowered to what the caller may do.
        /// </summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">draft (required), inferenceModel, embeddingModel, collection, ontologyMode.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What was created, or null when an error response was sent.</returns>
        public async Task<object?> CreateSubjectAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty("draft", out JsonElement draftElement) || draftElement.ValueKind != JsonValueKind.Object)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'draft' (an object, as returned by pneuma_draft_subject) is required.").ConfigureAwait(false);
                return null;
            }
            SubjectWizardDraft? draft;
            try
            {
                draft = Json.Deserialize<SubjectWizardDraft>(draftElement.GetRawText());
            }
            catch (Exception e)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'draft' could not be read: " + e.Message).ConfigureAwait(false);
                return null;
            }
            if (draft == null)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'draft' is empty.").ConfigureAwait(false);
                return null;
            }

            WizardOntologyModeEnum requested = WizardOntologyModeEnum.Approve;
            string modeText = McpJsonRpc.GetStringArgument(arguments, "ontologyMode");
            if (!String.IsNullOrWhiteSpace(modeText) && !Enum.TryParse(modeText.Trim(), true, out requested))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: ontologyMode is Prompt, Draft, or Approve.").ConfigureAwait(false);
                return null;
            }
            WizardOntologyModeEnum allowed = await HighestModeAsync(rc, token).ConfigureAwait(false);
            string? lowered = null;
            if (requested > allowed)
            {
                lowered = "The ontology mode was lowered from " + requested + " to " + allowed + " because the caller may not use " + requested + ".";
                requested = allowed;
            }

            WizardCommitRequest request = new WizardCommitRequest
            {
                Draft = draft,
                InferenceModel = NullIfEmpty(McpJsonRpc.GetStringArgument(arguments, "inferenceModel")),
                EmbeddingModel = NullIfEmpty(McpJsonRpc.GetStringArgument(arguments, "embeddingModel")),
                Collection = NullIfEmpty(McpJsonRpc.GetStringArgument(arguments, "collection")),
                OntologyMode = requested
            };
            WizardResult<WizardCommitResult> result = await _Commit.CommitAsync(rc.TenantId ?? String.Empty, rc.UserId, request, token).ConfigureAwait(false);
            if (!await OkAsync(ctx, id, "commit", result).ConfigureAwait(false)) return null;
            WizardCommitResult created = result.Value!;
            if (lowered != null) created.Warnings.Insert(0, lowered);
            return created;
        }

        #endregion

        #region Private-Methods

        private async Task<WizardOntologyModeEnum> HighestModeAsync(RequestContext rc, CancellationToken token)
        {
            if (!await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Ontology, OperationTypeEnum.Write, null, token).ConfigureAwait(false)) return WizardOntologyModeEnum.Prompt;
            if (!await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Ontology, OperationTypeEnum.Execute, null, token).ConfigureAwait(false)) return WizardOntologyModeEnum.Draft;
            return WizardOntologyModeEnum.Approve;
        }

        private static async Task<bool> OkAsync<T>(HttpContextBase ctx, object? id, string step, WizardResult<T> result) where T : class
        {
            if (result.Success) return true;
            int code = result.StatusCode == 400 ? -32602 : (result.StatusCode == 409 ? -32009 : -32000);
            await McpJsonRpc.SendErrorAsync(ctx, id, code, "The " + step + " step failed: " + (result.Error ?? "unknown error")).ConfigureAwait(false);
            return false;
        }

        private static string? NullIfEmpty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
