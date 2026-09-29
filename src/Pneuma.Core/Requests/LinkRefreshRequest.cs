namespace Pneuma.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// Sets the refresh interval of one link (<c>PUT /v1.0/links/{id}</c>) or several (<c>POST
    /// /v1.0/links/refresh-interval</c>, with <see cref="Ids"/>).
    /// </summary>
    public class LinkRefreshRequest
    {
        #region Public-Members

        /// <summary>Links to change (bulk route only).</summary>
        public List<string>? Ids { get; set; } = null;

        /// <summary>Minutes between refreshes: 0 turns refresh off, otherwise 60 to 525600. Ignored with <see cref="UseSubjectDefault"/>.</summary>
        public int? RefreshIntervalMinutes { get; set; } = null;

        /// <summary>True to clear the link's own interval so it follows the subject's default.</summary>
        public bool UseSubjectDefault { get; set; } = false;

        #endregion
    }
}
