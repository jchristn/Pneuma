namespace Pneuma.Core.Security
{
    using System.Collections.Generic;
    using Pneuma.Core.Enums;

    /// <summary>
    /// Definitions of the built-in, tenant-agnostic roles seeded at initialization.
    /// </summary>
    public static class BuiltInRoles
    {
        #region Role-Names

        /// <summary>Full control within a tenant.</summary>
        public const string TenantAdmin = "TenantAdmin";

        /// <summary>Manages security surfaces within a tenant.</summary>
        public const string SecurityAdmin = "SecurityAdmin";

        /// <summary>Read-only access to security surfaces.</summary>
        public const string Auditor = "Auditor";

        /// <summary>Resource-scoped full control over a graph.</summary>
        public const string GraphAdmin = "GraphAdmin";

        /// <summary>Read/write/delete on domain data resources.</summary>
        public const string Editor = "Editor";

        /// <summary>Read-only on domain data resources.</summary>
        public const string Viewer = "Viewer";

        /// <summary>Minimal authenticated presence.</summary>
        public const string TenantMember = "TenantMember";

        /// <summary>Template marker for tenant-defined custom roles.</summary>
        public const string Custom = "Custom";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Return the built-in role names in seed order.
        /// </summary>
        /// <returns>Role names.</returns>
        public static List<string> Names()
        {
            return new List<string>
            {
                TenantAdmin, SecurityAdmin, Auditor, GraphAdmin, Editor, Viewer, TenantMember, Custom
            };
        }

        /// <summary>
        /// Return the default permission specifications for a built-in role.
        /// </summary>
        /// <param name="roleName">Role name.</param>
        /// <returns>Permission specifications (empty for Custom or unknown roles).</returns>
        public static List<PermissionSpec> DefaultPermissions(string roleName)
        {
            List<PermissionSpec> specs = new List<PermissionSpec>();

            switch (roleName)
            {
                case TenantAdmin:
                    specs.Add(new PermissionSpec(
                        PermissionTypeEnum.Permit,
                        new List<ResourceTypeEnum> { ResourceTypeEnum.All },
                        new List<OperationTypeEnum> { OperationTypeEnum.All }));
                    break;

                case SecurityAdmin:
                    specs.Add(new PermissionSpec(
                        PermissionTypeEnum.Permit,
                        new List<ResourceTypeEnum>
                        {
                            ResourceTypeEnum.User, ResourceTypeEnum.Credential, ResourceTypeEnum.Session,
                            ResourceTypeEnum.Role, ResourceTypeEnum.Permission, ResourceTypeEnum.Assignment,
                            ResourceTypeEnum.Audit, ResourceTypeEnum.Tenant
                        },
                        new List<OperationTypeEnum> { OperationTypeEnum.Admin, OperationTypeEnum.Read }));
                    break;

                case Auditor:
                    specs.Add(new PermissionSpec(
                        PermissionTypeEnum.Permit,
                        new List<ResourceTypeEnum>
                        {
                            ResourceTypeEnum.User, ResourceTypeEnum.Credential, ResourceTypeEnum.Session,
                            ResourceTypeEnum.Role, ResourceTypeEnum.Permission, ResourceTypeEnum.Assignment,
                            ResourceTypeEnum.Audit
                        },
                        new List<OperationTypeEnum> { OperationTypeEnum.Read }));
                    break;

                case GraphAdmin:
                    specs.Add(new PermissionSpec(
                        PermissionTypeEnum.Permit,
                        new List<ResourceTypeEnum> { ResourceTypeEnum.GraphNode },
                        new List<OperationTypeEnum> { OperationTypeEnum.All }));
                    break;

                case Editor:
                    specs.Add(new PermissionSpec(
                        PermissionTypeEnum.Permit,
                        new List<ResourceTypeEnum>
                        {
                            ResourceTypeEnum.Subject, ResourceTypeEnum.IngestionJob, ResourceTypeEnum.GraphNode,
                            ResourceTypeEnum.SearchIndex, ResourceTypeEnum.Source, ResourceTypeEnum.Prompt
                        },
                        new List<OperationTypeEnum> { OperationTypeEnum.Read, OperationTypeEnum.Write }));
                    break;

                case Viewer:
                    specs.Add(new PermissionSpec(
                        PermissionTypeEnum.Permit,
                        new List<ResourceTypeEnum>
                        {
                            ResourceTypeEnum.Subject, ResourceTypeEnum.GraphNode, ResourceTypeEnum.SearchIndex,
                            ResourceTypeEnum.Source
                        },
                        new List<OperationTypeEnum> { OperationTypeEnum.Read }));
                    break;

                case TenantMember:
                    specs.Add(new PermissionSpec(
                        PermissionTypeEnum.Permit,
                        new List<ResourceTypeEnum> { ResourceTypeEnum.User },
                        new List<OperationTypeEnum> { OperationTypeEnum.Read }));
                    break;

                default:
                    break;
            }

            return specs;
        }

        #endregion
    }
}
