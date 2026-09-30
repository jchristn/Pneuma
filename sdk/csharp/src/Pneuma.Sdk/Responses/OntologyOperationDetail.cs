namespace Pneuma.Sdk.Responses
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Models;

    /// <summary>An ontology operation with its items.</summary>
    public class OntologyOperationDetail
    {
        /// <summary>The operation.</summary>
        public OntologyOperation Operation { get; set; } = new OntologyOperation();

        /// <summary>Its items.</summary>
        public List<OntologyOperationItem> Items { get; set; } = new List<OntologyOperationItem>();
    }
}
