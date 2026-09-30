namespace Pneuma.Server.Mcp
{
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using Pneuma.Core.Ingestion.Pipeline;
    using Pneuma.Core.Ingestion.Deletion;
    using Pneuma.Core.Ingestion.Prompts;
    using Pneuma.Core.Observability;

    /// <summary>
    /// Maps a Pneuma tool name to the resource/operation it requires and authorizes it against the shared
    /// <see cref="AuthorizationService"/>. Kept in one place so the MCP transport path
    /// (<see cref="McpToolInvoker"/>) and the in-process agentic chat path (<see cref="PneumaToolExecutor"/>)
    /// authorize tools identically and cannot drift.
    /// </summary>
    public static class McpToolAuthorization
    {
        #region Public-Methods

        /// <summary>Authorize a tool call for the given request context.</summary>
        /// <param name="authz">Authorization service.</param>
        /// <param name="rc">Request context.</param>
        /// <param name="toolName">The tool name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the caller may invoke the tool.</returns>
        public static async Task<bool> AuthorizeAsync(AuthorizationService authz, RequestContext rc, string toolName, CancellationToken token)
        {
            switch (toolName)
            {
                case "pneuma_capabilities":
                    return rc.IsAuthenticated;
                case "pneuma_create_subject":
                case "pneuma_draft_subject":
                case "pneuma_create_subject_from_draft":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, OperationTypeEnum.Create, null, token).ConfigureAwait(false);
                case "pneuma_update_subject":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, OperationTypeEnum.Update, null, token).ConfigureAwait(false);
                case "pneuma_enumerate_subjects":
                case "pneuma_get_subject":
                case "pneuma_enumerate_links":
                case "pneuma_get_link":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, OperationTypeEnum.Read, null, token).ConfigureAwait(false);
                case "pneuma_enumerate_jobs":
                case "pneuma_get_job":
                case "pneuma_ingestion_summary":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.IngestionJob, OperationTypeEnum.Read, null, token).ConfigureAwait(false);
                case "pneuma_search":
                case "pneuma_get_node":
                case "pneuma_get_neighbors":
                case "pneuma_query":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.GraphNode, OperationTypeEnum.Read, null, token).ConfigureAwait(false);
                case "pneuma_get_history_turn":
                case "pneuma_enumerate_threads":
                case "pneuma_get_thread":
                case "pneuma_enumerate_feedback":
                case "pneuma_analytics":
                case "pneuma_enumerate_eval_runs":
                case "pneuma_get_eval_run":
                case "pneuma_enumerate_eval_facts":
                case "pneuma_distinct_labels":
                case "pneuma_distinct_tags":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, OperationTypeEnum.Read, null, token).ConfigureAwait(false);
                case "pneuma_set_link_refresh":
                case "pneuma_submit_content":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, OperationTypeEnum.Write, null, token).ConfigureAwait(false);
                case "pneuma_enumerate_crawl_plans":
                case "pneuma_get_crawl_plan":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.CrawlPlan, OperationTypeEnum.Read, null, token).ConfigureAwait(false);
                case "pneuma_create_crawl_plan":
                case "pneuma_update_crawl_plan":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.CrawlPlan, OperationTypeEnum.Write, null, token).ConfigureAwait(false);
                case "pneuma_test_crawl_plan":
                case "pneuma_preview_crawl_plan":
                case "pneuma_start_crawl_plan":
                case "pneuma_stop_crawl_plan":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.CrawlPlan, OperationTypeEnum.Execute, null, token).ConfigureAwait(false);
                case "pneuma_enumerate_crawl_operations":
                case "pneuma_get_crawl_operation":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.CrawlOperation, OperationTypeEnum.Read, null, token).ConfigureAwait(false);
                case "pneuma_enumerate_ontologies":
                case "pneuma_get_ontology_version":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.Ontology, OperationTypeEnum.Read, null, token).ConfigureAwait(false);
                case "pneuma_get_subject_ontology":
                case "pneuma_enumerate_ontology_violations":
                case "pneuma_get_ontology_operation":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, OperationTypeEnum.Read, null, token).ConfigureAwait(false);
                case "pneuma_start_ontology_operation":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, OperationTypeEnum.Update, null, token).ConfigureAwait(false);
                case "pneuma_create_eval_fact":
                case "pneuma_start_eval_run":
                case "pneuma_cancel_eval_run":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, OperationTypeEnum.Update, null, token).ConfigureAwait(false);
                case "pneuma_delete_thread":
                case "pneuma_delete_eval_fact":
                case "pneuma_delete_eval_run":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, OperationTypeEnum.Delete, null, token).ConfigureAwait(false);
                case "pneuma_enumerate_model_runner_health":
                case "pneuma_get_model_runner_health":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.ModelRunner, OperationTypeEnum.Read, null, token).ConfigureAwait(false);
                case "pneuma_enumerate_request_history":
                case "pneuma_get_request_history":
                case "pneuma_request_history_summary":
                case "pneuma_get_settings":
                    // Observability/config surfaces mirror their REST twins, which are restricted to the system administrator.
                    return rc.IsAdmin;
                default:
                    return false;
            }
        }

        #endregion
    }
}
