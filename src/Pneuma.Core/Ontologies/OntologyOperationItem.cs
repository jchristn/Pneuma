namespace Pneuma.Core.Ontologies
{
    using System;

    /// <summary>
    /// One finding of an ontology operation: a drift-check cell whose classification changed (or did not), or a
    /// retagged cell whose taxonomy links changed. Validation findings are stored as violations instead.
    /// </summary>
    public class OntologyOperationItem
    {
        #region Public-Members

        /// <summary>Operation identifier.</summary>
        public string OperationId { get; set; } = String.Empty;

        /// <summary>Position within the operation, from 0.</summary>
        public int Ordinal { get; set; } = 0;

        /// <summary>The graph node the item is about (a Cell node).</summary>
        public string? NodeId { get; set; } = null;

        /// <summary>The start of the cell's text, for display.</summary>
        public string? Excerpt { get; set; } = null;

        /// <summary>Whether the item is a finding (classification changed, or taxonomy links changed).</summary>
        public bool Changed { get; set; } = false;

        /// <summary>What was found, in words.</summary>
        public string? Detail { get; set; } = null;

        #endregion
    }
}
