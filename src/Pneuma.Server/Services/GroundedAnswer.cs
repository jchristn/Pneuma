namespace Pneuma.Server.Services
{
    using System.Collections.Generic;
    using Pneuma.Core.Graph;

    /// <summary>
    /// The result of a grounded question: the answer text, the supporting graph nodes it was drawn
    /// from, whether it is grounded in retrieved sources, and whether the corpus lacked enough support
    /// to answer.
    /// </summary>
    public class GroundedAnswer
    {
        #region Public-Members

        /// <summary>The answer text.</summary>
        public string Answer { get; set; } = string.Empty;

        /// <summary>The supporting source nodes.</summary>
        public List<GraphNode> Sources { get; set; } = new List<GraphNode>();

        /// <summary>True when the answer is grounded in retrieved sources.</summary>
        public bool Grounded { get; set; }

        /// <summary>True when the corpus did not contain enough information to answer.</summary>
        public bool InsufficientSupport { get; set; }

        /// <summary>The model that produced the answer, or null when no model ran.</summary>
        public string? AnswerModel { get; set; }

        /// <summary>The wall-clock answer-generation time in milliseconds, or null when no model ran.</summary>
        public long? GenerationMs { get; set; }

        #endregion
    }
}
