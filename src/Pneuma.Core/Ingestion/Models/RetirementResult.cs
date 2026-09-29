namespace Pneuma.Core.Ingestion.Models
{
    using System.Collections.Generic;

    /// <summary>What <c>VersionRetirementService</c> removed, and any failures it hit (which never abort the job).</summary>
    public class RetirementResult
    {
        #region Public-Members

        /// <summary>Jobs whose output was removed.</summary>
        public int JobsRetired { get; set; } = 0;

        /// <summary>Source and Cell graph nodes deleted.</summary>
        public int NodesDeleted { get; set; } = 0;

        /// <summary>Failures, one message each. Empty when everything was removed.</summary>
        public List<string> Errors { get; set; } = new List<string>();

        #endregion
    }
}
