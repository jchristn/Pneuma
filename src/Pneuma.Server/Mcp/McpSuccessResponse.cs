namespace Pneuma.Server.Mcp
{
    /// <summary>
    /// A JSON-RPC 2.0 success response returned by the MCP endpoint. Serialized with Pneuma's camelCase
    /// policy so the wire fields are <c>jsonrpc</c>, <c>id</c>, and <c>result</c>.
    /// </summary>
    public class McpSuccessResponse
    {
        #region Public-Members

        /// <summary>JSON-RPC protocol version; always "2.0".</summary>
        public string Jsonrpc { get; set; } = "2.0";

        /// <summary>Echoed request id (number, string, or null).</summary>
        public object? Id { get; set; }

        /// <summary>The method result payload.</summary>
        public object? Result { get; set; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initialize a success response.</summary>
        /// <param name="id">Echoed request id.</param>
        /// <param name="result">Result payload.</param>
        public McpSuccessResponse(object? id, object? result)
        {
            Id = id;
            Result = result;
        }

        #endregion
    }
}
