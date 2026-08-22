namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Core.Storage;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Subject content-link routes. Submitting a link creates the link and enqueues an ingestion job.
    /// </summary>
    public class SubjectLinkRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;
        private readonly IArtifactStore _Artifacts;
        private readonly CascadeDeletionService _Cascade;
        private readonly ICollectionStore _Collections;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate subject link routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="artifacts">Per-stage S3 artifact store used by the artifact-view endpoints.</param>
        /// <param name="cascade">Cascade deletion service, used to remove a link's subordinate objects.</param>
        /// <param name="collections">Collection store, used to validate the target collection at submit time.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public SubjectLinkRoutes(DatabaseDriverBase db, AuthorizationService authz, IArtifactStore artifacts, CascadeDeletionService cascade, ICollectionStore collections)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            if (artifacts == null) throw new ArgumentNullException(nameof(artifacts));
            if (cascade == null) throw new ArgumentNullException(nameof(cascade));
            if (collections == null) throw new ArgumentNullException(nameof(collections));
            _Db = db;
            _Authz = authz;
            _Artifacts = artifacts;
            _Cascade = cascade;
            _Collections = collections;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/subjects/{subjectId}/links", SubmitAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Submit a content link for ingestion", "Subjects"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/subjects/{subjectId}/links/bulk", BulkSubmitAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Submit multiple content links for ingestion", "Subjects"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/subjects/{subjectId}/links", ListBySubjectAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List a subject's links", "Subjects"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/links", ListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List content links", "Subjects"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/links/{id}", ReadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read a content link", "Subjects"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/links/{id}/log", LogAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Get a content link's per-step ingestion log", "Subjects"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/links/{id}/source", SourceAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("View a content link's stored source asset", "Subjects"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/links/{id}/atoms", AtomsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("View a content link's stored atoms", "Subjects"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/links/{id}/chunks", ChunksAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("View a content link's stored chunks", "Subjects"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/links/{id}/vectors", VectorsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("View a content link's stored embedding vectors", "Subjects"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/links/{id}/subgraph", SubgraphAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("View a content link's stored candidate subgraph", "Subjects"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/links/{id}", DeleteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete a content link", "Subjects"));
        }

        #endregion

        #region Private-Methods

        /// <summary>Trim, drop blanks, and de-duplicate submitted labels (order preserved).</summary>
        /// <param name="labels">Submitted labels, possibly null.</param>
        /// <returns>The normalized labels.</returns>
        private static List<string> NormalizeLabels(List<string>? labels)
        {
            List<string> result = new List<string>();
            if (labels == null) return result;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string label in labels)
            {
                if (String.IsNullOrWhiteSpace(label)) continue;
                string trimmed = label.Trim();
                if (seen.Add(trimmed)) result.Add(trimmed);
            }
            return result;
        }

        /// <summary>Trim keys/values and drop entries with a blank key (last value wins on a duplicate key).</summary>
        /// <param name="tags">Submitted tags, possibly null.</param>
        /// <returns>The normalized tags.</returns>
        private static Dictionary<string, string> NormalizeTags(Dictionary<string, string>? tags)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (tags == null) return result;
            foreach (KeyValuePair<string, string> tag in tags)
            {
                if (String.IsNullOrWhiteSpace(tag.Key)) continue;
                result[tag.Key.Trim()] = (tag.Value ?? String.Empty).Trim();
            }
            return result;
        }

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc, OperationTypeEnum op)
        {
            if (await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, op, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        /// <summary>
        /// Ensure the subject owns the configuration ingestion needs (embedding + inference models and an
        /// existing collection). Sends a 400 describing what to set and returns false when it is not configured.
        /// </summary>
        private async Task<bool> RequireSubjectConfiguredAsync(HttpContextBase ctx, string tenantId, Subject subject)
        {
            if (String.IsNullOrWhiteSpace(subject.EmbeddingModel) || String.IsNullOrWhiteSpace(subject.InferenceModel))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "This subject has no embedding and inference model configured. Set them on the subject before submitting links.").ConfigureAwait(false);
                return false;
            }
            if (String.IsNullOrWhiteSpace(subject.Collection))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "This subject has no collection configured. Set it on the subject before submitting links.").ConfigureAwait(false);
                return false;
            }
            if (!await _Collections.CollectionExistsAsync(tenantId, subject.Collection!, ctx.Token).ConfigureAwait(false))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "This subject's configured collection no longer exists. Update the subject's collection.").ConfigureAwait(false);
                return false;
            }
            return true;
        }

        private async Task SubmitAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Write).ConfigureAwait(false)) return;

            string tenantId = rc.TenantId ?? String.Empty;
            string subjectId = RouteHelper.Param(ctx, "subjectId");
            if (String.IsNullOrEmpty(tenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return;
            }

            Subject? subject = await _Db.Subjects.ReadAsync(tenantId, subjectId, ctx.Token).ConfigureAwait(false);
            if (subject == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Subject not found.").ConfigureAwait(false);
                return;
            }

            SubmitLinkRequest? request = RouteHelper.ReadBody<SubmitLinkRequest>(ctx);
            if (request == null || String.IsNullOrWhiteSpace(request.Url))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A URL is required.").ConfigureAwait(false);
                return;
            }

            // Models and collection are owned by the subject; the link body carries the URL/title plus optional
            // labels/tags that are stamped onto every chunk (RecallDB) and the link's source graph node (LiteGraph).
            if (!await RequireSubjectConfiguredAsync(ctx, tenantId, subject).ConfigureAwait(false)) return;

            List<string> labels = NormalizeLabels(request.Labels);
            Dictionary<string, string> tags = NormalizeTags(request.Tags);

            SubjectLink link = new SubjectLink
            {
                TenantId = tenantId,
                SubjectId = subjectId,
                Url = request.Url,
                Title = request.Title,
                Labels = labels,
                Tags = tags,
                SubmittedByUserId = rc.UserId,
                Status = SubjectLinkStatusEnum.Submitted
            };
            IngestionJob job = new IngestionJob
            {
                TenantId = tenantId,
                SubjectId = subjectId,
                LinkId = link.Id,
                SourceUrl = link.Url,
                Labels = labels,
                Tags = tags,
                Status = IngestionStatusEnum.Queued,
                Stage = IngestionStageEnum.Pending,
                EmbeddingEndpointId = subject.EmbeddingModel,
                CompletionEndpointId = subject.InferenceModel,
                CollectionId = subject.Collection
            };
            SubjectLink createdLink = await _Db.SubjectLinks.CreateWithJobAsync(link, job, ctx.Token).ConfigureAwait(false);

            await RouteHelper.SendJsonAsync(ctx, 201, createdLink).ConfigureAwait(false);
        }

        private async Task BulkSubmitAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Write).ConfigureAwait(false)) return;

            string tenantId = rc.TenantId ?? String.Empty;
            string subjectId = RouteHelper.Param(ctx, "subjectId");
            if (String.IsNullOrEmpty(tenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return;
            }

            Subject? subject = await _Db.Subjects.ReadAsync(tenantId, subjectId, ctx.Token).ConfigureAwait(false);
            if (subject == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Subject not found.").ConfigureAwait(false);
                return;
            }

            BulkSubmitLinkRequest? request = RouteHelper.ReadBody<BulkSubmitLinkRequest>(ctx);
            if (request == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A request body with URLs is required.").ConfigureAwait(false);
                return;
            }

            // Models and collection are owned by the subject; the bulk body carries URLs plus one optional set of
            // labels/tags applied identically to every URL in the batch.
            if (!await RequireSubjectConfiguredAsync(ctx, tenantId, subject).ConfigureAwait(false)) return;

            List<string> urls = new List<string>();
            if (request.Urls != null)
            {
                foreach (string url in request.Urls)
                {
                    if (!String.IsNullOrWhiteSpace(url)) urls.Add(url.Trim());
                }
            }

            if (urls.Count == 0)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "At least one URL is required.").ConfigureAwait(false);
                return;
            }

            List<string> labels = NormalizeLabels(request.Labels);
            Dictionary<string, string> tags = NormalizeTags(request.Tags);

            List<SubjectLink> createdLinks = new List<SubjectLink>();
            foreach (string url in urls)
            {
                // Each link gets its own copy of the batch labels/tags so per-link edits never alias one another.
                SubjectLink link = new SubjectLink
                {
                    TenantId = tenantId,
                    SubjectId = subjectId,
                    Url = url,
                    Labels = new List<string>(labels),
                    Tags = new Dictionary<string, string>(tags),
                    SubmittedByUserId = rc.UserId,
                    Status = SubjectLinkStatusEnum.Submitted
                };
                IngestionJob job = new IngestionJob
                {
                    TenantId = tenantId,
                    SubjectId = subjectId,
                    LinkId = link.Id,
                    SourceUrl = link.Url,
                    Labels = new List<string>(labels),
                    Tags = new Dictionary<string, string>(tags),
                    Status = IngestionStatusEnum.Queued,
                    Stage = IngestionStageEnum.Pending,
                    EmbeddingEndpointId = subject.EmbeddingModel,
                    CompletionEndpointId = subject.InferenceModel,
                    CollectionId = subject.Collection
                };
                SubjectLink createdLink = await _Db.SubjectLinks.CreateWithJobAsync(link, job, ctx.Token).ConfigureAwait(false);

                createdLinks.Add(createdLink);
            }

            BulkSubmitLinkResponse response = new BulkSubmitLinkResponse
            {
                Created = createdLinks.Count,
                Links = createdLinks
            };
            await RouteHelper.SendJsonAsync(ctx, 201, response).ConfigureAwait(false);
        }

        private async Task ListBySubjectAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            string subjectId = RouteHelper.Param(ctx, "subjectId");
            List<SubjectLink> links = await _Db.SubjectLinks.EnumerateBySubjectAsync(tenantId, subjectId, ctx.Token).ConfigureAwait(false);
            EnumerationResult<SubjectLink> result = EnumerationHelper.Paginate(links, RouteHelper.ReadEnumerationQuery(ctx), l => l.CreatedUtc, l => l.Url);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task ListAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            List<SubjectLink> links = await _Db.SubjectLinks.EnumerateAsync(tenantId, ctx.Token).ConfigureAwait(false);
            EnumerationResult<SubjectLink> result = EnumerationHelper.Paginate(links, RouteHelper.ReadEnumerationQuery(ctx), l => l.CreatedUtc, l => l.Url);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            SubjectLink? link = await _Db.SubjectLinks.ReadAsync(tenantId, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (link == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Link not found.").ConfigureAwait(false);
                return;
            }
            await RouteHelper.SendJsonAsync(ctx, 200, link).ConfigureAwait(false);
        }

        private async Task LogAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            string linkId = RouteHelper.Param(ctx, "id");

            SubjectLink? link = await _Db.SubjectLinks.ReadAsync(tenantId, linkId, ctx.Token).ConfigureAwait(false);
            if (link == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Link not found.").ConfigureAwait(false);
                return;
            }

            List<IngestionJob> jobs = await _Db.IngestionJobs.EnumerateByLinkAsync(tenantId, linkId, ctx.Token).ConfigureAwait(false);
            List<IngestionJobDetail> log = new List<IngestionJobDetail>();
            foreach (IngestionJob job in jobs)
            {
                List<IngestionJobEvent> events = await _Db.IngestionJobEvents.EnumerateByJobAsync(tenantId, job.Id, ctx.Token).ConfigureAwait(false);
                log.Add(new IngestionJobDetail { Job = job, Events = events });
            }
            await RouteHelper.SendJsonAsync(ctx, 200, log).ConfigureAwait(false);
        }

        private async Task SourceAsync(HttpContextBase ctx)
        {
            await SendArtifactAsync(ctx, (linkId, token) => _Artifacts.GetSourceAsync(linkId, token)).ConfigureAwait(false);
        }

        private async Task AtomsAsync(HttpContextBase ctx)
        {
            await SendArtifactAsync(ctx, (linkId, token) => _Artifacts.GetAtomsAsync(linkId, token)).ConfigureAwait(false);
        }

        private async Task ChunksAsync(HttpContextBase ctx)
        {
            await SendArtifactAsync(ctx, (linkId, token) => _Artifacts.GetChunksAsync(linkId, token)).ConfigureAwait(false);
        }

        private async Task VectorsAsync(HttpContextBase ctx)
        {
            await SendArtifactAsync(ctx, (linkId, token) => _Artifacts.GetEmbeddingsAsync(linkId, token)).ConfigureAwait(false);
        }

        private async Task SubgraphAsync(HttpContextBase ctx)
        {
            await SendArtifactAsync(ctx, (linkId, token) => _Artifacts.GetSubgraphAsync(linkId, token)).ConfigureAwait(false);
        }

        private async Task SendArtifactAsync(HttpContextBase ctx, Func<string, System.Threading.CancellationToken, Task<S3ArtifactResult?>> fetch)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            string linkId = RouteHelper.Param(ctx, "id");

            SubjectLink? link = await _Db.SubjectLinks.ReadAsync(tenantId, linkId, ctx.Token).ConfigureAwait(false);
            if (link == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Link not found.").ConfigureAwait(false);
                return;
            }

            S3ArtifactResult? artifact = await fetch(linkId, ctx.Token).ConfigureAwait(false);
            if (artifact == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Artifact not found.").ConfigureAwait(false);
                return;
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = artifact.ContentType;
            await ctx.Response.Send(artifact.Data, ctx.Token).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Delete).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            string linkId = RouteHelper.Param(ctx, "id");

            SubjectLink? link = await _Db.SubjectLinks.ReadAsync(tenantId, linkId, ctx.Token).ConfigureAwait(false);
            if (link == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Link not found.").ConfigureAwait(false);
                return;
            }

            // Cascade: remove every downstream object this link produced (jobs, processing logs, S3
            // pipeline artifacts, raw blobs, graph nodes/edges, and index documents) before the link row.
            await _Cascade.DeleteLinkCascadeAsync(tenantId, linkId, ctx.Token).ConfigureAwait(false);

            ctx.Response.StatusCode = 204;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        #endregion
    }
}
