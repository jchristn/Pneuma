namespace Pneuma.Core.Ingestion.Stages
{
    using System;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Models;

    /// <summary>
    /// Fetches the source bytes for the job's URL, persists them as the source artifact, and performs
    /// re-ingestion delta detection: if the fetched bytes are byte-for-byte identical to what the link last
    /// ingested successfully (matching content hash), the job can be completed immediately without re-running
    /// the expensive downstream stages. Sets <see cref="StageContext.CompleteEarly"/> in that case.
    /// </summary>
    public class ContentRetrievalStage : IStage
    {
        #region Private-Members

        private readonly StageDependencies _Deps;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the stage.</summary>
        /// <param name="deps">Shared stage dependencies.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="deps"/> is null.</exception>
        public ContentRetrievalStage(StageDependencies deps)
        {
            _Deps = deps ?? throw new ArgumentNullException(nameof(deps));
        }

        #endregion

        #region Public-Members

        /// <inheritdoc />
        public IngestionStageEnum Stage => IngestionStageEnum.ContentRetrieval;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task ExecuteAsync(StageContext context, CancellationToken token)
        {
            IngestionJob job = context.Job;

            byte[] data = await _Deps.Fetcher.FetchAsync(job.SourceUrl, token).ConfigureAwait(false);
            context.SourceBytes = data;
            await _Deps.Journal.TryStoreAsync("source", () => _Deps.Artifacts.PutSourceAsync(job.LinkId, data, null, token), token).ConfigureAwait(false);

            // Delta detection: hash the fetched bytes and, if identical to what the link last ingested successfully,
            // signal the orchestrator to complete immediately and skip the expensive downstream work. Re-processing
            // identical content is deterministic, so skipping is safe. A previously-failed link has no stored hash.
            context.ContentHash = ComputeContentHash(data);
            SubjectLink? existingLink = await _Deps.Db.SubjectLinks.ReadAsync(job.TenantId, job.LinkId, token).ConfigureAwait(false);
            if (existingLink != null
                && existingLink.Status == SubjectLinkStatusEnum.Ingested
                && !String.IsNullOrEmpty(existingLink.ContentHash)
                && String.Equals(existingLink.ContentHash, context.ContentHash, StringComparison.Ordinal))
            {
                context.CompleteEarly = true;
            }

            context.Message = "Content retrieval complete — fetched " + data.Length + " byte(s) from " + job.SourceUrl + ".";
        }

        #endregion

        #region Private-Methods

        private static string ComputeContentHash(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data ?? Array.Empty<byte>());
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        #endregion
    }
}
