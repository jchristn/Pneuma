namespace Pneuma.Core.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;

    /// <summary>
    /// Produces a paginated <see cref="EnumerationResult{T}"/> from a full in-memory result set,
    /// applying optional search, ordering, skip, and page-size.
    /// </summary>
    public static class EnumerationHelper
    {
        /// <summary>
        /// Paginate a full result set into an enumeration result.
        /// </summary>
        /// <typeparam name="T">Record type.</typeparam>
        /// <param name="all">The full, unpaged result set.</param>
        /// <param name="query">Enumeration query (max results, skip, ordering, search).</param>
        /// <param name="createdSelector">Selects the record's creation timestamp for ordering.</param>
        /// <param name="searchSelector">Optional selector for the text a search filter matches against.</param>
        /// <returns>A paginated enumeration result.</returns>
        public static EnumerationResult<T> Paginate<T>(
            IReadOnlyList<T> all,
            EnumerationQuery query,
            Func<T, DateTime> createdSelector,
            Func<T, string?>? searchSelector = null)
        {
            if (all == null) all = new List<T>();
            if (query == null) query = new EnumerationQuery();

            IEnumerable<T> filtered = all;

            if (!String.IsNullOrWhiteSpace(query.Search) && searchSelector != null)
            {
                string needle = query.Search.Trim();
                filtered = filtered.Where(item =>
                {
                    string? hay = searchSelector(item);
                    return hay != null && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
                });
            }

            filtered = query.Ordering == EnumerationOrderEnum.CreatedAscending
                ? filtered.OrderBy(createdSelector)
                : filtered.OrderByDescending(createdSelector);

            List<T> ordered = filtered.ToList();
            long total = ordered.Count;

            List<T> page = ordered.Skip(query.Skip).Take(query.MaxResults).ToList();
            long consumed = (long)query.Skip + page.Count;
            long remaining = total - consumed;
            if (remaining < 0) remaining = 0;

            return new EnumerationResult<T>
            {
                Success = true,
                MaxResults = query.MaxResults,
                Skip = query.Skip,
                TotalRecords = total,
                RecordsRemaining = remaining,
                EndOfResults = consumed >= total,
                Objects = page
            };
        }
    }
}
