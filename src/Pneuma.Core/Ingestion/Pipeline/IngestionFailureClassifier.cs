namespace Pneuma.Core.Ingestion.Pipeline
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Ingestion.Stages;
    using Pneuma.Core.Integrations.Implementations;

    /// <summary>
    /// Maps an ingestion failure (an exception plus the stage it surfaced in) to a failure category, a retry decision,
    /// and remediation text. The processor asks the classifier whether to retry, so retry policy lives in one place
    /// rather than in a chain of catch blocks. Stateless and thread-safe.
    /// </summary>
    public static class IngestionFailureClassifier
    {
        #region Public-Methods

        /// <summary>Classify a failure.</summary>
        /// <param name="exception">The exception that ended the attempt.</param>
        /// <param name="stage">The stage the job was in when it failed.</param>
        /// <returns>The classification; never null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is null.</exception>
        public static IngestionFailureClassification Classify(Exception exception, IngestionStageEnum stage)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));

            Exception inner = Unwrap(exception);

            if (inner is IngestionHardFailException hard) return For(hard.Category, false);
            if (inner is JobCancelledException) return For(IngestionFailureCategoryEnum.Cancelled, false);
            if (inner is PartialLossException) return For(IngestionFailureCategoryEnum.PartialLoss, true);
            if (inner is FetchBlockedException) return For(IngestionFailureCategoryEnum.Blocked, false);
            if (inner is ContentTooLargeException) return For(IngestionFailureCategoryEnum.TooLarge, false);
            if (inner is ModelEndpointUnavailableException) return For(IngestionFailureCategoryEnum.ModelUnavailable, true);
            if (inner is ModelRequestRejectedException) return For(IngestionFailureCategoryEnum.ModelRejected, false);
            if (inner is NotSupportedException) return For(IngestionFailureCategoryEnum.UnsupportedType, false);
            if (inner is TimeoutException || inner is OperationCanceledException) return For(IngestionFailureCategoryEnum.Timeout, true);

            if (inner is IntegrationClientException integration)
            {
                if (String.Equals(integration.ServiceName, "documentatom", StringComparison.OrdinalIgnoreCase))
                {
                    return For(IngestionFailureCategoryEnum.Extraction, true);
                }

                return For(IngestionFailureCategoryEnum.Storage, true);
            }

            if (inner is HttpRequestException http && stage == IngestionStageEnum.ContentRetrieval)
            {
                // A permanent client error from the source (404, 410, 401, ...) will not change on retry; a 408, a 429,
                // a server error, or a network failure might.
                bool retryable = http.StatusCode == null || IsTransientStatus((int)http.StatusCode.Value);
                return For(IngestionFailureCategoryEnum.Fetch, retryable);
            }

            if (inner is SocketException && stage == IngestionStageEnum.ContentRetrieval) return For(IngestionFailureCategoryEnum.Fetch, true);

            return ForStage(stage);
        }

        /// <summary>Build the classification for a category with its default retry policy and remediation.</summary>
        /// <param name="category">The category.</param>
        /// <returns>The classification.</returns>
        public static IngestionFailureClassification Describe(IngestionFailureCategoryEnum category)
        {
            return For(category, IsRetryableByDefault(category));
        }

        /// <summary>True when failures of the category are retried by default.</summary>
        /// <param name="category">The category.</param>
        /// <returns>True when retryable.</returns>
        public static bool IsRetryableByDefault(IngestionFailureCategoryEnum category)
        {
            switch (category)
            {
                case IngestionFailureCategoryEnum.Blocked:
                case IngestionFailureCategoryEnum.TooLarge:
                case IngestionFailureCategoryEnum.UnsupportedType:
                case IngestionFailureCategoryEnum.NoContent:
                case IngestionFailureCategoryEnum.ModelRejected:
                case IngestionFailureCategoryEnum.Configuration:
                case IngestionFailureCategoryEnum.Cancelled:
                    return false;
                default:
                    return true;
            }
        }

        #endregion

        #region Private-Methods

        private static IngestionFailureClassification ForStage(IngestionStageEnum stage)
        {
            switch (stage)
            {
                case IngestionStageEnum.ContentRetrieval:
                    return For(IngestionFailureCategoryEnum.Fetch, true);
                case IngestionStageEnum.TypeDetection:
                case IngestionStageEnum.CellExtraction:
                    return For(IngestionFailureCategoryEnum.Extraction, true);
                case IngestionStageEnum.GraphMerge:
                case IngestionStageEnum.RelationshipConsolidation:
                case IngestionStageEnum.Indexing:
                    return For(IngestionFailureCategoryEnum.Storage, true);
                default:
                    return For(IngestionFailureCategoryEnum.Internal, true);
            }
        }

        private static IngestionFailureClassification For(IngestionFailureCategoryEnum category, bool retryable)
        {
            return new IngestionFailureClassification
            {
                Category = category,
                Retryable = retryable,
                BackoffMultiplier = category == IngestionFailureCategoryEnum.ModelUnavailable ? 4 : 1,
                Remediation = RemediationFor(category)
            };
        }

        private static string RemediationFor(IngestionFailureCategoryEnum category)
        {
            switch (category)
            {
                case IngestionFailureCategoryEnum.Fetch: return "Check that the URL is reachable from the Pneuma server and returns a successful response.";
                case IngestionFailureCategoryEnum.Blocked: return "The address is private or the scheme is not allowed. Add the host to the allowed private hosts if it should be ingested.";
                case IngestionFailureCategoryEnum.TooLarge: return "The content exceeds the configured size limit. Raise the limit or split the content.";
                case IngestionFailureCategoryEnum.UnsupportedType: return "The content type is not supported. Convert it to a supported format.";
                case IngestionFailureCategoryEnum.Extraction: return "Content extraction failed. Check that DocumentAtom is running and that the file is not corrupt.";
                case IngestionFailureCategoryEnum.NoContent: return "No text could be extracted. Check that the document is not empty or image-only without OCR.";
                case IngestionFailureCategoryEnum.ModelUnavailable: return "A model endpoint was unavailable or rate limited. Check the endpoint's health and capacity.";
                case IngestionFailureCategoryEnum.ModelRejected: return "A model endpoint rejected the request. Check the model name, credentials, and maximum input tokens.";
                case IngestionFailureCategoryEnum.Configuration: return "The subject is missing required configuration. Check its models, endpoints, and collection.";
                case IngestionFailureCategoryEnum.Storage: return "A storage service (RecallDB, LiteGraph, or the blob store) failed. Check that it is running and healthy.";
                case IngestionFailureCategoryEnum.Timeout: return "A stage timed out. Check the model endpoint's timeout and the stage timeout setting.";
                case IngestionFailureCategoryEnum.PartialLoss: return "Some work was dropped and the partial-loss policy is Fail. Check the warnings for the cause.";
                case IngestionFailureCategoryEnum.WorkerLost: return "The worker stopped before the job finished. Re-ingest the link.";
                case IngestionFailureCategoryEnum.Cancelled: return "The job was stopped by an operator.";
                default: return "An unexpected error occurred. Check the server log for details.";
            }
        }

        private static bool IsTransientStatus(int status)
        {
            return status == (int)HttpStatusCode.RequestTimeout || status == 429 || status >= 500;
        }

        private static Exception Unwrap(Exception exception)
        {
            Exception current = exception;
            while (current is AggregateException aggregate && aggregate.InnerException != null) current = aggregate.InnerException;
            return current;
        }

        #endregion
    }
}
