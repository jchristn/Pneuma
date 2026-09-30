namespace Pneuma.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Ontologies;

    /// <summary>An ontology operation with its items.</summary>
    public class OntologyOperationDetail
    {
        #region Public-Members

        /// <summary>The operation.</summary>
        public OntologyOperation Operation { get; set; } = null!;

        /// <summary>Its items (drift-check samples or retagged cells), in order.</summary>
        public List<OntologyOperationItem> Items { get; set; } = new List<OntologyOperationItem>();

        #endregion
    }
}
