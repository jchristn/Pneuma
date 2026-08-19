namespace Pneuma.Server.Mcp
{
    /// <summary>
    /// A JSON-RPC 2.0 error response returned by the MCP endpoint. Serialized with Pneuma's camelCase
    /// policy so the wire fields are <c>jsonrpc</c>, <c>id</c>, and <c>error</c>.
    /// </summary>
    public class McpErrorResponse
    {
        #region Public-Members

        /// <summary>JSON-RPC protocol version; always "2.0".</summary>
        public string Jsonrpc { get; set; } = "2.0";

        /// <summary>Echoed request id (number, string, or null).</summary>
        public object? Id { get; set; }

        /// <summary>The error body.</summary>
        public McpErrorBody Error { get; set; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initialize an error response.</summary>
        /// <param name="id">Echoed request id.</param>
        /// <param name="code">JSON-RPC error code.</param>
        /// <param name="message">Error message.</param>
        public McpErrorResponse(object? id, int code, string message)
        {
            Id = id;
            Error = new McpErrorBody(code, message);
        }

        #endregion
    }
}
