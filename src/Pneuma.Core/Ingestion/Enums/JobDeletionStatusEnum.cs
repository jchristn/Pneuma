namespace Pneuma.Core.Ingestion.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Lifecycle state of an ingestion job's tracked cascade deletion. A job that is not being deleted is
    /// <see cref="None"/>; deletion runs asynchronously in the background and records progress/errors so it is
    /// durable across restarts (mirrors <see cref="LinkDeletionStatusEnum"/>).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum JobDeletionStatusEnum
    {
        /// <summary>The job is live and not queued for deletion.</summary>
        None,
        /// <summary>Deletion has been requested and is waiting for the background worker to claim it.</summary>
        Pending,
        /// <summary>The background cascade deletion is in progress.</summary>
        Deleting,
        /// <summary>The background cascade deletion failed; the job remains and can be retried.</summary>
        Failed
    }
}
