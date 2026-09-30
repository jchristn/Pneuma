namespace Pneuma.Core.Ontologies
{
    using System;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A tenant's governed ontology: a named model of node types, edge types, rules, and taxonomy concepts, kept as a
    /// series of numbered versions. Subjects pin one approved version.
    /// </summary>
    public class TenantOntology
    {
        #region Public-Members

        /// <summary>Ontology identifier (prefix "ont_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId
        {
            get { return _TenantId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(TenantId)); _TenantId = value; }
        }

        /// <summary>Display name, unique within the tenant. At most 256 characters.</summary>
        public string Name
        {
            get { return _Name; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name)); _Name = value; }
        }

        /// <summary>Optional description.</summary>
        public string? Description { get; set; } = null;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateOntologyId();
        private string _TenantId = String.Empty;
        private string _Name = String.Empty;

        #endregion
    }
}
