namespace Test.Benchmark.Runners
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Where and against what a benchmark ran, recorded in every report so results are comparable over time.
    /// </summary>
    public class BenchmarkEnvironment
    {
        #region Public-Members

        /// <summary>
        /// UTC start time.
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Pneuma base URL.
        /// </summary>
        public string ServerUrl { get; set; } = string.Empty;

        /// <summary>
        /// Git commit of the working tree, with "-dirty" when there are uncommitted changes.
        /// </summary>
        public string GitCommit { get; set; } = "unknown";

        /// <summary>
        /// OS, processor, and core count.
        /// </summary>
        public string Machine { get; set; } = string.Empty;

        /// <summary>
        /// Embedding endpoint Pneuma was configured with.
        /// </summary>
        public string Embedding { get; set; } = string.Empty;

        /// <summary>
        /// Completion endpoint used for ingestion and answers.
        /// </summary>
        public string Inference { get; set; } = string.Empty;

        /// <summary>
        /// Judge endpoint, for LLM-judged commands.
        /// </summary>
        public string? Judge { get; set; } = null;

        /// <summary>
        /// Ingest profile: full (real LLM classification and summaries) or lean (stub, retrieval-only).
        /// </summary>
        public string IngestProfile { get; set; } = "full";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Capture the current environment.
        /// </summary>
        /// <param name="serverUrl">Pneuma base URL.</param>
        /// <returns>The environment.</returns>
        public static BenchmarkEnvironment Capture(string serverUrl)
        {
            string processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? RuntimeInformation.ProcessArchitecture.ToString();
            return new BenchmarkEnvironment
            {
                ServerUrl = serverUrl,
                GitCommit = GitDescribe(),
                Machine = RuntimeInformation.OSDescription.Trim() + "; " + processor + "; " + Environment.ProcessorCount + " logical cores"
            };
        }

        #endregion

        #region Private-Methods

        private static string GitDescribe()
        {
            string? commit = RunGit("rev-parse --short HEAD");
            if (string.IsNullOrWhiteSpace(commit)) return "unknown";
            string? status = RunGit("status --porcelain --untracked-files=no");
            return commit.Trim() + (string.IsNullOrWhiteSpace(status) ? string.Empty : "-dirty");
        }

        private static string? RunGit(string arguments)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("git", arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory()
                };
                using (Process? process = Process.Start(info))
                {
                    if (process == null) return null;
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(10000);
                    return process.ExitCode == 0 ? output : null;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }

        #endregion
    }
}
