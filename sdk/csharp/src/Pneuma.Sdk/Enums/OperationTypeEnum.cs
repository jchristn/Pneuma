namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Operation types evaluated during authorization.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OperationTypeEnum
    {
        /// <summary>Wildcard: matches any operation.</summary>
        All,
        /// <summary>Create new resources.</summary>
        Create,
        /// <summary>Retrieve or view resources.</summary>
        Read,
        /// <summary>Mutating shorthand expanding to Create, Update, and Delete.</summary>
        Write,
        /// <summary>Modify existing resources.</summary>
        Update,
        /// <summary>Remove resources.</summary>
        Delete,
        /// <summary>Run or trigger operations.</summary>
        Execute,
        /// <summary>Manage security, tenancy, configuration, or other privileged surfaces.</summary>
        Admin
    }
}
