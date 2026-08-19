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
                case "pneuma_enumerate_subjects":
                case "pneuma_get_subject":
                case "pneuma_enumerate_links":
                case "pneuma_get_link":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, OperationTypeEnum.Read, null, token).ConfigureAwait(false);
                case "pneuma_enumerate_jobs":
                case "pneuma_get_job":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.IngestionJob, OperationTypeEnum.Read, null, token).ConfigureAwait(false);
                case "pneuma_search":
                case "pneuma_get_node":
                case "pneuma_get_neighbors":
                case "pneuma_query":
                    return await authz.AuthorizeAsync(rc, ResourceTypeEnum.GraphNode, OperationTypeEnum.Read, null, token).ConfigureAwait(false);
                default:
                    return false;
            }
        }

        #endregion
    }
}
