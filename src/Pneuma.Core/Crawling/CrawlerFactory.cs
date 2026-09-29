namespace Pneuma.Core.Crawling
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using Pneuma.Core.Enums;

    /// <summary>
    /// The registered crawlers by plan type, and the type catalog (with each type's settings schema) the dashboards
    /// render their crawl plan forms from. Registering a crawler for a type replaces the previous one. Thread-safe.
    /// </summary>
    public class CrawlerFactory
    {
        #region Private-Members

        private readonly ConcurrentDictionary<CrawlPlanTypeEnum, ICrawler> _Crawlers = new ConcurrentDictionary<CrawlPlanTypeEnum, ICrawler>();

        #endregion

        #region Public-Methods

        /// <summary>Register (or replace) the crawler for its type.</summary>
        /// <param name="crawler">The crawler.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="crawler"/> is null.</exception>
        public void Register(ICrawler crawler)
        {
            if (crawler == null) throw new ArgumentNullException(nameof(crawler));
            _Crawlers[crawler.Type] = crawler;
        }

        /// <summary>The crawler for a type.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The crawler.</returns>
        /// <exception cref="NotSupportedException">Thrown when no crawler is registered for the type.</exception>
        public ICrawler Get(CrawlPlanTypeEnum type)
        {
            ICrawler? crawler;
            if (_Crawlers.TryGetValue(type, out crawler)) return crawler;
            throw new NotSupportedException("No crawler is available for " + type + " plans on this server.");
        }

        /// <summary>True when a crawler is registered for the type.</summary>
        /// <param name="type">The type.</param>
        /// <returns>True when available.</returns>
        public bool IsAvailable(CrawlPlanTypeEnum type)
        {
            return _Crawlers.ContainsKey(type);
        }

        /// <summary>The type catalog: every registered type with its settings schema, in enum order.</summary>
        /// <returns>The catalog.</returns>
        public List<CrawlPlanTypeInfo> Catalog()
        {
            return _Crawlers.Values
                .OrderBy(c => (int)c.Type)
                .Select(c => new CrawlPlanTypeInfo
                {
                    Type = c.Type,
                    DisplayName = c.DisplayName,
                    Description = c.Description,
                    SettingsProperty = CrawlSettingsCodec.SettingsProperty(c.Type),
                    Fields = CrawlSettingsCodec.Describe(CrawlSettingsCodec.SettingsType(c.Type))
                })
                .ToList();
        }

        #endregion
    }
}
