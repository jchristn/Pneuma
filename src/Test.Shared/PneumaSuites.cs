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
                    GraphSuite.Build(),
                    IngestionSuite.Build(),
                    ExternalServicesSuite.Build(),
                    ApiSuite.Build(),
                    CollectionsSuite.Build(),
                    GraphTenancySuite.Build()
                };
            }
        }
    }
}
