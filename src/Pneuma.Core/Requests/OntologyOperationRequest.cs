namespace Pneuma.Core.Requests
{
    using System;
    using Pneuma.Core.Enums;

    /// <summary>Request body to start a background ontology operation on a subject.</summary>
    public class OntologyOperationRequest
    {
        #region Public-Members

        /// <summary>What to run: Validate, Retag, or DriftCheck.</summary>
        public OntologyOperationKindEnum Kind { get; set; } = OntologyOperationKindEnum.Validate;

        /// <summary>Cells to sample for a drift check. Default 10; capped by the server's Ontology.MaxDriftSampleSize.</summary>
        public int SampleSize { get; set; } = 10;

        #endregion
    }
}
