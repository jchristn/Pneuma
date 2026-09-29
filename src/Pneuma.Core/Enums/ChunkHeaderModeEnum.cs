namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// What context is prepended to a chunk's text before it is embedded (the stored and returned text never changes).
    /// A chunk cut from the middle of a document often lacks the words that say what it is about; a short header of the
    /// document title and section headings gives the embedding that context.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChunkHeaderModeEnum
    {
        /// <summary>Embed the chunk text alone.</summary>
        None,
        /// <summary>Prepend the document title.</summary>
        Title,
        /// <summary>Prepend the document title and the heading path of the chunk's section.</summary>
        TitleAndHeadings
    }
}
