namespace Pneuma.Server.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ontologies;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using WatsonWebserver.Core;

    /// <summary>
    /// MCP tools for ontology governance: read ontologies and versions, see how a subject classifies, list its
    /// violations, and queue and read background operations. Authoring, approval, and pinning stay on the REST API and
    /// dashboards, where a person reviews them.
    /// </summary>
    public class McpOntologyTools
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly ClassificationCache _Cache;
        private readonly OntologySettings _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the tools.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="cache">Classification cache (entry counts).</param>
        /// <param name="settings">Ontology limits.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public McpOntologyTools(DatabaseDriverBase db, ClassificationCache cache, OntologySettings settings)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Public-Methods

        /// <summary>Enumerate the tenant's ontologies (<c>pneuma_enumerate_ontologies</c>) with their newest version's number and status.</summary>
        /// <param name="rc">Request context.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of summaries.</returns>
        public async Task<object> EnumerateOntologiesAsync(RequestContext rc, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            List<TenantOntology> ontologies = String.IsNullOrEmpty(tenantId) ? new List<TenantOntology>() : await _Db.Ontologies.EnumerateAsync(tenantId, token).ConfigureAwait(false);
            EnumerationResult<TenantOntology> page = EnumerationHelper.Paginate(ontologies, McpJsonRpc.QueryFromArguments(arguments), o => o.CreatedUtc, o => o.Name);
            List<object> summaries = new List<object>();
            foreach (TenantOntology ontology in page.Objects)
            {
                List<OntologyVersion> versions = await _Db.OntologyVersions.EnumerateAsync(tenantId, ontology.Id, token).ConfigureAwait(false);
                summaries.Add(new
                {
                    id = ontology.Id,
                    name = ontology.Name,
                    description = ontology.Description,
                    versions = versions.Select(v => new { id = v.Id, number = v.VersionNumber, status = v.Status.ToString() }).ToList()
                });
            }
            return McpJsonRpc.BuildPage(page.MaxResults, page.Skip, page.TotalRecords, page.RecordsRemaining, page.EndOfResults, summaries);
        }

        /// <summary>Fetch one ontology version with its contents (<c>pneuma_get_ontology_version</c>).</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The version, or null when an error response was already sent.</returns>
        public async Task<object?> GetVersionAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            OntologyVersion? version = await _Db.OntologyVersions.ReadAsync(rc.TenantId ?? String.Empty, McpJsonRpc.GetStringArgument(arguments, "id"), token).ConfigureAwait(false);
            if (version == null)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Ontology version not found.").ConfigureAwait(false);
                return null;
            }
            if (version.Status == OntologyVersionStatusEnum.Draft) version.Problems = OntologyVersionValidator.Problems(version);
            return version;
        }

        /// <summary>Show how a subject classifies (<c>pneuma_get_subject_ontology</c>).</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The view, or null when an error response was already sent.</returns>
        public async Task<object?> GetSubjectOntologyAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            Subject? subject = await SubjectOrErrorAsync(ctx, rc, id, arguments, token).ConfigureAwait(false);
            if (subject == null) return null;
            return await SubjectOntologyViewBuilder.BuildAsync(_Db, _Cache, subject, token).ConfigureAwait(false);
        }

        /// <summary>Enumerate a subject's violations as summaries (<c>pneuma_enumerate_ontology_violations</c>).</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page, or null when an error response was already sent.</returns>
        public async Task<object?> EnumerateViolationsAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            Subject? subject = await SubjectOrErrorAsync(ctx, rc, id, arguments, token).ConfigureAwait(false);
            if (subject == null) return null;
            string statusText = McpJsonRpc.GetStringArgument(arguments, "status");
            OntologyViolationStatusEnum status = OntologyViolationStatusEnum.Recorded;
            if (!String.IsNullOrEmpty(statusText) && !Enum.TryParse(statusText, true, out status))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: status must be Recorded, Quarantined, Released, or Dismissed.").ConfigureAwait(false);
                return null;
            }
            List<OntologyViolation> violations = await _Db.OntologyViolations.EnumerateAsync(subject.TenantId, subject.Id,
                String.IsNullOrEmpty(statusText) ? (OntologyViolationStatusEnum?)null : status, null, null, token).ConfigureAwait(false);
            EnumerationResult<OntologyViolation> page = EnumerationHelper.Paginate(violations, McpJsonRpc.QueryFromArguments(arguments), v => v.CreatedUtc, v => v.Id);
            List<object> summaries = page.Objects.Select(v => (object)new
            {
                id = v.Id,
                kind = v.ElementKind.ToString(),
                ruleType = v.RuleType?.ToString() ?? "Undeclared",
                action = v.Action.ToString(),
                status = v.Status.ToString(),
                message = v.Message,
                jobId = v.JobId,
                createdUtc = v.CreatedUtc
            }).ToList();
            return McpJsonRpc.BuildPage(page.MaxResults, page.Skip, page.TotalRecords, page.RecordsRemaining, page.EndOfResults, summaries);
        }

        /// <summary>Queue a background operation on a subject (<c>pneuma_start_ontology_operation</c>).</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The queued operation, or null when an error response was already sent.</returns>
        public async Task<object?> StartOperationAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            Subject? subject = await SubjectOrErrorAsync(ctx, rc, id, arguments, token).ConfigureAwait(false);
            if (subject == null) return null;
            OntologyOperationKindEnum kind;
            if (!Enum.TryParse(McpJsonRpc.GetStringArgument(arguments, "kind"), true, out kind))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: kind must be Validate, Retag, or DriftCheck.").ConfigureAwait(false);
                return null;
            }
            int sampleSize = McpJsonRpc.GetIntArgument(arguments, "sampleSize", 10);
            if (sampleSize < 1 || sampleSize > _Settings.MaxDriftSampleSize)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: sampleSize must be between 1 and " + _Settings.MaxDriftSampleSize + ".").ConfigureAwait(false);
                return null;
            }
            if (kind == OntologyOperationKindEnum.Validate && String.IsNullOrWhiteSpace(subject.OntologyVersionId))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "The subject has no pinned ontology version to validate against.").ConfigureAwait(false);
                return null;
            }
            if (await _Db.OntologyOperations.ExistsActiveAsync(subject.TenantId, subject.Id, kind, token).ConfigureAwait(false))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32009, "A " + kind + " operation is already queued or running for this subject.").ConfigureAwait(false);
                return null;
            }
            return await _Db.OntologyOperations.CreateAsync(new OntologyOperation
            {
                TenantId = subject.TenantId,
                SubjectId = subject.Id,
                Kind = kind,
                SampleSize = sampleSize,
                OntologyVersionId = subject.OntologyVersionId,
                RequestedByUserId = rc.UserId
            }, token).ConfigureAwait(false);
        }

        /// <summary>Fetch one operation with its items (<c>pneuma_get_ontology_operation</c>).</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The operation, or null when an error response was already sent.</returns>
        public async Task<object?> GetOperationAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            OntologyOperation? operation = await _Db.OntologyOperations.ReadAsync(tenantId, McpJsonRpc.GetStringArgument(arguments, "id"), token).ConfigureAwait(false);
            if (operation == null)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Operation not found.").ConfigureAwait(false);
                return null;
            }
            List<OntologyOperationItem> items = await _Db.OntologyOperations.EnumerateItemsAsync(tenantId, operation.Id, token).ConfigureAwait(false);
            return new OntologyOperationDetail { Operation = operation, Items = items };
        }

        #endregion

        #region Private-Methods

        private async Task<Subject?> SubjectOrErrorAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string subjectId = McpJsonRpc.GetStringArgument(arguments, "subjectId");
            Subject? subject = String.IsNullOrEmpty(subjectId) ? null : await _Db.Subjects.ReadAsync(rc.TenantId ?? String.Empty, subjectId, token).ConfigureAwait(false);
            if (subject == null) await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Subject not found.").ConfigureAwait(false);
            return subject;
        }

        #endregion
    }
}
