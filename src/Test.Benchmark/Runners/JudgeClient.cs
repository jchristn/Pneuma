namespace Test.Benchmark.Runners
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Client;

    /// <summary>
    /// The LLM judge. It is called directly (never through Pneuma) and grades correctness against a gold answer,
    /// correct declines on unanswerable questions, per-claim faithfulness to the retrieved sources, and pairwise
    /// preference for thematic answers. The correctness prompts follow the Isis harness (itself LongMemEval-style)
    /// so the two suites grade alike.
    /// </summary>
    public class JudgeClient : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Judge endpoint description.
        /// </summary>
        public string Description
        {
            get
            {
                return _Client.Description;
            }
        }

        #endregion

        #region Private-Members

        private readonly DirectModelClient _Client;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Judge model settings.</param>
        public JudgeClient(ModelSettings settings)
        {
            _Client = new DirectModelClient(settings.Format, settings.Url, settings.Model, settings.ApiKey, TimeSpan.FromMinutes(10), settings.Think);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Grade an answer.
        /// </summary>
        /// <param name="question">The question.</param>
        /// <param name="gold">Gold answer, or NOT_IN_CORPUS for an unanswerable question.</param>
        /// <param name="response">The system's answer.</param>
        /// <param name="unanswerable">True when the question is unanswerable.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when accepted; null when the verdict could not be parsed.</returns>
        public async Task<bool?> GradeAsync(string question, string gold, string response, bool unanswerable, CancellationToken token)
        {
            string prompt;
            if (unanswerable)
            {
                prompt = "I will give you a question and a response from an assistant that answers only from a document collection. The collection does NOT contain the answer. "
                    + "Answer yes if the response says it does not know, cannot find it, or that the sources do not contain the answer (offering related information is fine). "
                    + "Answer no if the response asserts a specific answer to the question.\n\n"
                    + "Question: " + question + "\n\nResponse: " + response + "\n\nDoes the response correctly decline to answer? Answer yes or no only.";
            }
            else
            {
                prompt = "I will give you a question, a correct answer, and a response from a model. Answer yes if the response contains the correct answer "
                    + "or is equivalent to it (paraphrases and extra detail are fine). Answer no if the response is wrong, contradicts the correct answer, "
                    + "only gives part of a multi-part answer, or says it does not know.\n\n"
                    + "Question: " + question + "\n\nCorrect answer: " + gold + "\n\nModel response: " + response
                    + "\n\nIs the model response correct? Answer yes or no only.";
            }

            string reply = await _Client.CompleteAsync(null, prompt, 1024, token).ConfigureAwait(false);
            return ParseYesNo(reply);
        }

        /// <summary>
        /// Per-claim faithfulness: the share of the answer's factual claims that the sources support (the RAGAS
        /// definition).
        /// </summary>
        /// <param name="answer">The answer.</param>
        /// <param name="sources">The source text that was sent to the answering model.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>0..1, or null when the answer makes no factual claims or the reply cannot be parsed.</returns>
        public async Task<double?> FaithfulnessAsync(string answer, string sources, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(answer)) return null;
            string trimmedSources = sources.Length > 24000 ? sources.Substring(0, 24000) : sources;
            string prompt = "Below are SOURCES and an ANSWER written from them. Break the ANSWER into its individual factual claims (ignore statements that "
                + "the sources lack information, and ignore citation markers). For each claim, decide whether the SOURCES state or directly imply it.\n"
                + "Reply with one line per claim, each starting with SUPPORTED: or UNSUPPORTED: followed by the claim. If the answer makes no factual claims, reply NONE.\n\n"
                + "SOURCES:\n" + trimmedSources + "\n\nANSWER:\n" + answer;
            string reply = JudgeText(await _Client.CompleteAsync(null, prompt, 3000, token).ConfigureAwait(false));
            int supported = Regex.Matches(reply, "^\\s*[-*\\d.]*\\s*SUPPORTED\\s*:", RegexOptions.Multiline | RegexOptions.IgnoreCase).Count;
            int unsupported = Regex.Matches(reply, "^\\s*[-*\\d.]*\\s*UNSUPPORTED\\s*:", RegexOptions.Multiline | RegexOptions.IgnoreCase).Count;
            if (supported + unsupported == 0) return null;
            return Math.Round((double)supported / (supported + unsupported), 4);
        }

        /// <summary>
        /// Pairwise preference between two answers to a thematic question on one criterion (GraphRAG-style).
        /// </summary>
        /// <param name="question">The question.</param>
        /// <param name="first">Answer shown first.</param>
        /// <param name="second">Answer shown second.</param>
        /// <param name="criterion">comprehensiveness, diversity, or directness.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>1 when the first wins, 2 when the second wins, 0 for a tie or an unparseable reply.</returns>
        public async Task<int> PreferAsync(string question, string first, string second, string criterion, CancellationToken token)
        {
            string definition = criterion == "comprehensiveness" ? "How much detail does the answer provide to cover all aspects of the question?"
                : criterion == "diversity" ? "How varied and rich is the answer in providing different perspectives and insights?"
                : "How specifically and clearly does the answer address the question?";
            string prompt = "Compare two answers to the same question on one criterion.\nCriterion: " + criterion + " - " + definition + "\n\n"
                + "Question: " + question + "\n\nAnswer 1:\n" + first + "\n\nAnswer 2:\n" + second
                + "\n\nWhich answer is better on this criterion? Reply with exactly one of: 1, 2, TIE.";
            string reply = JudgeText(await _Client.CompleteAsync(null, prompt, 1024, token).ConfigureAwait(false)).Trim().ToUpperInvariant();
            if (reply.StartsWith("1", StringComparison.Ordinal)) return 1;
            if (reply.StartsWith("2", StringComparison.Ordinal)) return 2;
            return 0;
        }

        /// <summary>
        /// Remove &lt;think&gt; blocks some reasoning models emit inline.
        /// </summary>
        /// <param name="text">Model output.</param>
        /// <returns>The text without thinking blocks.</returns>
        public static string JudgeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string stripped = Regex.Replace(text, "<think>.*?</think>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            int open = stripped.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
            return open >= 0 ? stripped.Substring(0, open) : stripped;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private static bool? ParseYesNo(string reply)
        {
            string cleaned = JudgeText(reply).Trim().ToLowerInvariant();
            if (cleaned.StartsWith("yes", StringComparison.Ordinal)) return true;
            if (cleaned.StartsWith("no", StringComparison.Ordinal)) return false;
            if (Regex.IsMatch(cleaned, "\\byes\\b") && !Regex.IsMatch(cleaned, "\\bno\\b")) return true;
            if (Regex.IsMatch(cleaned, "\\bno\\b") && !Regex.IsMatch(cleaned, "\\byes\\b")) return false;
            return null;
        }

        /// <summary>
        /// Dispose resources.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) _Client.Dispose();
            _Disposed = true;
        }

        #endregion
    }
}
