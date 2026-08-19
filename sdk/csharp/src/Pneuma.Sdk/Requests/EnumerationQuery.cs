namespace Pneuma.Sdk.Requests
{
    using System;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// Optional paging and filtering options for list (GET-all) endpoints. Only the properties that are
    /// set are emitted on the request query string.
    /// </summary>
    public class EnumerationQuery
    {
        /// <summary>Maximum number of records to return. The server clamps this to the range 1..1000.</summary>
        public int? MaxResults { get; set; } = null;

        /// <summary>Number of records to skip before the returned page.</summary>
        public int? Skip { get; set; } = null;

        /// <summary>Sort order. Accepts "asc" or "desc".</summary>
        public string? Order { get; set; } = null;

        /// <summary>Case-insensitive substring filter applied server-side.</summary>
        public string? Search { get; set; } = null;

        /// <summary>
        /// Build a URL-escaped query string from the set properties. Returns a string beginning with
        /// '?' when at least one property is set, or an empty string when none are set.
        /// </summary>
        /// <returns>The query string, or an empty string.</returns>
        public string ToQueryString()
        {
            StringBuilder sb = new StringBuilder();
            AppendPair(sb, "maxResults", MaxResults.HasValue ? MaxResults.Value.ToString(CultureInfo.InvariantCulture) : null);
            AppendPair(sb, "skip", Skip.HasValue ? Skip.Value.ToString(CultureInfo.InvariantCulture) : null);
            AppendPair(sb, "order", Order);
            AppendPair(sb, "search", Search);
            return sb.ToString();
        }

        private static void AppendPair(StringBuilder sb, string key, string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            sb.Append(sb.Length == 0 ? '?' : '&');
            sb.Append(Uri.EscapeDataString(key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value));
        }
    }
}
