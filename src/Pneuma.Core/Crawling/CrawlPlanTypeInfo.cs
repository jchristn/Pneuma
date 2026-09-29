namespace Pneuma.Core.Crawling
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Enums;

    /// <summary>A crawler type and its settings schema (<c>GET /v1.0/crawl-plan-types</c>).</summary>
    public class CrawlPlanTypeInfo
    {
        #region Public-Members

        /// <summary>The type.</summary>
        public CrawlPlanTypeEnum Type { get; set; } = CrawlPlanTypeEnum.Web;

        /// <summary>Display name.</summary>
        public string DisplayName { get; set; } = String.Empty;

        /// <summary>What the crawler does and how it detects changes.</summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>The JSON property of a crawl plan that holds this type's settings (for example "web").</summary>
        public string SettingsProperty { get; set; } = String.Empty;

        /// <summary>The settings fields, in form order.</summary>
        public List<CrawlSettingField> Fields { get; set; } = new List<CrawlSettingField>();

        #endregion
    }
}
