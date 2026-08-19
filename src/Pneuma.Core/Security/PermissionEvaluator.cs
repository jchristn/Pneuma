namespace Pneuma.Core.Security
{
    using System.Collections.Generic;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;

    /// <summary>
    /// Evaluates a resolved set of permissions against a requested (resource, operation) pair.
    /// Explicit deny beats permit; no match is an implicit deny. Wildcards and the Write shorthand
    /// (Create + Update + Delete) are expanded during matching.
    /// </summary>
    public static class PermissionEvaluator
    {
        #region Public-Methods

        /// <summary>
        /// Evaluate the effective permission set for a requested resource and operation.
        /// </summary>
        /// <param name="permissions">Resolved permissions applicable to the principal and scope.</param>
        /// <param name="resourceType">Requested resource type.</param>
        /// <param name="operation">Requested operation.</param>
        /// <returns>The authorization result.</returns>
        public static AuthorizationResultEnum Evaluate(
            IEnumerable<Permission> permissions,
            ResourceTypeEnum resourceType,
            OperationTypeEnum operation)
        {
            bool anyPermit = false;

            foreach (Permission permission in permissions)
            {
                if (!permission.Active) continue;
                if (!MatchesResource(permission.ResourceTypes, resourceType)) continue;
                if (!MatchesOperation(permission.OperationTypes, operation)) continue;

                if (permission.PermissionType == PermissionTypeEnum.Deny)
                {
                    return AuthorizationResultEnum.DeniedExplicit;
                }
                anyPermit = true;
            }

            return anyPermit ? AuthorizationResultEnum.Permitted : AuthorizationResultEnum.DeniedImplicit;
        }

        /// <summary>
        /// Convenience overload returning a simple allow/deny decision.
        /// </summary>
        /// <param name="permissions">Resolved permissions.</param>
        /// <param name="resourceType">Requested resource type.</param>
        /// <param name="operation">Requested operation.</param>
        /// <returns>True only when the result is Permitted.</returns>
        public static bool IsPermitted(
            IEnumerable<Permission> permissions,
            ResourceTypeEnum resourceType,
            OperationTypeEnum operation)
        {
            return Evaluate(permissions, resourceType, operation) == AuthorizationResultEnum.Permitted;
        }

        #endregion

        #region Private-Methods

        private static bool MatchesResource(List<ResourceTypeEnum> resourceTypes, ResourceTypeEnum requested)
        {
            if (resourceTypes == null) return false;
            foreach (ResourceTypeEnum rt in resourceTypes)
            {
                if (rt == ResourceTypeEnum.All || rt == requested) return true;
            }
            return false;
        }

        private static bool MatchesOperation(List<OperationTypeEnum> operationTypes, OperationTypeEnum requested)
        {
            if (operationTypes == null) return false;
            foreach (OperationTypeEnum op in operationTypes)
            {
                if (op == OperationTypeEnum.All) return true;
                if (op == requested) return true;

                // Write is shorthand for Create + Update + Delete.
                if (op == OperationTypeEnum.Write &&
                    (requested == OperationTypeEnum.Create ||
                     requested == OperationTypeEnum.Update ||
                     requested == OperationTypeEnum.Delete))
                {
                    return true;
                }
            }
            return false;
        }

        #endregion
    }
}
