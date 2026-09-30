namespace Pneuma.Core.Requests
{
    using System;

    /// <summary>Request body to rename or re-describe a tenant ontology.</summary>
    public class OntologyUpdateRequest
    {
        #region Public-Members

        /// <summary>New name, unique within the tenant; null keeps the current name.</summary>
        public string? Name { get; set; } = null;

        /// <summary>New description; null clears it.</summary>
        public string? Description { get; set; } = null;

        #endregion
    }
}
