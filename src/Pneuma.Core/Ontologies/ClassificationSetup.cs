namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;

    /// <summary>
    /// Everything a subject's classification calls share: the model runner, temperature, cache switch, the resolved
    /// system prompt (task, ontology, output contract), the pinned ontology version and its taxonomy matcher, and a
    /// provenance line that names each input so a result can be traced. Built by <see cref="ClassificationSetupBuilder"/>.
    /// </summary>
    public class ClassificationSetup
    {
        #region Public-Members

        /// <summary>The completion model runner.</summary>
        public ModelRunner Runner { get; set; } = null!;

        /// <summary>The runner's decrypted API key, if any.</summary>
        public string? ApiKey { get; set; } = null;

        /// <summary>Model temperature.</summary>
        public double Temperature { get; set; } = 0;

        /// <summary>Whether results are cached and reused.</summary>
        public bool CacheEnabled { get; set; } = true;

        /// <summary>The subject's pinned ontology version (with contents), or null when the ontology.definition prompt is used.</summary>
        public OntologyVersion? Version { get; set; } = null;

        /// <summary>The pinned version's ontology name, or null.</summary>
        public string? OntologyName { get; set; } = null;

        /// <summary>The ontology definition the classifier sees.</summary>
        public string OntologyDefinition { get; set; } = String.Empty;

        /// <summary>The full system prompt of every classification call.</summary>
        public string SystemPrompt { get; set; } = String.Empty;

        /// <summary>The <c>taxonomy.hint</c> prompt that introduces matched concepts.</summary>
        public string TaxonomyHintPrompt { get; set; } = String.Empty;

        /// <summary>The taxonomy matcher for the pinned version's concepts, or null when it has none.</summary>
        public TaxonomyMatcher? Matcher { get; set; } = null;

        /// <summary>A line that names each input of the classification (ontology, prompt hashes, model, temperature).</summary>
        public string Provenance { get; set; } = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>The concept with a key in the pinned version, or null.</summary>
        /// <param name="key">Concept key.</param>
        /// <returns>The concept, or null.</returns>
        public OntologyConcept? Concept(string key)
        {
            if (Version == null) return null;
            return Version.Concepts.FirstOrDefault(c => String.Equals(c.Key, key, StringComparison.Ordinal));
        }

        /// <summary>Phrase the concepts matched in a batch of cells as a hint for the classifier, or null when none matched.</summary>
        /// <param name="conceptKeys">Keys of the matched concepts.</param>
        /// <returns>The hint, or null.</returns>
        public string? Hint(IEnumerable<string> conceptKeys)
        {
            List<string> keys = (conceptKeys ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal).ToList();
            if (keys.Count == 0) return null;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(TaxonomyHintPrompt.Trim());
            foreach (string key in keys)
            {
                OntologyConcept? concept = Concept(key);
                if (concept == null) continue;
                sb.AppendLine("- " + concept.PrefLabel + " (" + concept.NodeType + ")");
            }
            return sb.ToString().TrimEnd();
        }

        #endregion
    }
}
