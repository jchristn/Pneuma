namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Models;

    /// <summary>
    /// Discovers the distinct retrieval facets — labels and tag key/value pairs — an operator has actually
    /// applied to a subject's content, aggregated from the subject's ingestion links (the authoritative source
    /// of operator-set labels/tags). Powers the Scope filter's suggestions and the <c>metadataFilter</c> an
    /// agent can build. Results are bounded caps, not an unbounded enumeration of link rows.
    /// </summary>
    public class FacetDiscoveryService
    {
        #region Private-Members

        private const int MaxLabels = 500;
        private const int MaxTagKeys = 200;
        private const int MaxValuesPerKey = 200;

        private readonly DatabaseDriverBase _Db;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the facet discovery service.</summary>
        /// <param name="db">Database driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="db"/> is null.</exception>
        public FacetDiscoveryService(DatabaseDriverBase db)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
        }

        #endregion

        #region Public-Methods

        /// <summary>Return the distinct labels applied across a subject's links, sorted, capped.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The distinct label values (at most a bounded cap).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tenantId"/> or <paramref name="subjectId"/> is null.</exception>
        public async Task<List<string>> DistinctLabelsAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            if (tenantId == null) throw new ArgumentNullException(nameof(tenantId));
            if (subjectId == null) throw new ArgumentNullException(nameof(subjectId));

            List<SubjectLink> links = await _Db.SubjectLinks.EnumerateBySubjectAsync(tenantId, subjectId, token).ConfigureAwait(false);
            SortedSet<string> labels = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SubjectLink link in links)
            {
                if (link.Labels == null) continue;
                foreach (string label in link.Labels)
                {
                    if (String.IsNullOrWhiteSpace(label)) continue;
                    labels.Add(label.Trim());
                    if (labels.Count >= MaxLabels) break;
                }
                if (labels.Count >= MaxLabels) break;
            }

            return new List<string>(labels);
        }

        /// <summary>Return the distinct tag keys and, per key, the distinct values applied across a subject's links.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A map of tag key to its distinct values (each bounded by a cap).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tenantId"/> or <paramref name="subjectId"/> is null.</exception>
        public async Task<Dictionary<string, List<string>>> DistinctTagsAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            if (tenantId == null) throw new ArgumentNullException(nameof(tenantId));
            if (subjectId == null) throw new ArgumentNullException(nameof(subjectId));

            List<SubjectLink> links = await _Db.SubjectLinks.EnumerateBySubjectAsync(tenantId, subjectId, token).ConfigureAwait(false);
            SortedDictionary<string, SortedSet<string>> tags = new SortedDictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (SubjectLink link in links)
            {
                if (link.Tags == null) continue;
                foreach (KeyValuePair<string, string> tag in link.Tags)
                {
                    if (String.IsNullOrWhiteSpace(tag.Key)) continue;
                    string key = tag.Key.Trim();
                    if (!tags.TryGetValue(key, out SortedSet<string>? values))
                    {
                        if (tags.Count >= MaxTagKeys) continue;
                        values = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                        tags[key] = values;
                    }
                    if (!String.IsNullOrWhiteSpace(tag.Value) && values.Count < MaxValuesPerKey) values.Add(tag.Value.Trim());
                }
            }

            Dictionary<string, List<string>> result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, SortedSet<string>> entry in tags) result[entry.Key] = new List<string>(entry.Value);
            return result;
        }

        #endregion
    }
}
