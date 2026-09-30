namespace Pneuma.Sdk.Requests
{
    using System;

    /// <summary>Request to start a new draft.</summary>
    public class OntologyDraftRequest
    {
        /// <summary>Version to copy, or null for the newest.</summary>
        public string? BasedOnVersionId { get; set; } = null;
    }
}
