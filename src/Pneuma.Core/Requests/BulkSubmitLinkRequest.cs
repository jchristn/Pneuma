namespace Pneuma.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// Request to submit multiple content links for ingestion in a single call. Any supplied
    /// <see cref="Labels"/> and <see cref="Tags"/> are applied to <b>every</b> URL in the batch (the same set
    /// for all), attached to each chunk and to each link's source graph node so retrieval can later be scoped
    /// to them via a <see cref="RetrievalFilter"/>.
    /// </summary>
    public class BulkSubmitLinkRequest
    {
        /// <summary>The content URLs to ingest.</summary>
        public List<string> Urls { get; set; } = new List<string>();

        /// <summary>Optional labels applied to every URL in the batch.</summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>Optional key/value tags applied to every URL in the batch.</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
    }
}
