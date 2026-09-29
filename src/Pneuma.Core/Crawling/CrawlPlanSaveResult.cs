namespace Pneuma.Core.Crawling
{
    using System.Collections.Generic;

    /// <summary>The outcome of saving a crawl plan: the stored plan (secrets cleared) and the secrets that changed.</summary>
    public class CrawlPlanSaveResult
    {
        #region Public-Members

        /// <summary>The plan as stored, with secret values cleared and <see cref="CrawlPlan.SecretsSet"/> filled.</summary>
        public CrawlPlan Plan { get; set; } = new CrawlPlan();

        /// <summary>Names of secrets set or cleared by the save (for the audit log). Never holds values.</summary>
        public List<string> ChangedSecrets { get; set; } = new List<string>();

        #endregion
    }
}
