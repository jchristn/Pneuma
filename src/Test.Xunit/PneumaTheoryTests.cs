namespace Test.Xunit
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Core;
    using Xunit;

    /// <summary>
    /// Reports every Pneuma Touchstone case as an individual xUnit theory row, so CI shows a pass/fail
    /// per case rather than one aggregate result. The row data is the (suiteId, caseId, displayName)
    /// triple; the descriptor is resolved and executed by suite/case id.
    /// </summary>
    public sealed class PneumaTheoryTests
    {
        /// <summary>Theory data: one row per registered Touchstone case.</summary>
        /// <returns>Rows of (suiteId, caseId, displayName).</returns>
        public static IEnumerable<object[]> Cases()
        {
            foreach (TestSuiteDescriptor suite in PneumaSuites.All)
            {
                foreach (TestCaseDescriptor testCase in suite.Cases)
                {
                    yield return new object[] { suite.SuiteId, testCase.CaseId, testCase.DisplayName };
                }
            }
        }

        /// <summary>Execute a single Touchstone case.</summary>
        /// <param name="suiteId">Suite identifier.</param>
        /// <param name="caseId">Case identifier.</param>
        /// <param name="displayName">Case display name (used for the reported test name).</param>
        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Case(string suiteId, string caseId, string displayName)
        {
            TestCaseDescriptor? testCase = Resolve(suiteId, caseId);
            Assert.True(testCase != null, "Case not found: " + suiteId + "." + caseId + " (" + displayName + ")");
            if (testCase!.Skip) return;
            await testCase.ExecuteAsync(CancellationToken.None);
        }

        private static TestCaseDescriptor? Resolve(string suiteId, string caseId)
        {
            foreach (TestSuiteDescriptor suite in PneumaSuites.All)
            {
                if (suite.SuiteId != suiteId) continue;
                foreach (TestCaseDescriptor testCase in suite.Cases)
                {
                    if (testCase.CaseId == caseId) return testCase;
                }
            }
            return null;
        }
    }
}
