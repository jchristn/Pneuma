namespace Pneuma.Sdk.Responses
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Enums;
    using Pneuma.Sdk.Models;

    /// <summary>A draft ontology cleaned up and rendered as the classifier will see it.</summary>
    public class WizardRenderedOntology
    {
        /// <summary>The cleaned-up draft.</summary>
        public WizardOntology Ontology { get; set; } = new WizardOntology();

        /// <summary>The rendered definition.</summary>
        public string Rendered { get; set; } = string.Empty;

        /// <summary>What the clean-up changed or dropped.</summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
