namespace Pneuma.Server.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using WatsonWebserver.Core;

    /// <summary>
    /// MCP tools over the relational entities (subjects, ingestion jobs, content links). Each collection
    /// is exposed as an <c>enumerate</c> tool returning small summary projections paged through the
    /// <see cref="EnumerationQuery"/>/<see cref="EnumerationResult{T}"/> envelope, plus a <c>get</c> tool
    /// returning one full object by id. No collection is ever returned unbounded.
    /// </summary>
    public class McpEntityTools
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the entity tools.</summary>
        /// <param name="db">Database driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="db"/> is null.</exception>
        public McpEntityTools(DatabaseDriverBase db)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
        }

        #endregion

        #region Public-Methods

        /// <summary>Enumerate subject summaries for the caller's tenant, paged.</summary>
        /// <param name="rc">Request context.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An enumeration-result payload.</returns>
        public async Task<object> EnumerateSubjectsAsync(RequestContext rc, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            List<Subject> subjects = String.IsNullOrEmpty(tenantId)
                ? new List<Subject>()
                : await _Db.Subjects.EnumerateAsync(tenantId, token).ConfigureAwait(false);

            EnumerationQuery query = McpJsonRpc.QueryFromArguments(arguments);
            EnumerationResult<Subject> page = EnumerationHelper.Paginate(subjects, query, c => c.CreatedUtc, c => c.DisplayName);

            List<object> summaries = new List<object>();
            foreach (Subject subject in page.Objects)
            {
                summaries.Add(new { id = subject.Id, displayName = subject.DisplayName, type = subject.Type.ToString(), active = subject.Active });
            }

            return BuildPage(page.MaxResults, page.Skip, page.TotalRecords, page.RecordsRemaining, page.EndOfResults, summaries);
        }

        /// <summary>Fetch a single full subject by id.</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The subject, or null when an error response was already sent.</returns>
        public async Task<object?> GetSubjectAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string subjectId = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(subjectId))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'id' is required.").ConfigureAwait(false);
                return null;
            }

            string tenantId = rc.TenantId ?? String.Empty;
            Subject? subject = String.IsNullOrEmpty(tenantId)
                ? null
                : await _Db.Subjects.ReadAsync(tenantId, subjectId, token).ConfigureAwait(false);

            if (subject == null)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Subject not found.").ConfigureAwait(false);
                return null;
            }

            return subject;
        }

        /// <summary>Enumerate ingestion-job summaries for the caller's tenant, paged.</summary>
        /// <param name="rc">Request context.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An enumeration-result payload.</returns>
        public async Task<object> EnumerateJobsAsync(RequestContext rc, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            List<IngestionJob> jobs = String.IsNullOrEmpty(tenantId)
                ? new List<IngestionJob>()
                : await _Db.IngestionJobs.EnumerateAsync(tenantId, null, token).ConfigureAwait(false);

            EnumerationQuery query = McpJsonRpc.QueryFromArguments(arguments);
            EnumerationResult<IngestionJob> page = EnumerationHelper.Paginate(jobs, query, j => j.CreatedUtc, j => j.SourceUrl);

            List<object> summaries = new List<object>();
            foreach (IngestionJob job in page.Objects)
            {
                summaries.Add(new { id = job.Id, status = job.Status.ToString(), stage = job.Stage.ToString(), sourceUrl = job.SourceUrl, documentType = job.DocumentType });
            }

            return BuildPage(page.MaxResults, page.Skip, page.TotalRecords, page.RecordsRemaining, page.EndOfResults, summaries);
        }

        /// <summary>Fetch a single full ingestion job by id.</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The job, or null when an error response was already sent.</returns>
        public async Task<object?> GetJobAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string jobId = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(jobId))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'id' is required.").ConfigureAwait(false);
                return null;
            }

            string tenantId = rc.TenantId ?? String.Empty;
            IngestionJob? job = String.IsNullOrEmpty(tenantId)
                ? null
                : await _Db.IngestionJobs.ReadAsync(tenantId, jobId, token).ConfigureAwait(false);

            if (job == null)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Ingestion job not found.").ConfigureAwait(false);
                return null;
            }

            return job;
        }

        /// <summary>Enumerate content-link summaries for the caller's tenant, paged.</summary>
        /// <param name="rc">Request context.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An enumeration-result payload.</returns>
        public async Task<object> EnumerateLinksAsync(RequestContext rc, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            List<SubjectLink> links = String.IsNullOrEmpty(tenantId)
                ? new List<SubjectLink>()
                : await _Db.SubjectLinks.EnumerateAsync(tenantId, token).ConfigureAwait(false);

            EnumerationQuery query = McpJsonRpc.QueryFromArguments(arguments);
            EnumerationResult<SubjectLink> page = EnumerationHelper.Paginate(links, query, l => l.CreatedUtc, l => l.Url);

            List<object> summaries = new List<object>();
            foreach (SubjectLink link in page.Objects)
            {
                summaries.Add(new { id = link.Id, url = link.Url, title = link.Title, status = link.Status.ToString(), subjectId = link.SubjectId });
            }

            return BuildPage(page.MaxResults, page.Skip, page.TotalRecords, page.RecordsRemaining, page.EndOfResults, summaries);
        }

        /// <summary>Fetch a single full content link by id.</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The link, or null when an error response was already sent.</returns>
        public async Task<object?> GetLinkAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string linkId = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(linkId))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'id' is required.").ConfigureAwait(false);
                return null;
            }

            string tenantId = rc.TenantId ?? String.Empty;
            SubjectLink? link = String.IsNullOrEmpty(tenantId)
                ? null
                : await _Db.SubjectLinks.ReadAsync(tenantId, linkId, token).ConfigureAwait(false);

            if (link == null)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Content link not found.").ConfigureAwait(false);
                return null;
            }

            return link;
        }

        #endregion

        #region Private-Methods

        private static object BuildPage(int maxResults, int skip, long totalRecords, long recordsRemaining, bool endOfResults, List<object> objects)
        {
            return new
            {
                success = true,
                maxResults,
                skip,
                totalRecords,
                recordsRemaining,
                endOfResults,
                objects
            };
        }

        #endregion
    }
}
