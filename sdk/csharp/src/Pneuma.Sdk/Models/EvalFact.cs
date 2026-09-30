namespace Pneuma.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>A ground-truth evaluation fact: a question about a subject and its expected answer.</summary>
    public class EvalFact
    {
        /// <summary>Fact identifier (efact_).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Subject identifier.</summary>
        public string SubjectId { get; set; } = string.Empty;

        /// <summary>The question.</summary>
        public string Question { get; set; } = string.Empty;

        /// <summary>The expected answer.</summary>
        public string ExpectedAnswer { get; set; } = string.Empty;

        /// <summary>Optional category.</summary>
        public string? Category { get; set; } = null;

        /// <summary>Creation time (UTC).</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
