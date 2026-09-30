namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>How an imported taxonomy combines with a draft's existing concepts.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TaxonomyImportModeEnum
    {
        /// <summary>Add new concepts and update concepts with the same key; keep the rest.</summary>
        Merge,
        /// <summary>Replace every existing concept.</summary>
        Replace
    }
}
