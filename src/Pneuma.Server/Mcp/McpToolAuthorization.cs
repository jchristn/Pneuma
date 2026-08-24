namespace Pneuma.Server.Mcp
{
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;

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
