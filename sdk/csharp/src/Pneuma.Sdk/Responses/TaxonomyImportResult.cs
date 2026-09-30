namespace Pneuma.Sdk.Responses
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Models;

    /// <summary>The result of a taxonomy import.</summary>
    public class TaxonomyImportResult
    {
        /// <summary>Concepts added.</summary>
        public int Added { get; set; } = 0;

        /// <summary>Concepts updated.</summary>
        public int Updated { get; set; } = 0;

        /// <summary>Concepts removed.</summary>
        public int Removed { get; set; } = 0;

        /// <summary>Concepts after the import.</summary>
        public int Total { get; set; } = 0;

        /// <summary>What was skipped.</summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>The draft after the import.</summary>
        public OntologyVersion Version { get; set; } = new OntologyVersion();
    }
}
