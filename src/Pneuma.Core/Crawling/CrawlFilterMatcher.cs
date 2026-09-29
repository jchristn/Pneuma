namespace Pneuma.Core.Crawling
{
    using System;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>Applies a <see cref="CrawlFilter"/> to enumerated objects.</summary>
    public static class CrawlFilterMatcher
    {
        #region Public-Methods

        /// <summary>Why an object is filtered out, or null when it is kept. The object limit is applied by the caller.</summary>
        /// <param name="filter">The filter.</param>
        /// <param name="obj">The object.</param>
        /// <returns>The reason, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static string? Reject(CrawlFilter filter, CrawledObject obj)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            if (obj == null) throw new ArgumentNullException(nameof(obj));

            if (filter.IncludePatterns.Count > 0 && !filter.IncludePatterns.Any(p => GlobMatch(p, obj.Key)))
                return "Matches no include pattern.";
            string? excluded = filter.ExcludePatterns.FirstOrDefault(p => GlobMatch(p, obj.Key));
            if (excluded != null) return "Matches exclude pattern " + excluded + ".";

            if (filter.AllowedContentTypes.Count > 0 && !String.IsNullOrEmpty(obj.ContentType))
            {
                string type = obj.ContentType.Split(';')[0].Trim();
                if (!filter.AllowedContentTypes.Any(t => String.Equals(t.Split(';')[0].Trim(), type, StringComparison.OrdinalIgnoreCase)))
                    return "Content type " + type + " is not allowed.";
            }

            if (obj.SizeBytes > 0)
            {
                if (filter.MinSizeBytes > 0 && obj.SizeBytes < filter.MinSizeBytes) return "Smaller than the minimum size.";
                if (filter.MaxSizeBytes > 0 && obj.SizeBytes > filter.MaxSizeBytes) return "Larger than the maximum size.";
            }
            return null;
        }

        /// <summary>Match a glob (<c>*</c> any run of characters, <c>?</c> one character) against a whole value, ignoring case.</summary>
        /// <param name="pattern">The glob.</param>
        /// <param name="value">The value.</param>
        /// <returns>True on a match.</returns>
        public static bool GlobMatch(string pattern, string value)
        {
            if (String.IsNullOrEmpty(pattern)) return false;
            StringBuilder sb = new StringBuilder("^");
            foreach (char c in pattern)
            {
                if (c == '*') sb.Append(".*");
                else if (c == '?') sb.Append('.');
                else sb.Append(Regex.Escape(c.ToString()));
            }
            sb.Append('$');
            return Regex.IsMatch(value ?? String.Empty, sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        }

        #endregion
    }
}
