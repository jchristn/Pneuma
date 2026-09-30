namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;

    /// <summary>The result of one wizard generation: the proposal, or the status and reason it failed.</summary>
    /// <typeparam name="T">The proposal type.</typeparam>
    public class WizardResult<T> where T : class
    {
        #region Public-Members

        /// <summary>HTTP status to report: 200 on success, 400 for a bad draft, 502 when the model failed.</summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>True when a proposal was produced.</summary>
        public bool Success { get { return Value != null && StatusCode < 400; } }

        /// <summary>Why the generation failed, or null.</summary>
        public string? Error { get; set; } = null;

        /// <summary>The proposal.</summary>
        public T? Value { get; set; } = null;

        /// <summary>The model endpoint that produced the proposal.</summary>
        public string? ModelRunnerId { get; set; } = null;

        /// <summary>The model name.</summary>
        public string? Model { get; set; } = null;

        /// <summary>How long the model took, in milliseconds.</summary>
        public long ElapsedMs { get; set; } = 0;

        /// <summary>Things the server changed or dropped while checking the proposal.</summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>Brief step only: the text read from the grounding URL, to keep as grounding text for later steps.</summary>
        public string? GroundingExcerpt { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>A failed result.</summary>
        /// <param name="statusCode">HTTP status.</param>
        /// <param name="error">Reason.</param>
        /// <returns>The result.</returns>
        public static WizardResult<T> Fail(int statusCode, string error)
        {
            return new WizardResult<T> { StatusCode = statusCode, Error = error };
        }

        #endregion
    }
}
