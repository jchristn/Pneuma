namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;

    /// <summary>A suggestion of where content for a new subject might come from.</summary>
    public class WizardSourceSuggestion
    {
        #region Public-Members

        /// <summary>How to add it: Links, Text, or a crawl plan type (Web, Sitemap, GitHub, S3, AzureBlob, GoogleCloud, Cifs, Nfs, LocalFolder).</summary>
        public string Kind { get; set; } = "Links";

        /// <summary>Short title.</summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>What to look for and why it helps.</summary>
        public string? Detail { get; set; } = null;

        #endregion
    }
}
