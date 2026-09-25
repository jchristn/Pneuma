namespace Test.Benchmark.Client
{
    using System.Collections.Generic;

    /// <summary>
    /// <c>POST /v1.0/query</c> response.
    /// </summary>
    public class QueryResult
    {
        #region Public-Members

        /// <summary>
        /// The answer text.
        /// </summary>
        public string Answer { get; set; } = string.Empty;

        /// <summary>
        /// Sources sent to the model, in presentation order ([n] citations index into this list).
        /// </summary>
        public List<GraphNodeInfo> Sources { get; set; } = new List<GraphNodeInfo>();

        /// <summary>
        /// False when nothing was retrieved.
        /// </summary>
        public bool Grounded { get; set; } = false;

        /// <summary>
        /// True when the server declined for lack of support, on builds that return it.
        /// </summary>
        public bool? InsufficientSupport { get; set; } = null;

        /// <summary>
        /// Answering model.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Generation time.
        /// </summary>
        public long? GenerationMs { get; set; } = null;

        #endregion
    }
}
