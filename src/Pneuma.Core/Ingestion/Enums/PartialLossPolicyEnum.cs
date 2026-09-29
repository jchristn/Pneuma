namespace Pneuma.Core.Ingestion.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// What happens when a job finishes but some of its work was dropped along the way (a failed classification batch,
    /// a failed cell summary, or a cell node that could not be created).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PartialLossPolicyEnum
    {
        /// <summary>The job completes and records a warning for each loss. This is the default.</summary>
        Warn,
        /// <summary>The job fails with category PartialLoss and is retried.</summary>
        Fail
    }
}
