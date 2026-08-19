namespace Pneuma.Core.Observability
{
    using System;

    /// <summary>
    /// Normalizes request and integration URL paths into low-cardinality route labels for metrics.
    /// Identifier-like path segments (all-digit, GUID, or PrettyId with an underscore) are collapsed
    /// to a literal <c>{id}</c> so that per-entity paths do not create unbounded label cardinality.
    /// </summary>
    public static class RouteNormalizer
    {
        #region Public-Members

        /// <summary>The literal token substituted for identifier-like path segments.</summary>
        public const string IdToken = "{id}";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Normalize a URL path by replacing identifier-like segments with <see cref="IdToken"/>.
        /// </summary>
        /// <param name="path">Path to normalize (query string, if any, should already be stripped).</param>
        /// <returns>The normalized, bounded-cardinality path. Returns "/" for null or empty input.</returns>
        public static string Normalize(string path)
        {
            if (String.IsNullOrEmpty(path)) return "/";

            int query = path.IndexOf('?');
            if (query >= 0) path = path.Substring(0, query);
            if (String.IsNullOrEmpty(path)) return "/";

            string[] segments = path.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i].Length == 0) continue;
                if (IsIdentifier(segments[i])) segments[i] = IdToken;
            }

            return String.Join("/", segments);
        }

        /// <summary>
        /// Extract and normalize the path portion of an absolute or relative URL, stripping scheme,
        /// host, and query.
        /// </summary>
        /// <param name="url">The URL whose path should be normalized.</param>
        /// <returns>The normalized path.</returns>
        public static string NormalizeUrl(string url)
        {
            if (String.IsNullOrEmpty(url)) return "/";

            Uri? parsed;
            if (Uri.TryCreate(url, UriKind.Absolute, out parsed) && parsed != null)
            {
                return Normalize(parsed.AbsolutePath);
            }

            return Normalize(url);
        }

        #endregion

        #region Private-Methods

        private static bool IsIdentifier(string segment)
        {
            if (IsAllDigits(segment)) return true;
            if (segment.IndexOf('_') >= 0) return true;

            Guid parsed;
            if (Guid.TryParse(segment, out parsed)) return true;

            return false;
        }

        private static bool IsAllDigits(string segment)
        {
            foreach (char c in segment)
            {
                if (c < '0' || c > '9') return false;
            }
            return true;
        }

        #endregion
    }
}
