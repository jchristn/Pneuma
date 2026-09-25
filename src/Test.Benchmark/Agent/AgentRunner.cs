namespace Test.Benchmark.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Datasets;
    using Test.Benchmark.Runners;

    /// <summary>
    /// Agent-in-the-loop benchmark: Claude Code runs headless on corpus-dependent tasks, once with Pneuma's MCP
    /// server connected and once with no knowledge source. Each run gets an empty temporary directory with every
    /// built-in tool disabled, so project facts can only come from Pneuma. Spends real API credits (it uses the
    /// claude CLI's own authentication).
    /// </summary>
    public class AgentRunner
    {
        #region Private-Members

        private readonly BenchmarkContext _Context;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Benchmark context.</param>
        /// <exception cref="ArgumentNullException">Thrown when the context is null.</exception>
        public AgentRunner(BenchmarkContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the suite.
        /// </summary>
        /// <param name="taskPath">Task file path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The report.</returns>
        /// <exception cref="InvalidDataException">Thrown when the task file has no tasks.</exception>
        public async Task<AgentReport> RunAsync(string taskPath, CancellationToken token)
        {
            BenchmarkArguments args = _Context.Arguments;
            AgentTaskFile? suite = JsonSerializer.Deserialize<AgentTaskFile>(File.ReadAllText(taskPath), HarnessJson.Options);
            if (suite == null || suite.Tasks.Count == 0) throw new InvalidDataException("'" + taskPath + "' has no tasks.");

            string datasetPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(taskPath)) ?? ".", suite.Dataset));
            BenchmarkDataset dataset = DatasetStore.Load(datasetPath);
            dataset.Corpora = dataset.Corpora.Take(1).ToList();

            string model = args.Get("model", "haiku");
            string mcpUrl = args.Get("mcp-url", _Context.Client.BaseUrl + "/mcp");
            List<string> arms = args.GetList("arms", "pneuma,none");
            int limit = args.GetInt("limit", 0);
            double budget = args.GetDouble("max-budget-usd", 0.50);
            string claude = args.Get("claude-path", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "claude.cmd" : "claude");

            AgentReport report = new AgentReport { Suite = suite.Name, Environment = _Context.Environment };
            report.Config["model"] = model;
            report.Config["mcpUrl"] = mcpUrl;
            report.Config["arms"] = string.Join(",", arms);
            report.Config["dataset"] = dataset.Name;
            report.Config["maxBudgetUsdPerRun"] = budget.ToString("F2", CultureInfo.InvariantCulture);

            IngestSummary ingest = new IngestSummary();
            List<ProvisionedSubject> subjects = await new SubjectProvisioner(_Context).ProvisionAsync(dataset, ingest, token).ConfigureAwait(false);
            ProvisionedSubject subject = subjects[0];
            Console.WriteLine("[agent] subject " + subject.SubjectName + " (" + subject.SubjectId + ", " + dataset.Corpora[0].Documents.Count + " documents)");

            List<AgentTask> tasks = limit > 0 ? suite.Tasks.Take(limit).ToList() : suite.Tasks;
            string root = Path.Combine(Path.GetTempPath(), "pneuma-agent-bench-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(root);
            try
            {
                string mcpConfig = Path.Combine(root, "mcp.json");
                McpConfigFile config = new McpConfigFile();
                config.McpServers["pneuma"] = new McpServerEntry { Type = "http", Url = mcpUrl, Headers = new Dictionary<string, string> { { "Authorization", "Bearer " + _Context.Client.SessionToken } } };
                File.WriteAllText(mcpConfig, JsonSerializer.Serialize(config));
                string emptyConfig = Path.Combine(root, "mcp-empty.json");
                File.WriteAllText(emptyConfig, JsonSerializer.Serialize(new McpConfigFile()));

                string pneumaPrompt = Path.Combine(root, "system-pneuma.txt");
                File.WriteAllText(pneumaPrompt,
                    "You are helping a developer on a software project named Pneuma. The project's documentation is indexed in the Pneuma MCP server (tools named mcp__pneuma__*), "
                    + "in subject id '" + subject.SubjectId + "'. Before answering a question about the project, search that subject (and read nodes for detail) and answer from what you find. "
                    + "Answer concisely. If the documentation does not contain the answer, say so.");
                string nonePrompt = Path.Combine(root, "system-none.txt");
                File.WriteAllText(nonePrompt, "You are helping a developer on a software project named Pneuma. Answer concisely. If you do not know a project-specific fact, say so rather than guessing.");

                int index = 0;
                foreach (AgentTask task in tasks)
                {
                    index++;
                    foreach (string arm in arms)
                    {
                        string workdir = Path.Combine(root, task.Id + "-" + arm);
                        Directory.CreateDirectory(workdir);
                        bool withPneuma = string.Equals(arm, "pneuma", StringComparison.OrdinalIgnoreCase);
                        AgentItem item = await RunOneAsync(claude, model, budget, task, arm, workdir, withPneuma ? mcpConfig : emptyConfig, withPneuma ? pneumaPrompt : nonePrompt, withPneuma, token).ConfigureAwait(false);
                        report.Items.Add(item);
                        Console.WriteLine("  [" + index + "/" + tasks.Count + "] " + task.Id + " " + arm.PadRight(6) + (item.Success ? " PASS" : " fail") + "  turns " + item.Turns
                            + "  $" + item.CostUsd.ToString("F4", CultureInfo.InvariantCulture) + (item.Error != null ? "  error: " + item.Error : string.Empty));
                    }
                }
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }

            foreach (IGrouping<string, AgentItem> group in report.Items.GroupBy(i => i.Arm))
            {
                List<AgentItem> items = group.ToList();
                report.Arms[group.Key] = new Dictionary<string, double>
                {
                    ["tasks"] = items.Count,
                    ["successRate"] = Math.Round(items.Average(i => i.Success ? 1.0 : 0.0), 4),
                    ["errors"] = items.Count(i => i.Error != null),
                    ["meanTurns"] = Math.Round(items.Average(i => i.Turns), 2),
                    ["meanCostUsd"] = Math.Round(items.Average(i => i.CostUsd), 5),
                    ["totalCostUsd"] = Math.Round(items.Sum(i => i.CostUsd), 4),
                    ["meanDurationMs"] = Math.Round(items.Average(i => i.DurationMs), 0)
                };
            }

            return report;
        }

        #endregion

        #region Private-Methods

        private static async Task<AgentItem> RunOneAsync(string claude, string model, double budget, AgentTask task, string arm, string workdir, string mcpConfig, string systemPromptFile, bool withPneuma, CancellationToken token)
        {
            AgentItem item = new AgentItem { TaskId = task.Id, Type = task.Type, Arm = arm };
            ProcessStartInfo info = new ProcessStartInfo(claude)
            {
                WorkingDirectory = workdir,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            // The prompt goes over stdin and the system prompt through a file, so no free text is parsed by a shell.
            info.ArgumentList.Add("-p");
            info.ArgumentList.Add("--output-format");
            info.ArgumentList.Add("json");
            info.ArgumentList.Add("--model");
            info.ArgumentList.Add(model);
            info.ArgumentList.Add("--no-session-persistence");
            info.ArgumentList.Add("--strict-mcp-config");
            info.ArgumentList.Add("--mcp-config");
            info.ArgumentList.Add(mcpConfig);
            info.ArgumentList.Add("--tools");
            info.ArgumentList.Add(string.Empty);
            if (withPneuma)
            {
                info.ArgumentList.Add("--allowedTools");
                info.ArgumentList.Add("mcp__pneuma");
            }

            info.ArgumentList.Add("--append-system-prompt-file");
            info.ArgumentList.Add(systemPromptFile);
            info.ArgumentList.Add("--max-budget-usd");
            info.ArgumentList.Add(budget.ToString(CultureInfo.InvariantCulture));

            Stopwatch wall = Stopwatch.StartNew();
            string output;
            using (Process? process = Process.Start(info))
            {
                if (process == null)
                {
                    item.Error = "could not start " + claude;
                    return item;
                }

                await process.StandardInput.WriteAsync(task.Prompt).ConfigureAwait(false);
                process.StandardInput.Close();
                Task<string> stdout = process.StandardOutput.ReadToEndAsync(token);
                Task<string> stderr = process.StandardError.ReadToEndAsync(token);
                using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromMinutes(5));
                    try
                    {
                        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        process.Kill(true);
                        item.Error = "timed out";
                        return item;
                    }
                }

                output = await stdout.ConfigureAwait(false);
                string errors = await stderr.ConfigureAwait(false);
                if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(output)) item.Error = "exit " + process.ExitCode + ": " + (errors.Length > 300 ? errors.Substring(0, 300) : errors);
            }

            item.DurationMs = Math.Round(wall.Elapsed.TotalMilliseconds, 0);
            ClaudeResult? result = null;
            int start = output.IndexOf('{');
            if (start >= 0)
            {
                try
                {
                    result = JsonSerializer.Deserialize<ClaudeResult>(output.Substring(start));
                }
                catch (JsonException)
                {
                }
            }

            if (result == null)
            {
                item.Error ??= "unparseable claude output";
                return item;
            }

            item.Answer = result.Result ?? string.Empty;
            item.Turns = result.NumTurns;
            item.CostUsd = result.TotalCostUsd;
            if (result.IsError) item.Error = result.Subtype ?? "error";
            item.Success = !result.IsError
                && task.Expect.All(p => Regex.IsMatch(item.Answer, p, RegexOptions.IgnoreCase))
                && !task.Forbid.Any(p => Regex.IsMatch(item.Answer, p, RegexOptions.IgnoreCase));
            return item;
        }

        #endregion
    }
}
