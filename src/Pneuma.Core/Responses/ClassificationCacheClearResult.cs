namespace Pneuma.Core.Responses
{
    using System;

    /// <summary>The result of clearing classification cache entries.</summary>
    public class ClassificationCacheClearResult
    {
        #region Public-Members

        /// <summary>Entries removed.</summary>
        public int Removed { get; set; } = 0;

        #endregion
    }
}
