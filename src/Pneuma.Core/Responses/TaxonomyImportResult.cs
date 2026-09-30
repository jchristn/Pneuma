namespace Pneuma.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Ontologies;

    /// <summary>The result of importing a SKOS taxonomy into a draft version.</summary>
    public class TaxonomyImportResult
    {
        #region Public-Members

        /// <summary>Concepts added.</summary>
        public int Added { get; set; } = 0;

        /// <summary>Concepts updated (same key).</summary>
        public int Updated { get; set; } = 0;

        /// <summary>Concepts removed (Replace mode).</summary>
        public int Removed { get; set; } = 0;

        /// <summary>Concepts in the draft after the import.</summary>
        public int Total { get; set; } = 0;

        /// <summary>Anything skipped, in words.</summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>The draft version after the import.</summary>
        public OntologyVersion Version { get; set; } = null!;

        #endregion
    }
}
