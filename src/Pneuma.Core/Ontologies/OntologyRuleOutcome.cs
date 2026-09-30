namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;

    /// <summary>What applying an ontology version's rules to a candidate subgraph did.</summary>
    public class OntologyRuleOutcome
    {
        #region Public-Members

        /// <summary>
        /// One violation per element that broke a rule or used an undeclared type. The tenant, subject, job, and link are
        /// not set; the caller fills them in before storing.
        /// </summary>
        public List<OntologyViolation> Violations { get; } = new List<OntologyViolation>();

        /// <summary>Elements kept with a warning.</summary>
        public int Warned { get; set; } = 0;

        /// <summary>Elements left out of the graph.</summary>
        public int Dropped { get; set; } = 0;

        /// <summary>Elements held out of the graph for review.</summary>
        public int Quarantined { get; set; } = 0;

        /// <summary>Edges reversed to satisfy an endpoint rule.</summary>
        public int Reversed { get; set; } = 0;

        /// <summary>Edges left out because an endpoint node was dropped or quarantined.</summary>
        public int DroppedWithNode { get; set; } = 0;

        #endregion
    }
}
