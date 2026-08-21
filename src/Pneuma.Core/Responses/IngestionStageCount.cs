namespace Pneuma.Core.Responses
{
    using Pneuma.Core.Enums;

    /// <summary>
    /// A count of ingestion activity events for a single pipeline stage.
    /// </summary>
    public class IngestionStageCount
    {
        #region Public-Members

        /// <summary>Pipeline stage.</summary>
        public IngestionStageEnum Stage { get; set; } = IngestionStageEnum.Pending;

        /// <summary>Number of stage events counted.</summary>
        public long Count { get; set; } = 0;

        #endregion
    }
}
