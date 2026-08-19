namespace Pneuma.Server.Mcp
{
    /// <summary>
    /// The <c>error</c> member of a JSON-RPC 2.0 error response: a numeric code and a human-readable
    /// message.
    /// </summary>
    public class McpErrorBody
    {
        #region Public-Members

        /// <summary>JSON-RPC error code.</summary>
        public int Code { get; set; }

        /// <summary>Human-readable error message.</summary>
        public string Message { get; set; } = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initialize an error body.</summary>
        /// <param name="code">Error code.</param>
        /// <param name="message">Error message.</param>
        public McpErrorBody(int code, string message)
        {
            Code = code;
            Message = message ?? string.Empty;
        }

        #endregion
    }
}
