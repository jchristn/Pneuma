namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Ontologies;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Tenant ontology routes: the built-in templates, ontology CRUD, an ontology's versions, starting a draft, and the
    /// authoring assistant. Versions themselves are in <see cref="OntologyVersionRoutes"/>; a subject's use of an
    /// ontology is in <see cref="SubjectOntologyRoutes"/>.
    /// </summary>
    public class OntologyRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;
        private readonly OntologyService _Ontologies;
        private readonly OntologyProposer _Proposer;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="ontologies">Ontology service.</param>
        /// <param name="proposer">Authoring assistant.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public OntologyRoutes(DatabaseDriverBase db, AuthorizationService authz, OntologyService ontologies, OntologyProposer proposer)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Authz = authz ?? throw new ArgumentNullException(nameof(authz));
            _Ontologies = ontologies ?? throw new ArgumentNullException(nameof(ontologies));
            _Proposer = proposer ?? throw new ArgumentNullException(nameof(proposer));
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="server"/> is null.</exception>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            const string tag = "Ontologies";

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/ontology-templates", TemplatesAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List the built-in ontology templates a tenant can start from", tag));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/ontologies", ListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List the tenant's ontologies", tag));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/ontologies", CreateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Create an ontology; its first version is a draft (empty, from a template, or a copy of a version)", tag).WithRequestBody(OpenApiBodies.Json<OntologyCreateRequest>("The ontology")));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/ontologies/{id}", ReadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read an ontology with its versions and the subjects that pin them", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/ontologies/{id}", UpdateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Rename or re-describe an ontology", tag).WithRequestBody(OpenApiBodies.Json<OntologyUpdateRequest>("The new name and description")));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/ontologies/{id}", DeleteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete an ontology and all its versions (refused while a subject pins one)", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/ontologies/{id}/versions", ListVersionsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List an ontology's versions, newest first", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/ontologies/{id}/versions", CreateDraftAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Start a new draft that copies a version (by default the newest)", tag).WithRequestBody(OpenApiBodies.Json<OntologyDraftRequest>("The version to copy")));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/ontologies/{id}/propose", ProposeAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Have the inference model propose a new draft from a subject's content or sample text (uses the ontology.propose prompts)", tag).WithRequestBody(OpenApiBodies.Json<OntologyProposeRequest>("What to sample and how")));
        }

        #endregion

        #region Private-Methods

        private async Task TemplatesAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            await RouteHelper.SendJsonAsync(ctx, 200, OntologyTemplates.List()).ConfigureAwait(false);
        }

        private async Task ListAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            List<TenantOntology> ontologies = await _Db.Ontologies.EnumerateAsync(Tenant(rc), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, EnumerationHelper.Paginate(ontologies, RouteHelper.ReadEnumerationQuery(ctx), o => o.CreatedUtc, o => o.Name)).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Write).ConfigureAwait(false)) return;
            OntologyCreateRequest? request = RouteHelper.ReadBody<OntologyCreateRequest>(ctx);
            if (request == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "An ontology body is required.").ConfigureAwait(false);
                return;
            }
            OntologyResult<TenantOntology> result = await _Ontologies.CreateAsync(Tenant(rc), request, rc.UserId, ctx.Token).ConfigureAwait(false);
            if (!await OntologyRouteSupport.SendFailureAsync(ctx, result).ConfigureAwait(false)) return;
            await RouteHelper.SendJsonAsync(ctx, 201, await DetailAsync(result.Value!, ctx).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            TenantOntology? ontology = await ReadOr404Async(ctx, rc).ConfigureAwait(false);
            if (ontology == null) return;
            await RouteHelper.SendJsonAsync(ctx, 200, await DetailAsync(ontology, ctx).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Write).ConfigureAwait(false)) return;
            TenantOntology? ontology = await ReadOr404Async(ctx, rc).ConfigureAwait(false);
            if (ontology == null) return;
            OntologyUpdateRequest? request = RouteHelper.ReadBody<OntologyUpdateRequest>(ctx);
            if (request == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A body is required.").ConfigureAwait(false);
                return;
            }
            OntologyResult<TenantOntology> result = await _Ontologies.UpdateAsync(ontology, request, ctx.Token).ConfigureAwait(false);
            if (!await OntologyRouteSupport.SendFailureAsync(ctx, result).ConfigureAwait(false)) return;
            await RouteHelper.SendJsonAsync(ctx, 200, await DetailAsync(result.Value!, ctx).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Delete).ConfigureAwait(false)) return;
            TenantOntology? ontology = await ReadOr404Async(ctx, rc).ConfigureAwait(false);
            if (ontology == null) return;
            OntologyResult<bool> result = await _Ontologies.DeleteAsync(ontology, ctx.Token).ConfigureAwait(false);
            if (!await OntologyRouteSupport.SendFailureAsync(ctx, result).ConfigureAwait(false)) return;
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        private async Task ListVersionsAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            TenantOntology? ontology = await ReadOr404Async(ctx, rc).ConfigureAwait(false);
            if (ontology == null) return;
            List<OntologyVersion> versions = await _Db.OntologyVersions.EnumerateAsync(ontology.TenantId, ontology.Id, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, EnumerationHelper.Paginate(versions, RouteHelper.ReadEnumerationQuery(ctx), v => v.CreatedUtc, v => v.Id)).ConfigureAwait(false);
        }

        private async Task CreateDraftAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Write).ConfigureAwait(false)) return;
            TenantOntology? ontology = await ReadOr404Async(ctx, rc).ConfigureAwait(false);
            if (ontology == null) return;
            OntologyDraftRequest request = RouteHelper.ReadBody<OntologyDraftRequest>(ctx) ?? new OntologyDraftRequest();
            OntologyResult<OntologyVersion> result = await _Ontologies.CreateDraftAsync(ontology, request.BasedOnVersionId, rc.UserId, ctx.Token).ConfigureAwait(false);
            if (!await OntologyRouteSupport.SendFailureAsync(ctx, result).ConfigureAwait(false)) return;
            await RouteHelper.SendJsonAsync(ctx, 201, result.Value).ConfigureAwait(false);
        }

        private async Task ProposeAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Write).ConfigureAwait(false)) return;
            TenantOntology? ontology = await ReadOr404Async(ctx, rc).ConfigureAwait(false);
            if (ontology == null) return;
            OntologyProposeRequest request = RouteHelper.ReadBody<OntologyProposeRequest>(ctx) ?? new OntologyProposeRequest();
            OntologyResult<OntologyVersion> result = await _Proposer.ProposeAsync(ontology, request, rc.UserId, ctx.Token).ConfigureAwait(false);
            if (!await OntologyRouteSupport.SendFailureAsync(ctx, result).ConfigureAwait(false)) return;
            await RouteHelper.SendJsonAsync(ctx, 201, result.Value).ConfigureAwait(false);
        }

        private async Task<OntologyDetail> DetailAsync(TenantOntology ontology, HttpContextBase ctx)
        {
            List<OntologyVersion> versions = await _Db.OntologyVersions.EnumerateAsync(ontology.TenantId, ontology.Id, ctx.Token).ConfigureAwait(false);
            List<OntologySubjectReference> pins = await _Ontologies.PinsAsync(ontology.TenantId, versions.Select(v => v.Id), ctx.Token).ConfigureAwait(false);
            return new OntologyDetail { Ontology = ontology, Versions = versions, PinnedSubjects = pins };
        }

        private async Task<TenantOntology?> ReadOr404Async(HttpContextBase ctx, RequestContext rc)
        {
            TenantOntology? ontology = await _Db.Ontologies.ReadAsync(Tenant(rc), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (ontology == null) await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Ontology not found.").ConfigureAwait(false);
            return ontology;
        }

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc, OperationTypeEnum op)
        {
            if (await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Ontology, op, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        private static string Tenant(RequestContext rc)
        {
            return rc.TenantId ?? String.Empty;
        }

        #endregion
    }
}
