namespace Pneuma.Core.Security
{
    using System.Collections.Generic;
    using Pneuma.Core.Enums;

    /// <summary>
    /// A declarative permission specification used to seed built-in roles.
    /// </summary>
    public class PermissionSpec
    {
        #region Public-Members

        /// <summary>Whether this specification permits or denies.</summary>
        public PermissionTypeEnum PermissionType { get; set; } = PermissionTypeEnum.Permit;

        /// <summary>Resource types the specification applies to.</summary>
        public List<ResourceTypeEnum> ResourceTypes { get; set; } = new List<ResourceTypeEnum>();

        /// <summary>Operation types the specification covers.</summary>
        public List<OperationTypeEnum> OperationTypes { get; set; } = new List<OperationTypeEnum>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate an empty specification.</summary>
        public PermissionSpec()
        {
        }

        /// <summary>Instantiate a specification.</summary>
        /// <param name="permissionType">Permit or deny.</param>
        /// <param name="resourceTypes">Resource types.</param>
        /// <param name="operationTypes">Operation types.</param>
        public PermissionSpec(PermissionTypeEnum permissionType, List<ResourceTypeEnum> resourceTypes, List<OperationTypeEnum> operationTypes)
        {
            PermissionType = permissionType;
            ResourceTypes = resourceTypes ?? new List<ResourceTypeEnum>();
            OperationTypes = operationTypes ?? new List<OperationTypeEnum>();
        }

        #endregion
    }
}
