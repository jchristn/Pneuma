namespace Pneuma.Sdk.Responses
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Enums;
    using Pneuma.Sdk.Models;

    /// <summary>The result of one wizard generation.</summary>
    /// <typeparam name="T">The proposal type.</typeparam>
    public class WizardResult<T> where T : class
    {
        /// <summary>The proposal.</summary>
        public T? Value { get; set; } = null;

        /// <summary>The model endpoint that drafted it.</summary>
        public string? ModelRunnerId { get; set; } = null;

        /// <summary>The model name.</summary>
        public string? Model { get; set; } = null;

        /// <summary>Model time in milliseconds.</summary>
        public long ElapsedMs { get; set; } = 0;

        /// <summary>What the server changed or dropped.</summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>Brief step: text read from the reference URL.</summary>
        public string? GroundingExcerpt { get; set; } = null;
    }
}
