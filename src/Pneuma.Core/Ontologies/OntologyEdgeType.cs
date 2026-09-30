namespace Pneuma.Core.Ontologies
{
    using System;

    /// <summary>An edge (relationship) type declared by an ontology version (for example "CREATED_BY").</summary>
    public class OntologyEdgeType
    {
        #region Public-Members

        /// <summary>Type name, unique within the version (for example "CREATED_BY"). At most 128 characters.</summary>
        public string Name
        {
            get { return _Name; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name)); _Name = value.Trim(); }
        }

        /// <summary>What the type means, in natural language. The classifier sees it.</summary>
        public string? Description { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Name = String.Empty;

        #endregion
    }
}
