namespace Pneuma.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>A suggestion of where content for a new subject might come from.</summary>
    public class WizardSourceSuggestion
    {
        /// <summary>Links, Text, or a crawl plan type.</summary>
        public string Kind { get; set; } = "Links";

        /// <summary>Title.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Detail.</summary>
        public string? Detail { get; set; } = null;
    }
}
