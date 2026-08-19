namespace Pneuma.Core.Requests
{
    using System;
    using Pneuma.Core.Enums;

    /// <summary>
    /// Query parameters for a paginated enumeration, following the platform enumeration pattern.
    /// </summary>
    public class EnumerationQuery
    {
        #region Public-Members

        /// <summary>Maximum number of records to return in this page (1..1000, default 100).</summary>
        public int MaxResults
        {
            get { return _MaxResults; }
            set { _MaxResults = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>Number of records to skip before this page (>= 0).</summary>
        public int Skip
        {
            get { return _Skip; }
            set { _Skip = value < 0 ? 0 : value; }
        }

        /// <summary>Result ordering.</summary>
        public EnumerationOrderEnum Ordering { get; set; } = EnumerationOrderEnum.CreatedDescending;

        /// <summary>Optional case-insensitive substring filter applied to the record's principal text field.</summary>
        public string? Search { get; set; } = null;

        #endregion

        #region Private-Members

        private int _MaxResults = 100;
        private int _Skip = 0;

        #endregion
    }
}
