namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using Pneuma.Core.Ingestion.Pipeline;
    using Pneuma.Core.Ingestion.Deletion;
    using Pneuma.Core.Ingestion.Prompts;
    using Pneuma.Core.Observability;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Per-subject prompt override routes: the "subject scope" of the dashboard prompt view. The global scope
    /// is served by <c>PromptRoutes</c> (<c>/v1.0/prompts</c>); here each prompt key is resolved for one subject,
    /// showing the effective content, whether it is inherited or overridden, and its merge mode.
    /// </summary>
    public class SubjectPromptRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate subject prompt routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public SubjectPromptRoutes(DatabaseDriverBase db, AuthorizationService authz)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Authz = authz ?? throw new ArgumentNullException(nameof(authz));
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/subjects/{id}/prompts", ListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List a subject's prompts (effective content, inherited vs overridden)", "Prompts"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/subjects/{id}/prompts/{key}", UpsertAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Set a per-subject prompt override", "Prompts"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/subjects/{id}/prompts/{key}", DeleteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Clear a per-subject prompt override (revert to global)", "Prompts"));
        }

        #endregion

        #region Private-Methods

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc, OperationTypeEnum op)
        {
            if (await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Prompt, op, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        private async Task<Subject?> LoadSubjectAsync(HttpContextBase ctx, RequestContext rc)
        {
            if (String.IsNullOrEmpty(rc.TenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return null;
            }
            string id = RouteHelper.Param(ctx, "id");
            Subject? subject = await _Db.Subjects.ReadAsync(rc.TenantId, id, ctx.Token).ConfigureAwait(false);
            if (subject == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Subject not found.").ConfigureAwait(false);
                return null;
            }
            return subject;
        }

        private async Task ListAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            Subject? subject = await LoadSubjectAsync(ctx, rc).ConfigureAwait(false);
            if (subject == null) return;

            PromptResolver resolver = new PromptResolver(_Db);
            List<Prompt> globals = await _Db.Prompts.EnumerateAsync(rc.TenantId, ctx.Token).ConfigureAwait(false);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<SubjectPromptDto> result = new List<SubjectPromptDto>();

            foreach (Prompt global in globals)
            {
                if (String.IsNullOrEmpty(global.Key) || !seen.Add(global.Key)) continue;

                SubjectPrompt? overrideRow = await _Db.SubjectPrompts.ReadAsync(rc.TenantId!, subject.Id, global.Key, ctx.Token).ConfigureAwait(false);
                ResolvedPrompt resolved = await resolver.ResolveAsync(rc.TenantId!, subject.Id, global.Key, LegacyOverrideFor(subject, global.Key), ctx.Token).ConfigureAwait(false);

                result.Add(new SubjectPromptDto
                {
                    Key = global.Key,
                    Name = global.Name,
                    EffectiveContent = resolved.EffectiveContent,
                    GlobalContent = resolved.GlobalContent,
                    OverrideContent = overrideRow?.Content,
                    Source = resolved.Source,
                    MergeMode = overrideRow?.MergeMode ?? resolved.MergeMode
                });
            }

            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task UpsertAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Update).ConfigureAwait(false)) return;
            Subject? subject = await LoadSubjectAsync(ctx, rc).ConfigureAwait(false);
            if (subject == null) return;

            string key = RouteHelper.Param(ctx, "key");
            if (String.IsNullOrWhiteSpace(key))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A prompt key is required.").ConfigureAwait(false);
                return;
            }

            SubjectPromptUpdateRequest? request = RouteHelper.ReadBody<SubjectPromptUpdateRequest>(ctx);
            if (request == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A request body is required.").ConfigureAwait(false);
                return;
            }

            // An empty override content is a clear (revert to global), so remove any existing override.
            if (String.IsNullOrWhiteSpace(request.Content))
            {
                await _Db.SubjectPrompts.DeleteAsync(rc.TenantId!, subject.Id, key, ctx.Token).ConfigureAwait(false);
            }
            else
            {
                SubjectPrompt prompt = new SubjectPrompt
                {
                    TenantId = rc.TenantId!,
                    SubjectId = subject.Id,
                    PromptKey = key,
                    Content = request.Content,
                    MergeMode = request.MergeMode
                };
                await _Db.SubjectPrompts.UpsertAsync(prompt, ctx.Token).ConfigureAwait(false);
            }

            SubjectPromptDto dto = await BuildDtoAsync(rc.TenantId!, subject, key, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, dto).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Update).ConfigureAwait(false)) return;
            Subject? subject = await LoadSubjectAsync(ctx, rc).ConfigureAwait(false);
            if (subject == null) return;

            string key = RouteHelper.Param(ctx, "key");
            await _Db.SubjectPrompts.DeleteAsync(rc.TenantId!, subject.Id, key, ctx.Token).ConfigureAwait(false);
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        private async Task<SubjectPromptDto> BuildDtoAsync(string tenantId, Subject subject, string key, CancellationToken token)
        {
            PromptResolver resolver = new PromptResolver(_Db);
            Prompt? global = await _Db.Prompts.ReadByKeyAsync(tenantId, key, token).ConfigureAwait(false);
            SubjectPrompt? overrideRow = await _Db.SubjectPrompts.ReadAsync(tenantId, subject.Id, key, token).ConfigureAwait(false);
            ResolvedPrompt resolved = await resolver.ResolveAsync(tenantId, subject.Id, key, LegacyOverrideFor(subject, key), token).ConfigureAwait(false);

            return new SubjectPromptDto
            {
                Key = key,
                Name = global?.Name,
                EffectiveContent = resolved.EffectiveContent,
                GlobalContent = resolved.GlobalContent,
                OverrideContent = overrideRow?.Content,
                Source = resolved.Source,
                MergeMode = overrideRow?.MergeMode ?? resolved.MergeMode
            };
        }

        private static string? LegacyOverrideFor(Subject subject, string key)
        {
            if (String.Equals(key, "user.answer", StringComparison.Ordinal) || String.Equals(key, "assistant.system", StringComparison.Ordinal)) return subject.SystemPrompt;
            if (String.Equals(key, "ontology.classify", StringComparison.Ordinal)) return subject.OntologyClassifyPrompt;
            if (String.Equals(key, "ontology.definition", StringComparison.Ordinal)) return subject.OntologyDefinitionPrompt;
            if (String.Equals(key, "reranking", StringComparison.Ordinal)) return subject.RerankingPrompt;
            if (String.Equals(key, "prompt.rewrite", StringComparison.Ordinal)) return subject.PromptRewritePrompt;
            return null;
        }

        #endregion
    }
}
