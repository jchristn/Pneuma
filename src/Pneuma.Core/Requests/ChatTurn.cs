namespace Pneuma.Core.Requests
{
    using System;

    /// <summary>
    /// One prior turn in a multi-turn assistant conversation. Only user and assistant turns are carried
    /// by the client; the system prompt is applied server-side from the tenant's configured prompt.
    /// </summary>
    public class ChatTurn
    {
        #region Public-Members

        /// <summary>Turn author: "user" or "assistant". Any other value is treated as "user".</summary>
        public string Role { get; set; } = "user";

        /// <summary>Turn text content.</summary>
        public string Content { get; set; } = String.Empty;

        #endregion
    }
}
