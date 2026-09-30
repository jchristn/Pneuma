namespace Pneuma.Sdk.Requests
{
    using System;
    using Pneuma.Sdk.Enums;

    /// <summary>Request to start an ontology operation.</summary>
    public class OntologyOperationRequest
    {
        /// <summary>Validate, Retag, or DriftCheck.</summary>
        public OntologyOperationKindEnum Kind { get; set; } = OntologyOperationKindEnum.Validate;

        /// <summary>Cells to sample (DriftCheck).</summary>
        public int SampleSize { get; set; } = 10;
    }
}
