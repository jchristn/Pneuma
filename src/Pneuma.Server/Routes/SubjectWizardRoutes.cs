namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Security;
    using Pneuma.Core.Wizard;
    using Pneuma.Server.Services;
    using Pneuma.Server.Streaming;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// New subject wizard routes: options, one drafting route per step, commit, and a subject's starter questions.
    /// Drafting needs Subject Create; nothing is stored until commit.
    /// </summary>
    public class SubjectWizardRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;
        private readonly SubjectWizardService _Wizard;
        private readonly SubjectWizardCommitService _Commit;
        private readonly WizardSettings _Settings;
        private readonly bool _GroundingUrlEnabled;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="wizard">Drafting service.</param>
        /// <param name="commit">Commit service.</param>
        /// <param name="settings">Wizard limits.</param>
        /// <param name="groundingUrlEnabled">True when reference URLs can be read.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public SubjectWizardRoutes(DatabaseDriverBase db, AuthorizationService authz, SubjectWizardService wizard, SubjectWizardCommitService commit, WizardSettings settings, bool groundingUrlEnabled)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Authz = authz ?? throw new ArgumentNullException(nameof(authz));
            _Wizard = wizard ?? throw new ArgumentNullException(nameof(wizard));
            _Commit = commit ?? throw new ArgumentNullException(nameof(commit));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _GroundingUrlEnabled = groundingUrlEnabled;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="server"/> is null.</exception>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            const string tag = "Subject Wizard";
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/subject-wizard/options", OptionsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("What the new subject wizard can do for the caller (ontology modes, limits, whether a completion model exists)", tag));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/subject-wizard/brief", BriefAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Draft the subject brief from the description (and an optional reference URL or text)", tag).WithRequestBody(OpenApiBodies.Json<WizardGenerateRequest>("The draft so far")));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/subject-wizard/questions", QuestionsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Draft example questions (mode replace keeps locked and edited questions; mode more adds new ones)", tag).WithRequestBody(OpenApiBodies.Json<WizardGenerateRequest>("The draft so far")));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/subject-wizard/ontology", OntologyAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Draft node and relationship types that answer the example questions", tag).WithRequestBody(OpenApiBodies.Json<WizardGenerateRequest>("The draft so far")));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/subject-wizard/prompts", PromptsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Draft the subject's additions to the answering, classification, query rewriting, and reranking prompts", tag).WithRequestBody(OpenApiBodies.Json<WizardGenerateRequest>("The draft so far")));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/subject-wizard/sources", SourcesAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Suggest where content for the subject might come from", tag).WithRequestBody(OpenApiBodies.Json<WizardGenerateRequest>("The draft so far")));
            // Streaming variants: server-sent events report progress (phase, attempt, characters written, time so far) and end
            // with { type: "complete", result } or { type: "error", statusCode, message }.
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/subject-wizard/brief/stream", ctx => StreamAsync(ctx, (tenant, request, progress) => _Wizard.BriefAsync(tenant, request, ctx.Token, progress)), RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Draft the brief, streaming progress as server-sent events", tag).WithRequestBody(OpenApiBodies.Json<WizardGenerateRequest>("The draft so far")));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/subject-wizard/questions/stream", ctx => StreamAsync(ctx, (tenant, request, progress) => _Wizard.QuestionsAsync(tenant, request, ctx.Token, progress)), RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Draft example questions, streaming progress as server-sent events", tag).WithRequestBody(OpenApiBodies.Json<WizardGenerateRequest>("The draft so far")));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/subject-wizard/ontology/stream", ctx => StreamAsync(ctx, (tenant, request, progress) => _Wizard.OntologyAsync(tenant, request, ctx.Token, progress)), RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Draft the ontology, streaming progress as server-sent events", tag).WithRequestBody(OpenApiBodies.Json<WizardGenerateRequest>("The draft so far")));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/subject-wizard/prompts/stream", ctx => StreamAsync(ctx, (tenant, request, progress) => _Wizard.PromptsAsync(tenant, request, ctx.Token, progress)), RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Draft the prompt additions, streaming progress as server-sent events", tag).WithRequestBody(OpenApiBodies.Json<WizardGenerateRequest>("The draft so far")));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/subject-wizard/sources/stream", ctx => StreamAsync(ctx, (tenant, request, progress) => _Wizard.SourcesAsync(tenant, request, ctx.Token, progress)), RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Suggest sources, streaming progress as server-sent events", tag).WithRequestBody(OpenApiBodies.Json<WizardGenerateRequest>("The draft so far")));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/subject-wizard/render-ontology", RenderOntologyAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Clean up a draft ontology and render it as the classifier will see it (no model call)", tag).WithRequestBody(OpenApiBodies.Json<WizardGenerateRequest>("The draft so far")));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/subject-wizard/commit", CommitAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Create the subject, its starter questions, and its ontology from a finished draft", tag).WithRequestBody(OpenApiBodies.Json<WizardCommitRequest>("The finished draft")));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/subjects/{id}/questions", ReadQuestionsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List a subject's starter questions", "Subjects"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/subjects/{id}/questions", ReplaceQuestionsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Replace a subject's starter questions", "Subjects").WithRequestBody(OpenApiBodies.Json<SubjectQuestionsRequest>("The questions, in order")));
        }

        #endregion

        #region Private-Methods

        private async Task OptionsAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.Subject, OperationTypeEnum.Create).ConfigureAwait(false)) return;
            List<WizardOntologyModeEnum> modes = await AllowedModesAsync(rc, ctx).ConfigureAwait(false);
            List<ModelRunner> runners = await _Db.ModelRunners.EnumerateAsync(rc.TenantId, ctx.Token).ConfigureAwait(false);
            WizardOptions options = new WizardOptions
            {
                OntologyModes = modes,
                DefaultOntologyMode = modes.Max(),
                DefaultQuestionCount = _Settings.DefaultQuestionCount,
                MaxQuestions = _Settings.MaxQuestions,
                CoverageMaxQuestions = _Settings.CoverageMaxQuestions,
                GroundingUrlEnabled = _GroundingUrlEnabled,
                MaxGroundingUrls = _Settings.MaxGroundingUrls,
                HasCompletionModel = runners.Any(r => r.Active && r.Capabilities.Contains(ModelCapabilityEnum.Completion))
            };
            await RouteHelper.SendJsonAsync(ctx, 200, options).ConfigureAwait(false);
        }

        private async Task BriefAsync(HttpContextBase ctx)
        {
            WizardGenerateRequest? request = await ReadGenerateAsync(ctx).ConfigureAwait(false);
            if (request == null) return;
            await SendAsync(ctx, await _Wizard.BriefAsync(RouteHelper.Context(ctx).TenantId!, request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task QuestionsAsync(HttpContextBase ctx)
        {
            WizardGenerateRequest? request = await ReadGenerateAsync(ctx).ConfigureAwait(false);
            if (request == null) return;
            await SendAsync(ctx, await _Wizard.QuestionsAsync(RouteHelper.Context(ctx).TenantId!, request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task OntologyAsync(HttpContextBase ctx)
        {
            WizardGenerateRequest? request = await ReadGenerateAsync(ctx).ConfigureAwait(false);
            if (request == null) return;
            await SendAsync(ctx, await _Wizard.OntologyAsync(RouteHelper.Context(ctx).TenantId!, request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PromptsAsync(HttpContextBase ctx)
        {
            WizardGenerateRequest? request = await ReadGenerateAsync(ctx).ConfigureAwait(false);
            if (request == null) return;
            await SendAsync(ctx, await _Wizard.PromptsAsync(RouteHelper.Context(ctx).TenantId!, request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task SourcesAsync(HttpContextBase ctx)
        {
            WizardGenerateRequest? request = await ReadGenerateAsync(ctx).ConfigureAwait(false);
            if (request == null) return;
            await SendAsync(ctx, await _Wizard.SourcesAsync(RouteHelper.Context(ctx).TenantId!, request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RenderOntologyAsync(HttpContextBase ctx)
        {
            WizardGenerateRequest? request = await ReadGenerateAsync(ctx).ConfigureAwait(false);
            if (request == null) return;
            WizardRenderedOntology rendered = new WizardRenderedOntology();
            int questions = request.Draft.Questions.Count(q => q != null && !String.IsNullOrWhiteSpace(q.Question));
            rendered.Ontology = WizardOntologyBuilder.Normalize(request.Draft.Ontology ?? new WizardOntology(), questions, _Settings.MaxOntologyTypes, rendered.Warnings);
            rendered.Rendered = WizardOntologyBuilder.Render(rendered.Ontology);
            await RouteHelper.SendJsonAsync(ctx, 200, rendered).ConfigureAwait(false);
        }

        private async Task CommitAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.Subject, OperationTypeEnum.Create).ConfigureAwait(false)) return;
            if (!await RequireTenantAsync(ctx, rc).ConfigureAwait(false)) return;
            WizardCommitRequest? request = RouteHelper.ReadBody<WizardCommitRequest>(ctx);
            if (request == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A finished wizard draft is required.").ConfigureAwait(false);
                return;
            }

            // Lower the ontology mode to what the caller may do rather than failing the whole commit.
            List<WizardOntologyModeEnum> allowed = await AllowedModesAsync(rc, ctx).ConfigureAwait(false);
            string? lowered = null;
            if (!allowed.Contains(request.OntologyMode))
            {
                WizardOntologyModeEnum best = allowed.Where(m => m < request.OntologyMode).DefaultIfEmpty(WizardOntologyModeEnum.Prompt).Max();
                lowered = "You may not " + (request.OntologyMode == WizardOntologyModeEnum.Approve ? "approve ontologies" : "create ontologies") + ", so the ontology was saved as " + (best == WizardOntologyModeEnum.Draft ? "a draft for an approver" : "the subject's ontology prompt") + ".";
                request.OntologyMode = best;
            }

            WizardResult<WizardCommitResult> result = await _Commit.CommitAsync(rc.TenantId!, rc.UserId, request, ctx.Token).ConfigureAwait(false);
            if (!result.Success)
            {
                await SendAsync(ctx, result).ConfigureAwait(false);
                return;
            }
            WizardCommitResult created = result.Value!;
            if (lowered != null) created.Warnings.Insert(0, lowered);
            if (created.OntologyId != null)
            {
                await OntologyRouteSupport.AuditAsync(_Db, ctx, rc, ResourceTypeEnum.Ontology, OperationTypeEnum.Write, created.OntologyId,
                    "Created ontology " + created.OntologyId + " (version " + created.OntologyVersionId + ") with the new subject wizard for subject " + created.Subject!.Id).ConfigureAwait(false);
            }
            if (created.OntologyMode == WizardOntologyModeEnum.Approve && created.OntologyVersionId != null)
            {
                await OntologyRouteSupport.AuditAsync(_Db, ctx, rc, ResourceTypeEnum.Ontology, OperationTypeEnum.Execute, created.OntologyVersionId,
                    "Approved version 1 of ontology " + created.OntologyId + " from the new subject wizard").ConfigureAwait(false);
                await OntologyRouteSupport.AuditAsync(_Db, ctx, rc, ResourceTypeEnum.Subject, OperationTypeEnum.Update, created.Subject!.Id,
                    "Pinned ontology version changed from none to " + created.OntologyVersionId + " by the new subject wizard").ConfigureAwait(false);
            }
            await RouteHelper.SendJsonAsync(ctx, 201, created).ConfigureAwait(false);
        }

        private async Task ReadQuestionsAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.Subject, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            Subject? subject = await SubjectOr404Async(ctx, rc).ConfigureAwait(false);
            if (subject == null) return;
            List<SubjectQuestion> questions = await _Db.SubjectQuestions.EnumerateBySubjectAsync(subject.TenantId, subject.Id, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, questions).ConfigureAwait(false);
        }

        private async Task ReplaceQuestionsAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.Subject, OperationTypeEnum.Update).ConfigureAwait(false)) return;
            Subject? subject = await SubjectOr404Async(ctx, rc).ConfigureAwait(false);
            if (subject == null) return;
            SubjectQuestionsRequest? request = RouteHelper.ReadBody<SubjectQuestionsRequest>(ctx);
            List<SubjectQuestion> incoming = (request?.Questions ?? new List<SubjectQuestion>()).Where(q => q != null && !String.IsNullOrWhiteSpace(q.Question)).ToList();
            if (incoming.Count > _Settings.MaxQuestions)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A subject can have at most " + _Settings.MaxQuestions + " starter questions.").ConfigureAwait(false);
                return;
            }
            List<SubjectQuestion> rows = incoming.Select(q => new SubjectQuestion
            {
                TenantId = subject.TenantId,
                SubjectId = subject.Id,
                Question = q.Question.Trim().Length > 500 ? q.Question.Trim().Substring(0, 500) : q.Question.Trim(),
                Kind = q.Kind,
                Origin = q.Origin
            }).ToList();
            List<SubjectQuestion> saved = await _Db.SubjectQuestions.ReplaceAsync(subject.TenantId, subject.Id, rows, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, saved).ConfigureAwait(false);
        }

        private async Task<WizardGenerateRequest?> ReadGenerateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.Subject, OperationTypeEnum.Create).ConfigureAwait(false)) return null;
            if (!await RequireTenantAsync(ctx, rc).ConfigureAwait(false)) return null;
            WizardGenerateRequest? request = RouteHelper.ReadBody<WizardGenerateRequest>(ctx);
            if (request == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "The wizard draft is required.").ConfigureAwait(false);
                return null;
            }
            if (!_GroundingUrlEnabled && WizardGroundingReader.UrlsOf(request.Draft).Count > 0)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Reading reference URLs is not available on this server; paste the text instead.").ConfigureAwait(false);
                return null;
            }
            return request;
        }

        private async Task StreamAsync<T>(HttpContextBase ctx, Func<string, WizardGenerateRequest, Func<WizardProgress, Task>, Task<WizardResult<T>>> run) where T : class
        {
            WizardGenerateRequest? request = await ReadGenerateAsync(ctx).ConfigureAwait(false);
            if (request == null) return;
            SseWriter sse = new SseWriter(ctx);
            Func<WizardProgress, Task> progress = p => sse.SendAsync(new
            {
                type = "progress",
                phase = p.Phase,
                attempt = p.Attempt,
                characters = p.Characters,
                elapsedMs = p.ElapsedMs,
                message = p.Message
            }, false, ctx.Token);
            WizardResult<T> result;
            try
            {
                result = await run(RouteHelper.Context(ctx).TenantId!, request, progress).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                await sse.SendAsync(new { type = "error", statusCode = 500, message = "The wizard step failed: " + e.Message }, true, ctx.Token).ConfigureAwait(false);
                return;
            }
            if (result.Success) await sse.SendAsync(new { type = "complete", result }, true, ctx.Token).ConfigureAwait(false);
            else await sse.SendAsync(new { type = "error", statusCode = result.StatusCode, message = result.Error ?? "The wizard step failed." }, true, ctx.Token).ConfigureAwait(false);
        }

        private static async Task SendAsync<T>(HttpContextBase ctx, WizardResult<T> result) where T : class
        {
            if (result.Success)
            {
                await RouteHelper.SendJsonAsync(ctx, result.StatusCode, result).ConfigureAwait(false);
                return;
            }
            string code = result.StatusCode switch
            {
                404 => "NotFound",
                409 => "Conflict",
                502 => "BadGateway",
                504 => "GatewayTimeout",
                _ => "BadRequest"
            };
            await RouteHelper.SendErrorAsync(ctx, result.StatusCode, code, result.Error ?? "The wizard step failed.").ConfigureAwait(false);
        }

        private async Task<List<WizardOntologyModeEnum>> AllowedModesAsync(RequestContext rc, HttpContextBase ctx)
        {
            List<WizardOntologyModeEnum> modes = new List<WizardOntologyModeEnum> { WizardOntologyModeEnum.Prompt };
            bool write = await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Ontology, OperationTypeEnum.Write, null, ctx.Token).ConfigureAwait(false);
            if (!write) return modes;
            modes.Add(WizardOntologyModeEnum.Draft);
            bool execute = await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Ontology, OperationTypeEnum.Execute, null, ctx.Token).ConfigureAwait(false);
            if (execute) modes.Add(WizardOntologyModeEnum.Approve);
            return modes;
        }

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc, ResourceTypeEnum resource, OperationTypeEnum op)
        {
            if (await _Authz.AuthorizeAsync(rc, resource, op, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        private static async Task<bool> RequireTenantAsync(HttpContextBase ctx, RequestContext rc)
        {
            if (!String.IsNullOrEmpty(rc.TenantId)) return true;
            await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
            return false;
        }

        private async Task<Subject?> SubjectOr404Async(HttpContextBase ctx, RequestContext rc)
        {
            Subject? subject = String.IsNullOrEmpty(rc.TenantId) ? null : await _Db.Subjects.ReadAsync(rc.TenantId, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (subject == null) await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Subject not found.").ConfigureAwait(false);
            return subject;
        }

        #endregion
    }
}
