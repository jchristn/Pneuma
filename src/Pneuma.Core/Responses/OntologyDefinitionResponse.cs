namespace Pneuma.Core.Responses
{
    using System;

    /// <summary>The ontology definition text the classifier sees for a version.</summary>
    public class OntologyDefinitionResponse
    {
        #region Public-Members

        /// <summary>Version identifier.</summary>
        public string VersionId { get; set; } = String.Empty;

        /// <summary>The rendered definition.</summary>
        public string Definition { get; set; } = String.Empty;

        #endregion
    }
}
