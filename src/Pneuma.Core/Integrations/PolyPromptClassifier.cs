namespace Pneuma.Core.Integrations
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Graph;
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
        /// Classify a set of extracted cells into a candidate subgraph.
        /// </summary>
        /// <param name="cells">Extracted cells.</param>
        /// <param name="systemPrompt">Classification system prompt (the task framing).</param>
        /// <param name="ontologyDefinition">Admin-defined, natural-language ontology describing node and relationship types.</param>
        /// <param name="runner">Model runner to use.</param>
        /// <param name="apiKey">Decrypted API key, if any.</param>
        /// <param name="subjectName">Display name of the subject, for grounding.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The candidate subgraph (possibly empty).</returns>
        public async Task<CandidateSubgraph> ClassifyAsync(
            List<ExtractedCell> cells,
            string systemPrompt,
            string ontologyDefinition,
            ModelRunner runner,
            string? apiKey,
            string subjectName,
            CancellationToken token = default)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            if (runner == null) throw new ArgumentNullException(nameof(runner));

            CompletionClientBase client = ModelClientFactory.Create(runner, apiKey, _Logging);

            string userPrompt = BuildUserPrompt(cells, subjectName);
            StringBuilder system = new StringBuilder();
            system.AppendLine(systemPrompt);
            if (!String.IsNullOrWhiteSpace(ontologyDefinition))
            {
                system.AppendLine();
                system.AppendLine("Ontology (use exactly the node types and relationship types described here):");
                system.AppendLine(ontologyDefinition);
            }
            system.AppendLine();
            system.Append(OutputContract());

            ChatCompletionOptions options = new ChatCompletionOptions
            {
                Temperature = 0.1,
                MaxTokens = 4096,
                SystemPrompt = system.ToString()
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
                _Logging.Warn("[PolyPromptClassifier] classification failed: " + (response?.Error ?? "no response"));
                return new CandidateSubgraph();
            }

            return Parse(response.Text);
        }

        #endregion

        #region Private-Methods

        private static string OutputContract()
        {
            // Only the JSON shape is fixed; the set of node/edge types comes from the admin-defined
            // ontology supplied above, so the ontology can be changed without a code change.
            return
                "Respond with ONLY a JSON object of this exact shape and nothing else: " +
                "{\"nodes\":[{\"ref\":\"n1\",\"nodeType\":\"<a node type from the ontology>\",\"name\":\"...\"," +
                "\"canonicalName\":\"...\",\"content\":\"...\",\"rights\":\"UnknownPending\",\"authority\":\"ThirdParty\"," +
                "\"confidence\":0.8}],\"edges\":[{\"fromRef\":\"n1\",\"toRef\":\"n2\"," +
                "\"edgeType\":\"<a relationship type from the ontology>\",\"confidence\":0.8}]}. " +
                "Use node and relationship type names exactly as named in the ontology above. " +
                "Set rights and authority when the source makes them evident; otherwise use UnknownPending and ThirdParty.";
        }

        private static string BuildUserPrompt(List<ExtractedCell> cells, string subjectName)
        {
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
            return sb.ToString();
        }

        private CandidateSubgraph Parse(string text)
        {
            string json = ExtractJson(text);
            try
            {
                CandidateSubgraph? subgraph = Json.Deserialize<CandidateSubgraph>(json);
                return subgraph ?? new CandidateSubgraph();
            }
            catch (Exception e)
            {
                _Logging.Warn("[PolyPromptClassifier] failed to parse subgraph JSON: " + e.Message);
                return new CandidateSubgraph();
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
