namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ontologies;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// How a subject uses an ontology: its pinned version and effective definition, pinning, violations and their review,
    /// background operations (validate, retag, drift check), graph export, and clearing its classification cache entries.
    /// Reads need Subject Read; changes need Subject Update (pinning also needs Ontology Read). Pin changes are audited.
    /// </summary>
    public class SubjectOntologyRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;
        private readonly OntologyService _Ontologies;
        private readonly OntologyViolationReviewer _Reviewer;
        private readonly ClassificationCache _Cache;
        private readonly IGraphRepositoryFactory _GraphFactory;
        private readonly OntologySettings _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="ontologies">Ontology service.</param>
        /// <param name="reviewer">Violation reviewer.</param>
        /// <param name="cache">Classification cache.</param>
        /// <param name="graphFactory">Per-tenant graph factory (export).</param>
        /// <param name="settings">Ontology limits.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public SubjectOntologyRoutes(DatabaseDriverBase db, AuthorizationService authz, OntologyService ontologies, OntologyViolationReviewer reviewer, ClassificationCache cache, IGraphRepositoryFactory graphFactory, OntologySettings settings)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Authz = authz ?? throw new ArgumentNullException(nameof(authz));
            _Ontologies = ontologies ?? throw new ArgumentNullException(nameof(ontologies));
            _Reviewer = reviewer ?? throw new ArgumentNullException(nameof(reviewer));
            _Cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _GraphFactory = graphFactory ?? throw new ArgumentNullException(nameof(graphFactory));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
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

            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/subjects/{id}/ontology", ViewAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Show how a subject classifies: its pinned ontology version, the definition the classifier sees, and its classification settings", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/subjects/{id}/ontology", PinAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Pin a subject to an approved ontology version, or unpin it (audited; queues a retag when the taxonomy changes)", tag).WithRequestBody(OpenApiBodies.Json<SubjectOntologyRequest>("The version to pin")));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/subjects/{id}/ontology-violations", ViolationsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List a subject's ontology rule violations (?status=, ?jobId=, ?operationId=)", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/ontology-violations/{id}/release", ReleaseAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Release a quarantined element into the subject's graph", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/ontology-violations/{id}/dismiss", DismissAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Dismiss a quarantined element (it stays out of the graph)", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/subjects/{id}/ontology-operations", OperationsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List a subject's ontology operations, newest first", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/subjects/{id}/ontology-operations", StartOperationAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Queue a background ontology operation: Validate, Retag, or DriftCheck", tag).WithRequestBody(OpenApiBodies.Json<OntologyOperationRequest>("The operation")));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/ontology-operations/{id}", OperationAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read an ontology operation with its items", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/subjects/{id}/graph/export", ExportAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Export a subject's graph (?format=json|jsonld|turtle|graphml, ?baseIri=)", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/subjects/{id}/classification-cache", ClearCacheAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Remove the classification cache entries a subject stored, so its next ingests call the model again", tag));
        }

        #endregion

        #region Private-Methods

        private async Task ViewAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.Subject, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            Subject? subject = await SubjectOr404Async(ctx, rc).ConfigureAwait(false);
            if (subject == null) return;
            await RouteHelper.SendJsonAsync(ctx, 200, await SubjectOntologyViewBuilder.BuildAsync(_Db, _Cache, subject, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PinAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.Subject, OperationTypeEnum.Update).ConfigureAwait(false)) return;
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.Ontology, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            Subject? subject = await SubjectOr404Async(ctx, rc).ConfigureAwait(false);
            if (subject == null) return;
            SubjectOntologyRequest request = RouteHelper.ReadBody<SubjectOntologyRequest>(ctx) ?? new SubjectOntologyRequest();
            OntologyResult<SubjectPinResult> result = await _Ontologies.PinAsync(subject, request.OntologyVersionId, ctx.Token).ConfigureAwait(false);
            if (!await OntologyRouteSupport.SendFailureAsync(ctx, result).ConfigureAwait(false)) return;
            SubjectPinResult pin = result.Value!;
            await OntologyRouteSupport.AuditAsync(_Db, ctx, rc, ResourceTypeEnum.Subject, OperationTypeEnum.Update, subject.Id,
                "Pinned ontology version changed from " + (pin.PreviousVersionId ?? "none") + " to " + (pin.Subject.OntologyVersionId ?? "none")).ConfigureAwait(false);

            if (request.Retag && pin.TaxonomyChanged && !await _Db.OntologyOperations.ExistsActiveAsync(subject.TenantId, subject.Id, OntologyOperationKindEnum.Retag, ctx.Token).ConfigureAwait(false))
            {
                await _Db.OntologyOperations.CreateAsync(new OntologyOperation
                {
                    TenantId = subject.TenantId,
                    SubjectId = subject.Id,
                    Kind = OntologyOperationKindEnum.Retag,
                    OntologyVersionId = pin.Subject.OntologyVersionId,
                    RequestedByUserId = rc.UserId
                }, ctx.Token).ConfigureAwait(false);
            }
            await RouteHelper.SendJsonAsync(ctx, 200, await SubjectOntologyViewBuilder.BuildAsync(_Db, _Cache, pin.Subject, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ViolationsAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.Subject, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            Subject? subject = await SubjectOr404Async(ctx, rc).ConfigureAwait(false);
            if (subject == null) return;
            string? statusText = RouteHelper.Query(ctx, "status");
            OntologyViolationStatusEnum status = OntologyViolationStatusEnum.Recorded;
            if (!String.IsNullOrWhiteSpace(statusText) && !Enum.TryParse(statusText, true, out status))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Unknown status '" + statusText + "'. Use Recorded, Quarantined, Released, or Dismissed.").ConfigureAwait(false);
                return;
            }
            List<OntologyViolation> violations = await _Db.OntologyViolations.EnumerateAsync(subject.TenantId, subject.Id,
                String.IsNullOrWhiteSpace(statusText) ? (OntologyViolationStatusEnum?)null : status, RouteHelper.Query(ctx, "jobId"), RouteHelper.Query(ctx, "operationId"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, EnumerationHelper.Paginate(violations, RouteHelper.ReadEnumerationQuery(ctx), v => v.CreatedUtc, v => v.Id)).ConfigureAwait(false);
        }

        private async Task ReleaseAsync(HttpContextBase ctx)
        {
            await ReviewAsync(ctx, true).ConfigureAwait(false);
        }

        private async Task DismissAsync(HttpContextBase ctx)
        {
            await ReviewAsync(ctx, false).ConfigureAwait(false);
        }

        private async Task ReviewAsync(HttpContextBase ctx, bool release)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.Subject, OperationTypeEnum.Update).ConfigureAwait(false)) return;
            OntologyViolation? violation = await _Db.OntologyViolations.ReadAsync(rc.TenantId ?? String.Empty, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (violation == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Violation not found.").ConfigureAwait(false);
                return;
            }
            OntologyResult<OntologyViolation> result = release
                ? await _Reviewer.ReleaseAsync(violation, rc.UserId, ctx.Token).ConfigureAwait(false)
                : await _Reviewer.DismissAsync(violation, rc.UserId, ctx.Token).ConfigureAwait(false);
            if (!await OntologyRouteSupport.SendFailureAsync(ctx, result).ConfigureAwait(false)) return;
            await RouteHelper.SendJsonAsync(ctx, 200, result.Value).ConfigureAwait(false);
        }

        private async Task OperationsAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.Subject, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            Subject? subject = await SubjectOr404Async(ctx, rc).ConfigureAwait(false);
            if (subject == null) return;
            List<OntologyOperation> operations = await _Db.OntologyOperations.EnumerateAsync(subject.TenantId, subject.Id, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, EnumerationHelper.Paginate(operations, RouteHelper.ReadEnumerationQuery(ctx), o => o.CreatedUtc, o => o.Id)).ConfigureAwait(false);
        }

        private async Task StartOperationAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.Subject, OperationTypeEnum.Update).ConfigureAwait(false)) return;
            Subject? subject = await SubjectOr404Async(ctx, rc).ConfigureAwait(false);
            if (subject == null) return;
            OntologyOperationRequest request = RouteHelper.ReadBody<OntologyOperationRequest>(ctx) ?? new OntologyOperationRequest();
            if (request.Kind == OntologyOperationKindEnum.Validate && String.IsNullOrWhiteSpace(subject.OntologyVersionId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Pin an approved ontology version to the subject before validating its graph.").ConfigureAwait(false);
                return;
            }
            if (request.SampleSize < 1 || request.SampleSize > _Settings.MaxDriftSampleSize)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "sampleSize must be between 1 and " + _Settings.MaxDriftSampleSize + " (Ontology.MaxDriftSampleSize).").ConfigureAwait(false);
                return;
            }
            if (await _Db.OntologyOperations.ExistsActiveAsync(subject.TenantId, subject.Id, request.Kind, ctx.Token).ConfigureAwait(false))
            {
                await RouteHelper.SendErrorAsync(ctx, 409, "Conflict", "A " + request.Kind + " operation is already queued or running for this subject.").ConfigureAwait(false);
                return;
            }
            OntologyOperation operation = await _Db.OntologyOperations.CreateAsync(new OntologyOperation
            {
                TenantId = subject.TenantId,
                SubjectId = subject.Id,
                Kind = request.Kind,
                SampleSize = request.SampleSize,
                OntologyVersionId = subject.OntologyVersionId,
                RequestedByUserId = rc.UserId
            }, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 202, operation).ConfigureAwait(false);
        }

        private async Task OperationAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.Subject, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            OntologyOperation? operation = await _Db.OntologyOperations.ReadAsync(tenantId, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (operation == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Operation not found.").ConfigureAwait(false);
                return;
            }
            List<OntologyOperationItem> items = await _Db.OntologyOperations.EnumerateItemsAsync(tenantId, operation.Id, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new OntologyOperationDetail { Operation = operation, Items = items }).ConfigureAwait(false);
        }

        private async Task ExportAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.Subject, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            Subject? subject = await SubjectOr404Async(ctx, rc).ConfigureAwait(false);
            if (subject == null) return;
            GraphExportFormatEnum? format = OntologyRouteSupport.GraphFormat(RouteHelper.Query(ctx, "format"));
            string? baseIri = RouteHelper.Query(ctx, "baseIri");
            if (format == null || !OntologyRouteSupport.ValidBaseIri(baseIri))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Use format=json, jsonld, turtle, or graphml, and an absolute baseIri if one is given.").ConfigureAwait(false);
                return;
            }
            IGraphRepository graph = await _GraphFactory.ForTenantAsync(subject.TenantId, ctx.Token).ConfigureAwait(false);
            SubjectGraph stored = await SubjectGraphReader.ReadAsync(graph, subject.Id, _Settings.MaxGraphNodes, ctx.Token).ConfigureAwait(false);
            string body = GraphExporter.Serialize(stored, subject, format.Value, baseIri);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = GraphExporter.ContentType(format.Value);
            ctx.Response.Headers.Add("Content-Disposition", "attachment; filename=\"" + (subject.UrlSlug ?? subject.Id) + "-graph." + GraphExporter.Extension(format.Value) + "\"");
            if (stored.Truncated) ctx.Response.Headers.Add("X-Pneuma-Truncated", "true");
            await ctx.Response.Send(body).ConfigureAwait(false);
        }

        private async Task ClearCacheAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.Subject, OperationTypeEnum.Update).ConfigureAwait(false)) return;
            Subject? subject = await SubjectOr404Async(ctx, rc).ConfigureAwait(false);
            if (subject == null) return;
            int removed = await _Cache.ClearAsync(subject.TenantId, subject.Id, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new ClassificationCacheClearResult { Removed = removed }).ConfigureAwait(false);
        }

        private async Task<Subject?> SubjectOr404Async(HttpContextBase ctx, RequestContext rc)
        {
            Subject? subject = await _Db.Subjects.ReadAsync(rc.TenantId ?? String.Empty, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (subject == null) await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Subject not found.").ConfigureAwait(false);
            return subject;
        }

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc, ResourceTypeEnum resource, OperationTypeEnum op)
        {
            if (await _Authz.AuthorizeAsync(rc, resource, op, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        #endregion
    }
}
