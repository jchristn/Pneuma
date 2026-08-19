namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Ordering applied to an enumeration.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EnumerationOrderEnum
    {
        /// <summary>Oldest records first.</summary>
        CreatedAscending,
        /// <summary>Newest records first.</summary>
        CreatedDescending
    }
}
