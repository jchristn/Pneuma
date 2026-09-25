namespace Test.Shared.Support
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Stages;

    /// <summary>
    /// A stage that fails the way an HTTP client timeout does inside a model call: it throws a
    /// <see cref="TaskCanceledException"/> while neither the stage deadline nor the job token has fired.
    /// </summary>
    public class InnerTimeoutStage : IStage
    {
        #region Public-Members

        /// <inheritdoc />
        public IngestionStageEnum Stage => IngestionStageEnum.Classification;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task ExecuteAsync(StageContext context, CancellationToken token)
        {
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.");
        }

        #endregion
    }
}
