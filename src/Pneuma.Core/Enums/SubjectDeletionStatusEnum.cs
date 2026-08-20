namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Lifecycle state of a subject's tracked cascade deletion. A subject that is not being deleted is
    /// <see cref="None"/>; deletion runs asynchronously in the background and records progress/errors.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SubjectDeletionStatusEnum
    {
        /// <summary>The subject is live and not queued for deletion.</summary>
        None,
        /// <summary>Deletion has been requested and is waiting for the background worker to claim it.</summary>
        Pending,
        /// <summary>The background cascade deletion is in progress.</summary>
        Deleting,
        /// <summary>The background cascade deletion failed; the subject remains and can be retried.</summary>
        Failed
    }
}
