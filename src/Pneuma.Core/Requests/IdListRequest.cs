namespace Pneuma.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// A request body carrying a list of identifiers, used by bulk operations (for example, bulk delete) so a
    /// single server request performs the work for many entities instead of the client issuing one request each.
    /// </summary>
    public class IdListRequest
    {
        /// <summary>The identifiers to operate on. Empty or null is treated as no-op.</summary>
        public List<string> Ids { get; set; } = new List<string>();
    }
}
