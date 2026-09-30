namespace Pneuma.Sdk.Requests
{
    using System;

    /// <summary>Request for the authoring assistant to propose a draft.</summary>
    public class OntologyProposeRequest
    {
        /// <summary>Subject to sample.</summary>
        public string? SubjectId { get; set; } = null;

        /// <summary>Sample text.</summary>
        public string? SampleText { get; set; } = null;

        /// <summary>Model runner.</summary>
        public string? ModelRunnerId { get; set; } = null;

        /// <summary>Cells to sample.</summary>
        public int SampleCells { get; set; } = 20;

        /// <summary>Extra instructions.</summary>
        public string? Instructions { get; set; } = null;

        /// <summary>Language for descriptions.</summary>
        public string? Language { get; set; } = null;

        /// <summary>Version to extend.</summary>
        public string? BasedOnVersionId { get; set; } = null;
    }
}
