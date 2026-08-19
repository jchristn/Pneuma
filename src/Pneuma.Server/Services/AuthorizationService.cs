namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Security;
    using SyslogLogging;

    /// <summary>
    /// Evaluates authorization for authenticated principals. Administrators and IsAdmin/IsTenantAdmin
    /// principals bypass RBAC (audited); everyone else is evaluated against their resolved permissions.
    /// Denials and bypasses are written to the audit stream.
    /// </summary>
    public class AuthorizationService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the authorization service.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        public AuthorizationService(DatabaseDriverBase db, LoggingModule logging)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _Db = db;
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Authorize a request for a resource type and operation, optionally targeting a specific resource.
        /// </summary>
        /// <param name="rc">Request context.</param>
        /// <param name="resourceType">Requested resource type.</param>
        /// <param name="operation">Requested operation.</param>
        /// <param name="resourceId">Target resource identifier, if resource-scoped.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when permitted.</returns>
        public async Task<bool> AuthorizeAsync(
            RequestContext rc,
            ResourceTypeEnum resourceType,
            OperationTypeEnum operation,
            string? resourceId = null,
            CancellationToken token = default)
        {
            if (rc == null) throw new ArgumentNullException(nameof(rc));

            rc.Authorization.ResourceType = resourceType;
            rc.Authorization.Operation = operation;

            if (!rc.IsAuthenticated)
            {
                rc.Authorization.Result = AuthorizationResultEnum.DeniedImplicit;
                PneumaMetrics.RecordAuthzDecision("deny");
                return false;
            }

            // Bypass: system administrator / IsAdmin.
            if (rc.IsAdmin || rc.Authentication.PrincipalType == PrincipalTypeEnum.Administrator)
            {
                rc.Authorization.Result = AuthorizationResultEnum.Permitted;
                rc.Authorization.BypassReason = "System administrator";
                PneumaMetrics.RecordAuthzDecision("permit");
                await AuditBypassAsync(rc, resourceType, operation, resourceId, token).ConfigureAwait(false);
                return true;
            }

            // Bypass: tenant administrator within their own tenant.
            if (rc.IsTenantAdmin)
            {
                rc.Authorization.Result = AuthorizationResultEnum.Permitted;
                rc.Authorization.BypassReason = "Tenant administrator";
                PneumaMetrics.RecordAuthzDecision("permit");
                await AuditBypassAsync(rc, resourceType, operation, resourceId, token).ConfigureAwait(false);
                return true;
            }

            if (String.IsNullOrEmpty(rc.TenantId))
            {
                rc.Authorization.Result = AuthorizationResultEnum.DeniedImplicit;
                PneumaMetrics.RecordAuthzDecision("deny");
                return false;
            }

            List<Permission> permissions = await ResolvePermissionsAsync(rc, resourceId, token).ConfigureAwait(false);
            AuthorizationResultEnum result = PermissionEvaluator.Evaluate(permissions, resourceType, operation);
            rc.Authorization.Result = result;

            if (result != AuthorizationResultEnum.Permitted)
            {
                rc.Authorization.DenialReason = "No matching permit for " + resourceType + "/" + operation;
                PneumaMetrics.RecordAuthzDenied();
                PneumaMetrics.RecordAuthzDecision("deny");
                await AuditDenialAsync(rc, resourceType, operation, resourceId, token).ConfigureAwait(false);
                return false;
            }

            PneumaMetrics.RecordAuthzDecision("permit");
            return true;
        }

        #endregion

        #region Private-Methods

        private async Task<List<Permission>> ResolvePermissionsAsync(RequestContext rc, string? resourceId, CancellationToken token)
        {
            List<Permission> permissions = new List<Permission>();
            string tenantId = rc.TenantId!;

            if (rc.Authentication.PrincipalType == PrincipalTypeEnum.Credential && !String.IsNullOrEmpty(rc.Authentication.PrincipalId))
            {
                List<CredentialScopeAssignment> credAssignments =
                    await _Db.CredentialScopeAssignments.EnumerateByCredentialAsync(tenantId, rc.Authentication.PrincipalId!, token).ConfigureAwait(false);
                foreach (CredentialScopeAssignment assignment in credAssignments)
                {
                    if (!assignment.Active) continue;
                    if (!ScopeApplies(assignment.ResourceScope, assignment.ResourceId, resourceId)) continue;
                    await AddRolePermissionsAsync(permissions, assignment.RoleId, assignment.RoleName, tenantId, token).ConfigureAwait(false);
                    AddDirectPermissions(permissions, assignment);
                }
                return permissions;
            }

            if (!String.IsNullOrEmpty(rc.UserId))
            {
                List<UserRoleAssignment> assignments =
                    await _Db.UserRoleAssignments.EnumerateByUserAsync(tenantId, rc.UserId!, token).ConfigureAwait(false);
                foreach (UserRoleAssignment assignment in assignments)
                {
                    if (!assignment.Active) continue;
                    if (!ScopeApplies(assignment.ResourceScope, assignment.ResourceId, resourceId)) continue;
                    await AddRolePermissionsAsync(permissions, assignment.RoleId, assignment.RoleName, tenantId, token).ConfigureAwait(false);
                }

                List<UserRoleMap> legacy = await _Db.UserRoleMaps.EnumerateByUserAsync(tenantId, rc.UserId!, token).ConfigureAwait(false);
                foreach (UserRoleMap map in legacy)
                {
                    if (!map.Active) continue;
                    await AddRolePermissionsAsync(permissions, map.RoleId, null, tenantId, token).ConfigureAwait(false);
                }
            }

            return permissions;
        }

        private async Task AddRolePermissionsAsync(List<Permission> permissions, string? roleId, string? roleName, string tenantId, CancellationToken token)
        {
            UserRole? role = null;
            if (!String.IsNullOrEmpty(roleId)) role = await _Db.Roles.ReadAsync(roleId!, token).ConfigureAwait(false);
            if (role == null && !String.IsNullOrEmpty(roleName)) role = await _Db.Roles.ReadByNameAsync(tenantId, roleName!, token).ConfigureAwait(false);
            if (role == null || !role.Active) return;

            List<Permission> rolePermissions = await _Db.Permissions.EnumerateByRoleAsync(role.Id, token).ConfigureAwait(false);
            permissions.AddRange(rolePermissions);
        }

        private static void AddDirectPermissions(List<Permission> permissions, CredentialScopeAssignment assignment)
        {
            if (assignment.ResourceTypes.Count == 0 || assignment.Permissions.Count == 0) return;
            permissions.Add(new Permission
            {
                PermissionType = PermissionTypeEnum.Permit,
                ResourceTypes = assignment.ResourceTypes,
                OperationTypes = assignment.Permissions,
                Active = true
            });
        }

        private static bool ScopeApplies(ResourceScopeEnum scope, string? assignmentResourceId, string? requestedResourceId)
        {
            if (scope == ResourceScopeEnum.Tenant) return true;
            if (String.IsNullOrEmpty(requestedResourceId)) return false;
            return String.Equals(assignmentResourceId, requestedResourceId, StringComparison.Ordinal);
        }

        private async Task AuditBypassAsync(RequestContext rc, ResourceTypeEnum resourceType, OperationTypeEnum operation, string? resourceId, CancellationToken token)
        {
            try
            {
                AuditRecord record = new AuditRecord
                {
                    EventType = AuditEventTypeEnum.AuthorizationBypass,
                    TenantId = rc.TenantId,
                    UserId = rc.UserId,
                    CredentialId = rc.Authentication.Credential?.Id,
                    SessionId = rc.Authentication.SessionId,
                    ResourceId = resourceId,
                    PrincipalType = rc.Authentication.PrincipalType,
                    AuthScheme = rc.Authentication.Scheme,
                    RequestId = rc.RequestId,
                    HttpMethod = rc.HttpMethod,
                    UrlPath = rc.Url,
                    SourceIp = rc.SourceIp,
                    AuthorizationResult = rc.Authorization.Result,
                    RequiredResourceType = resourceType,
                    RequiredOperation = operation,
                    BypassReason = rc.Authorization.BypassReason
                };
                await _Db.Audit.CreateAsync(record, token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Logging.Warn("[AuthorizationService] failed to write bypass audit: " + e.Message);
            }
        }

        private async Task AuditDenialAsync(RequestContext rc, ResourceTypeEnum resourceType, OperationTypeEnum operation, string? resourceId, CancellationToken token)
        {
            try
            {
                AuditRecord record = new AuditRecord
                {
                    EventType = AuditEventTypeEnum.AuthorizationDenied,
                    TenantId = rc.TenantId,
                    UserId = rc.UserId,
                    CredentialId = rc.Authentication.Credential?.Id,
                    SessionId = rc.Authentication.SessionId,
                    ResourceId = resourceId,
                    PrincipalType = rc.Authentication.PrincipalType,
                    AuthScheme = rc.Authentication.Scheme,
                    RequestId = rc.RequestId,
                    HttpMethod = rc.HttpMethod,
                    UrlPath = rc.Url,
                    SourceIp = rc.SourceIp,
                    AuthorizationResult = rc.Authorization.Result,
                    RequiredResourceType = resourceType,
                    RequiredOperation = operation,
                    DenialReason = rc.Authorization.DenialReason
                };
                await _Db.Audit.CreateAsync(record, token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Logging.Warn("[AuthorizationService] failed to write denial audit: " + e.Message);
            }
        }

        #endregion
    }
}
