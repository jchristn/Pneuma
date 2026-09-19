namespace Pneuma.Core.Database.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Data access for the singleton ingestion-tuning row (system-wide concurrency defaults).
    /// </summary>
    public interface IIngestionTuningMethods
    {
        /// <summary>Read the singleton tuning row, or null if it has not been created yet.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The tuning row, or null.</returns>
        Task<IngestionTuning?> ReadAsync(CancellationToken token = default);

        /// <summary>Create or replace the singleton tuning row.</summary>
        /// <param name="tuning">The tuning values to persist.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The persisted tuning row.</returns>
        Task<IngestionTuning> UpsertAsync(IngestionTuning tuning, CancellationToken token = default);
    }
}
