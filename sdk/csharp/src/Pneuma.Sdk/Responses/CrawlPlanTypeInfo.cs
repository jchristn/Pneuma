namespace Pneuma.Sdk.Responses
{
    using System.Collections.Generic;
    using Pneuma.Sdk.Enums;

    /// <summary>A crawler type and its settings schema.</summary>
    public class CrawlPlanTypeInfo
    {
        /// <summary>The type.</summary>
        public CrawlPlanTypeEnum Type { get; set; } = CrawlPlanTypeEnum.Web;

        /// <summary>Display name.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Description.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>The plan property holding this type's settings.</summary>
        public string SettingsProperty { get; set; } = string.Empty;

        /// <summary>Settings fields in form order.</summary>
        public List<CrawlSettingField> Fields { get; set; } = new List<CrawlSettingField>();
    }
}
