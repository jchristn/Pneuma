namespace Pneuma.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ontologies;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Ontology version routes: read, edit and delete drafts, approve and retire, compare, show the rendered definition,
    /// export as OWL and SKOS, and import a SKOS taxonomy into a draft. Approving and retiring need Ontology Execute and
    /// are audited.
    /// </summary>
    public class OntologyVersionRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;
        private readonly OntologyService _Ontologies;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="ontologies">Ontology service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public OntologyVersionRoutes(DatabaseDriverBase db, AuthorizationService authz, OntologyService ontologies)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Authz = authz ?? throw new ArgumentNullException(nameof(authz));
            _Ontologies = ontologies ?? throw new ArgumentNullException(nameof(ontologies));
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

            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/ontology-versions/{id}", ReadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read a version with its node types, edge types, rules, taxonomy concepts, and approval problems", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/ontology-versions/{id}", UpdateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Replace a draft's contents (approved and retired versions are immutable)", tag).WithRequestBody(OpenApiBodies.Json<OntologyVersion>("The draft's contents")));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/ontology-versions/{id}", DeleteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete a draft version", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/ontology-versions/{id}/approve", ApproveAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Approve a draft so subjects can pin it (needs Ontology Execute; audited)", tag).WithRequestBody(OpenApiBodies.Json<OntologyApproveRequest>("Optional change summary")));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/ontology-versions/{id}/retire", RetireAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Retire an approved version so it cannot be newly pinned (needs Ontology Execute; audited)", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/ontology-versions/{id}/diff", DiffAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Compare a version with another (?against=; default the version it was copied from)", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/ontology-versions/{id}/definition", DefinitionAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Show the ontology definition text the classifier sees for this version", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/ontology-versions/{id}/export", ExportAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Export a version as OWL and SKOS (?format=turtle|jsonld, ?baseIri=)", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/ontology-versions/{id}/taxonomy/import", ImportAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Import a SKOS taxonomy (the raw Turtle or JSON-LD document as the body) into a draft (?format=turtle|jsonld, ?mode=merge|replace)", tag));
        }

        #endregion

        #region Private-Methods

        private async Task ReadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            OntologyVersion? version = await ReadOr404Async(ctx, rc).ConfigureAwait(false);
            if (version == null) return;
            if (version.Status == OntologyVersionStatusEnum.Draft) version.Problems = OntologyVersionValidator.Problems(version);
            await RouteHelper.SendJsonAsync(ctx, 200, version).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Write).ConfigureAwait(false)) return;
            OntologyVersion? existing = await ReadOr404Async(ctx, rc).ConfigureAwait(false);
            if (existing == null) return;
            OntologyVersion? incoming = RouteHelper.ReadBody<OntologyVersion>(ctx);
            if (incoming == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "The version's contents are required.").ConfigureAwait(false);
                return;
            }
            OntologyResult<OntologyVersion> result = await _Ontologies.SaveDraftAsync(existing, incoming, ctx.Token).ConfigureAwait(false);
            if (!await OntologyRouteSupport.SendFailureAsync(ctx, result).ConfigureAwait(false)) return;
            await RouteHelper.SendJsonAsync(ctx, 200, result.Value).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Delete).ConfigureAwait(false)) return;
            OntologyVersion? version = await ReadOr404Async(ctx, rc).ConfigureAwait(false);
            if (version == null) return;
            OntologyResult<bool> result = await _Ontologies.DeleteVersionAsync(version, ctx.Token).ConfigureAwait(false);
            if (!await OntologyRouteSupport.SendFailureAsync(ctx, result).ConfigureAwait(false)) return;
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        private async Task ApproveAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Execute).ConfigureAwait(false)) return;
            OntologyVersion? version = await ReadOr404Async(ctx, rc).ConfigureAwait(false);
            if (version == null) return;
            OntologyApproveRequest request = RouteHelper.ReadBody<OntologyApproveRequest>(ctx) ?? new OntologyApproveRequest();
            OntologyResult<OntologyVersion> result = await _Ontologies.ApproveAsync(version, rc.UserId, request.ChangeSummary, ctx.Token).ConfigureAwait(false);
            if (!await OntologyRouteSupport.SendFailureAsync(ctx, result).ConfigureAwait(false)) return;
            await OntologyRouteSupport.AuditAsync(_Db, ctx, rc, ResourceTypeEnum.Ontology, OperationTypeEnum.Execute, version.Id,
                "Approved version " + version.VersionNumber + " of ontology " + version.OntologyId).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, result.Value).ConfigureAwait(false);
        }

        private async Task RetireAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Execute).ConfigureAwait(false)) return;
            OntologyVersion? version = await ReadOr404Async(ctx, rc).ConfigureAwait(false);
            if (version == null) return;
            OntologyResult<OntologyVersion> result = await _Ontologies.RetireAsync(version, ctx.Token).ConfigureAwait(false);
            if (!await OntologyRouteSupport.SendFailureAsync(ctx, result).ConfigureAwait(false)) return;
            await OntologyRouteSupport.AuditAsync(_Db, ctx, rc, ResourceTypeEnum.Ontology, OperationTypeEnum.Execute, version.Id,
                "Retired version " + version.VersionNumber + " of ontology " + version.OntologyId).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, result.Value).ConfigureAwait(false);
        }

        private async Task DiffAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            OntologyVersion? version = await ReadOr404Async(ctx, rc).ConfigureAwait(false);
            if (version == null) return;
            OntologyResult<OntologyVersionDiff> result = await _Ontologies.DiffAsync(version, RouteHelper.Query(ctx, "against"), ctx.Token).ConfigureAwait(false);
            if (!await OntologyRouteSupport.SendFailureAsync(ctx, result).ConfigureAwait(false)) return;
            await RouteHelper.SendJsonAsync(ctx, 200, result.Value).ConfigureAwait(false);
        }

        private async Task DefinitionAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            OntologyVersion? version = await ReadOr404Async(ctx, rc).ConfigureAwait(false);
            if (version == null) return;
            await RouteHelper.SendJsonAsync(ctx, 200, new OntologyDefinitionResponse { VersionId = version.Id, Definition = OntologyDefinitionRenderer.Render(version) }).ConfigureAwait(false);
        }

        private async Task ExportAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            OntologyVersion? version = await ReadOr404Async(ctx, rc).ConfigureAwait(false);
            if (version == null) return;
            RdfFormatEnum? format = OntologyRouteSupport.RdfFormat(RouteHelper.Query(ctx, "format"));
            string? baseIri = RouteHelper.Query(ctx, "baseIri");
            if (format == null || !OntologyRouteSupport.ValidBaseIri(baseIri))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Use format=turtle or format=jsonld, and an absolute baseIri if one is given.").ConfigureAwait(false);
                return;
            }
            TenantOntology? ontology = await _Db.Ontologies.ReadAsync(version.TenantId, version.OntologyId, ctx.Token).ConfigureAwait(false);
            if (ontology == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Ontology not found.").ConfigureAwait(false);
                return;
            }
            string body = OntologyRdfExporter.Serialize(ontology, version, format.Value, baseIri);
            string extension = format == RdfFormatEnum.JsonLd ? "jsonld" : "ttl";
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = format == RdfFormatEnum.JsonLd ? "application/ld+json; charset=utf-8" : "text/turtle; charset=utf-8";
            ctx.Response.Headers.Add("Content-Disposition", "attachment; filename=\"ontology-" + version.Id + "." + extension + "\"");
            await ctx.Response.Send(body).ConfigureAwait(false);
        }

        private async Task ImportAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Write).ConfigureAwait(false)) return;
            OntologyVersion? version = await ReadOr404Async(ctx, rc).ConfigureAwait(false);
            if (version == null) return;
            RdfFormatEnum? format = OntologyRouteSupport.RdfFormat(RouteHelper.Query(ctx, "format"));
            string? modeText = RouteHelper.Query(ctx, "mode");
            TaxonomyImportModeEnum mode = TaxonomyImportModeEnum.Merge;
            if (format == null || (!String.IsNullOrWhiteSpace(modeText) && !Enum.TryParse(modeText, true, out mode)))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Use format=turtle or format=jsonld, and mode=merge or mode=replace.").ConfigureAwait(false);
                return;
            }
            OntologyResult<TaxonomyImportResult> result = await _Ontologies.ImportTaxonomyAsync(version, ctx.Request.DataAsString ?? String.Empty, format.Value, mode, ctx.Token).ConfigureAwait(false);
            if (!await OntologyRouteSupport.SendFailureAsync(ctx, result).ConfigureAwait(false)) return;
            await RouteHelper.SendJsonAsync(ctx, 200, result.Value).ConfigureAwait(false);
        }

        private async Task<OntologyVersion?> ReadOr404Async(HttpContextBase ctx, RequestContext rc)
        {
            OntologyVersion? version = await _Db.OntologyVersions.ReadAsync(rc.TenantId ?? String.Empty, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (version == null) await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Ontology version not found.").ConfigureAwait(false);
            return version;
        }

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc, OperationTypeEnum op)
        {
            if (await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Ontology, op, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        #endregion
    }
}
