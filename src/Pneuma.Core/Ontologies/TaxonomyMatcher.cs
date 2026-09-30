namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Finds taxonomy concept labels in text deterministically, without a model. Text and labels are split into words
    /// (runs of letters and digits), so a label only matches whole words. Matching ignores case unless a concept is
    /// case-sensitive, and at each position the longest label wins. Built once per ontology version and safe to share
    /// across threads (read-only after construction).
    /// </summary>
    public class TaxonomyMatcher
    {
        #region Public-Members

        /// <summary>Number of labels (preferred and alternative) the matcher knows.</summary>
        public int LabelCount { get { return _LabelCount; } }

        #endregion

        #region Private-Members

        private readonly TaxonomyTrieNode _Root = new TaxonomyTrieNode();
        private readonly int _LabelCount = 0;
        private readonly int _LongestLabelWords = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Build a matcher for a set of concepts.</summary>
        /// <param name="concepts">The concepts.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="concepts"/> is null.</exception>
        public TaxonomyMatcher(IEnumerable<OntologyConcept> concepts)
        {
            if (concepts == null) throw new ArgumentNullException(nameof(concepts));
            foreach (OntologyConcept concept in concepts)
            {
                List<string> labels = new List<string> { concept.PrefLabel };
                labels.AddRange(concept.AltLabels);
                foreach (string label in labels)
                {
                    List<string> words = Words(label);
                    if (words.Count == 0) continue;
                    TaxonomyTrieNode node = _Root;
                    foreach (string word in words)
                    {
                        string lower = word.ToLowerInvariant();
                        TaxonomyTrieNode? child;
                        if (!node.Children.TryGetValue(lower, out child))
                        {
                            child = new TaxonomyTrieNode();
                            node.Children[lower] = child;
                        }
                        node = child;
                    }
                    node.Labels.Add(new TaxonomyLabelEntry { ConceptKey = concept.Key, CaseSensitive = concept.CaseSensitive, ExactWords = String.Join(" ", words) });
                    _LabelCount++;
                    if (words.Count > _LongestLabelWords) _LongestLabelWords = words.Count;
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>Normalize a label for comparison: its words, lower-cased and joined by single spaces.</summary>
        /// <param name="label">The label.</param>
        /// <returns>The normalized label (empty when it has no words).</returns>
        public static string NormalizeLabel(string? label)
        {
            if (String.IsNullOrWhiteSpace(label)) return String.Empty;
            return String.Join(" ", Words(label!)).ToLowerInvariant();
        }

        /// <summary>Find every concept label in a text, longest first at each position, without overlaps.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The matches in text order (empty for empty text or an empty taxonomy).</returns>
        public List<TaxonomyMatch> Match(string? text)
        {
            List<TaxonomyMatch> matches = new List<TaxonomyMatch>();
            if (String.IsNullOrEmpty(text) || _LabelCount == 0) return matches;

            List<int> starts = new List<int>();
            List<int> ends = new List<int>();
            Tokenize(text!, starts, ends);

            int i = 0;
            while (i < starts.Count)
            {
                TaxonomyTrieNode node = _Root;
                TaxonomyLabelEntry? best = null;
                int bestEnd = -1;
                for (int j = i; j < starts.Count && j - i < _LongestLabelWords; j++)
                {
                    string word = text!.Substring(starts[j], ends[j] - starts[j]).ToLowerInvariant();
                    TaxonomyTrieNode? child;
                    if (!node.Children.TryGetValue(word, out child)) break;
                    node = child;
                    foreach (TaxonomyLabelEntry entry in node.Labels)
                    {
                        if (entry.CaseSensitive && !String.Equals(entry.ExactWords, SurfaceWords(text, starts, ends, i, j), StringComparison.Ordinal)) continue;
                        best = entry;
                        bestEnd = j;
                        break;
                    }
                }

                if (best == null)
                {
                    i++;
                    continue;
                }
                int start = starts[i];
                int length = ends[bestEnd] - start;
                matches.Add(new TaxonomyMatch { ConceptKey = best.ConceptKey, Text = text!.Substring(start, length), Start = start, Length = length });
                i = bestEnd + 1;
            }
            return matches;
        }

        #endregion

        #region Private-Methods

        private static List<string> Words(string value)
        {
            List<string> words = new List<string>();
            StringBuilder current = new StringBuilder();
            foreach (char c in value)
            {
                if (Char.IsLetterOrDigit(c))
                {
                    current.Append(c);
                    continue;
                }
                if (current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }
            }
            if (current.Length > 0) words.Add(current.ToString());
            return words;
        }

        private static void Tokenize(string text, List<int> starts, List<int> ends)
        {
            int start = -1;
            for (int i = 0; i < text.Length; i++)
            {
                if (Char.IsLetterOrDigit(text[i]))
                {
                    if (start < 0) start = i;
                    continue;
                }
                if (start >= 0)
                {
                    starts.Add(start);
                    ends.Add(i);
                    start = -1;
                }
            }
            if (start >= 0)
            {
                starts.Add(start);
                ends.Add(text.Length);
            }
        }

        private static string SurfaceWords(string text, List<int> starts, List<int> ends, int from, int to)
        {
            StringBuilder builder = new StringBuilder();
            for (int k = from; k <= to; k++)
            {
                if (builder.Length > 0) builder.Append(' ');
                builder.Append(text, starts[k], ends[k] - starts[k]);
            }
            return builder.ToString();
        }

        #endregion
    }
}
