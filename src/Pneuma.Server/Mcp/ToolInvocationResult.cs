namespace Pneuma.Server.Mcp
{
    using System;

    /// <summary>
    /// The outcome of executing a Pneuma tool through the context-free <see cref="PneumaToolExecutor"/>:
    /// either a JSON-serializable result payload or an error message. Unlike the MCP transport path this
    /// carries no HTTP context, so the same tool logic can be driven by the in-process agentic chat loop.
    /// </summary>
    public class ToolInvocationResult
    {
        #region Public-Members

        /// <summary>Whether the tool executed successfully.</summary>
        public bool Success { get; set; }

        /// <summary>The JSON-serializable result payload on success; null on failure.</summary>
        public object? Result { get; set; }

        /// <summary>A human-readable error message on failure; null on success.</summary>
        public string? Error { get; set; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate an empty result.</summary>
        public ToolInvocationResult()
        {
        }

        /// <summary>Build a success result carrying the given payload.</summary>
        /// <param name="result">The result payload.</param>
        /// <returns>A successful <see cref="ToolInvocationResult"/>.</returns>
        public static ToolInvocationResult Ok(object? result)
        {
            return new ToolInvocationResult { Success = true, Result = result };
        }

        /// <summary>Build a failure result carrying the given message.</summary>
        /// <param name="error">The error message.</param>
        /// <returns>A failed <see cref="ToolInvocationResult"/>.</returns>
        public static ToolInvocationResult Fail(string error)
        {
            return new ToolInvocationResult { Success = false, Error = error };
        }

        #endregion
    }
}
