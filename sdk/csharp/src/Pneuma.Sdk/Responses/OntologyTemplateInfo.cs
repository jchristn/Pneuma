namespace Pneuma.Sdk.Responses
{
    using System;

    /// <summary>A built-in ontology template.</summary>
    public class OntologyTemplateInfo
    {
        /// <summary>Template name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Description.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Node type count.</summary>
        public int NodeTypeCount { get; set; } = 0;

        /// <summary>Edge type count.</summary>
        public int EdgeTypeCount { get; set; } = 0;

        /// <summary>Rule count.</summary>
        public int RuleCount { get; set; } = 0;
    }
}
