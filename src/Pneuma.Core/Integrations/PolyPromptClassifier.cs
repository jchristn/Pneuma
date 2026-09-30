namespace Pneuma.Core.Integrations
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations.Implementations;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Serialization;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Classifies extracted cells into a candidate subgraph using an LLM via PolyPrompt.
    /// </summary>
    public class PolyPromptClassifier
    {
        #region Public-Members

        /// <summary>
        /// The default output contract (the <c>ontology.classify.format</c> prompt's seeded content): the JSON shape the
        /// classifier must return. Used when the prompt resolves to nothing.
        /// </summary>
        public const string DefaultOutputContract =
            "Respond with ONLY a JSON object of this exact shape and nothing else: " +
            "{\"nodes\":[{\"ref\":\"n1\",\"nodeType\":\"<a node type from the ontology>\",\"name\":\"...\"," +
            "\"canonicalName\":\"...\",\"content\":\"...\",\"rights\":\"UnknownPending\",\"authority\":\"ThirdParty\"," +
            "\"confidence\":0.8}],\"edges\":[{\"fromRef\":\"n1\",\"toRef\":\"n2\"," +
            "\"edgeType\":\"<a relationship type from the ontology>\",\"confidence\":0.8}]}. " +
            "Use node and relationship type names exactly as named in the ontology above. " +
            "Set rights and authority when the source makes them evident; otherwise use UnknownPending and ThirdParty.";

        #endregion

        #region Private-Members

        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the classifier.</summary>
        /// <param name="logging">Logging module.</param>
        public PolyPromptClassifier(LoggingModule logging)
        {
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the system prompt of a classification call: the task, the ontology, and the output contract.
        /// </summary>
        /// <param name="task">Classification task prompt (<c>ontology.classify</c>).</param>
        /// <param name="ontologyDefinition">The ontology definition (a pinned version's rendering, or the <c>ontology.definition</c> prompt).</param>
        /// <param name="outputContract">The output contract (<c>ontology.classify.format</c>); empty uses <see cref="DefaultOutputContract"/>.</param>
        /// <returns>The system prompt.</returns>
        public static string BuildSystemPrompt(string task, string? ontologyDefinition, string? outputContract)
        {
            StringBuilder system = new StringBuilder();
            system.AppendLine(String.IsNullOrWhiteSpace(task) ? "Classify the content into the subject knowledge-graph ontology." : task);
            if (!String.IsNullOrWhiteSpace(ontologyDefinition))
            {
                system.AppendLine();
                system.AppendLine("Ontology (use exactly the node types and relationship types described here):");
                system.AppendLine(ontologyDefinition);
            }
            system.AppendLine();
            system.Append(String.IsNullOrWhiteSpace(outputContract) ? DefaultOutputContract : outputContract!.Trim());
            return system.ToString();
        }

        /// <summary>Build the user prompt of a classification call: the subject, the cells, and any taxonomy hint.</summary>
        /// <param name="cells">Extracted cells.</param>
        /// <param name="subjectName">Display name of the subject, for grounding.</param>
        /// <param name="taxonomyHint">Matched taxonomy concepts (already phrased with the <c>taxonomy.hint</c> prompt), or null.</param>
        /// <returns>The user prompt.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="cells"/> is null.</exception>
        public static string BuildUserPrompt(List<ExtractedCell> cells, string subjectName, string? taxonomyHint)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Subject: " + subjectName);
            sb.AppendLine("Extracted cells:");
            int index = 1;
            foreach (ExtractedCell cell in cells)
            {
                if (String.IsNullOrWhiteSpace(cell.Text)) continue;
                sb.AppendLine("[" + index + "] (" + cell.Type + ") " + cell.Text);
                index++;
            }
            if (!String.IsNullOrWhiteSpace(taxonomyHint))
            {
                sb.AppendLine();
                sb.AppendLine(taxonomyHint!.Trim());
            }
            return sb.ToString();
        }

        /// <summary>Run a classification call and parse the candidate subgraph it returns.</summary>
        /// <param name="systemPrompt">System prompt (see <see cref="BuildSystemPrompt"/>).</param>
        /// <param name="userPrompt">User prompt (see <see cref="BuildUserPrompt"/>).</param>
        /// <param name="runner">Model runner to use.</param>
        /// <param name="apiKey">Decrypted API key, if any.</param>
        /// <param name="temperature">Model temperature.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The candidate subgraph (possibly empty).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="runner"/> is null.</exception>
        public async Task<CandidateSubgraph> ClassifyAsync(string systemPrompt, string userPrompt, ModelRunner runner, string? apiKey, double temperature, CancellationToken token = default)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));

            CompletionClientBase client = ModelClientFactory.Create(runner, apiKey, _Logging);
            ChatCompletionOptions options = new ChatCompletionOptions
            {
                Temperature = Math.Clamp(temperature, 0.0, 2.0),
                MaxTokens = 4096,
                SystemPrompt = systemPrompt
            };

            ChatResponse response;
            Stopwatch sw = Stopwatch.StartNew();
            string outcome = "ok";
            try
            {
                response = await client.ChatAsync(userPrompt, options, token).ConfigureAwait(false);
            }
            catch
            {
                outcome = "error";
                throw;
            }
            finally
            {
                sw.Stop();
                PneumaMetrics.RecordIntegration("polyprompt", "classify", outcome, sw.Elapsed.TotalSeconds);
            }

            if (response == null || !response.Success || String.IsNullOrWhiteSpace(response.Text))
            {
                // Surface the failure rather than returning an empty subgraph: the stage counts it as a failed batch
                // and records a warning, so a document whose classification failed is not reported as complete.
                _Logging.Warn("[PolyPromptClassifier] classification failed: " + (response?.Error ?? "no response"));
                throw ModelResponseErrors.ToException("classification", response?.Error);
            }

            return Parse(response.Text);
        }

        #endregion

        #region Private-Methods

        private CandidateSubgraph Parse(string text)
        {
            string json = ExtractJson(text);
            try
            {
                CandidateSubgraph? subgraph = Json.Deserialize<CandidateSubgraph>(json);
                return subgraph ?? new CandidateSubgraph();
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _Logging.Warn("[PolyPromptClassifier] failed to parse subgraph JSON: " + e.Message);
                throw new InvalidOperationException("The classification response was not valid subgraph JSON: " + e.Message, e);
            }
        }

        private static string ExtractJson(string text)
        {
            string trimmed = text.Trim();

            // Strip a ```json ... ``` or ``` ... ``` fence if present.
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                int firstNewline = trimmed.IndexOf('\n');
                if (firstNewline >= 0) trimmed = trimmed.Substring(firstNewline + 1);
                int fenceEnd = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (fenceEnd >= 0) trimmed = trimmed.Substring(0, fenceEnd);
                trimmed = trimmed.Trim();
            }

            // Fall back to the outermost { ... } span.
            int start = trimmed.IndexOf('{');
            int end = trimmed.LastIndexOf('}');
            if (start >= 0 && end > start) return trimmed.Substring(start, end - start + 1);
            return trimmed;
        }

        #endregion
    }
}
