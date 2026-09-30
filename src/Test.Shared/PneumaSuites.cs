namespace Test.Shared
{
    using System.Collections.Generic;
    using Test.Shared.Suites;
    using Touchstone.Core;

    /// <summary>
    /// Registry of all Pneuma test suite descriptors, consumed by every runner.
    /// </summary>
    public static class PneumaSuites
    {
        /// <summary>
        /// All registered test suites.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                return new List<TestSuiteDescriptor>
                {
                    SecuritySuite.Build(),
                    DatabaseSuite.Build(),
                    AuthDatabaseSuite.Build(),
                    RbacDatabaseSuite.Build(),
                    OpsDatabaseSuite.Build(),
                    GraphSuite.Build(),
                    RetrievalSuite.Build(),
                    IngestionSuite.Build(),
                    IngestionStagesSuite.Build(),
                    ExternalServicesSuite.Build(),
                    ApiSuite.Build(),
                    CollectionsSuite.Build(),
                    GraphTenancySuite.Build(),
                    ChunkingSuite.Build(),
                    SubjectPromptSuite.Build(),
                    ConcurrencyOverridesSuite.Build(),
                    IngestionReliabilitySuite.Build(),
                    FetchSafetySuite.Build(),
                    ModelRetrySuite.Build(),
                    VersionReplacementSuite.Build(),
                    ChunkContextSuite.Build(),
                    InlineContentSuite.Build(),
                    CrawlFrameworkSuite.Build(),
                    CrawlApiSuite.Build(),
                    CrawlersSuite.Build(),
                    LinkRefreshSuite.Build(),
                    OntologySuite.Build(),
                    OntologyPipelineSuite.Build(),
                    OntologyApiSuite.Build(),
                    SubjectWizardSuite.Build()
                };
            }
        }
    }
}
