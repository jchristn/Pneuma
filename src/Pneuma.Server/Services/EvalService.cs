namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Serialization;
    using SyslogLogging;

    /// <summary>
    /// Runs a RAG evaluation: answers each ground-truth fact through the real grounded pipeline, then judges the
    /// produced answer against the expected answer with an LLM judge, persisting a per-fact result and the run's
    /// aggregate tallies. Runs execute synchronously and are bounded by the number of facts.
    /// </summary>
    public class EvalService
    {
        #region Private-Members

        private const string DefaultJudgePrompt =
            "You are an impartial grader for a knowledge-base assistant. You are given a question, the expected " +
            "correct answer, and the answer the assistant produced. Judge whether the produced answer is correct " +
            "and complete relative to the expected answer, ignoring wording and style. Respond with ONLY a JSON " +
            "object: {\"verdict\":\"Pass|Partial|Fail\",\"score\":0-10,\"reason\":\"one sentence\",\"failureMode\":\"" +
            "missing_evidence|hallucination|incomplete|wrong|none\"}. Pass = fully correct; Partial = partially " +
            "correct or incomplete; Fail = incorrect or unsupported.";

        private readonly DatabaseDriverBase _Db;
        private readonly GroundedQueryService _Query;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the evaluation service.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="query">Grounded query service (answers facts and runs the judge completion).</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public EvalService(DatabaseDriverBase db, GroundedQueryService query, LoggingModule logging)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Query = query ?? throw new ArgumentNullException(nameof(query));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>Run an evaluation over a subject's facts (optionally one category) and return the completed run.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject to evaluate.</param>
        /// <param name="category">Optional category filter (null = all facts).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The completed (or failed/cancelled) run.</returns>
        public async Task<EvalRun> RunAsync(string tenantId, string subjectId, string? category, CancellationToken token = default)
        {
            Subject? subject = await _Db.Subjects.ReadAsync(tenantId, subjectId, token).ConfigureAwait(false);
            List<EvalFact> facts = await _Db.EvalFacts.EnumerateBySubjectAsync(tenantId, subjectId, token).ConfigureAwait(false);
            if (!String.IsNullOrEmpty(category))
            {
                facts = facts.FindAll(f => String.Equals(f.Category, category, StringComparison.OrdinalIgnoreCase));
            }

            EvalRun run = new EvalRun
            {
                TenantId = tenantId,
                SubjectId = subjectId,
                Category = category,
                Status = EvalRunStatusEnum.Running,
                TotalFacts = facts.Count,
                JudgeModel = subject?.InferenceModel
            };
            await _Db.EvalRuns.CreateAsync(run, token).ConfigureAwait(false);

            Prompt? judge = await _Db.Prompts.ReadByKeyAsync(tenantId, "eval.judge", token).ConfigureAwait(false);
            string judgePrompt = String.IsNullOrWhiteSpace(judge?.Content) ? DefaultJudgePrompt : judge!.Content!;

            try
            {
                foreach (EvalFact fact in facts)
                {
                    token.ThrowIfCancellationRequested();

                    string produced;
                    try
                    {
                        GroundedAnswer answer = await _Query.AnswerAsync(tenantId, fact.Question, 8, subjectId, null, token: token).ConfigureAwait(false);
                        produced = answer?.Answer ?? String.Empty;
                    }
                    catch (Exception exception)
                    {
                        produced = "(error producing answer: " + exception.Message + ")";
                    }

                    EvalResult result = new EvalResult
                    {
                        TenantId = tenantId,
                        RunId = run.Id,
                        FactId = fact.Id,
                        Question = fact.Question,
                        ExpectedAnswer = fact.ExpectedAnswer,
                        ProducedAnswer = produced,
                        Category = fact.Category
                    };

                    string judgeInput = "Question:\n" + fact.Question + "\n\nExpected answer:\n" + fact.ExpectedAnswer +
                        "\n\nProduced answer:\n" + produced;
                    string? judgeRaw = await _Query.CompleteWithSubjectAsync(tenantId, subject, judgePrompt, judgeInput, 256, token).ConfigureAwait(false);
                    ApplyJudge(result, judgeRaw);

                    await _Db.EvalResults.CreateAsync(result, token).ConfigureAwait(false);
                    if (result.Verdict == EvalVerdictEnum.Pass) run.PassCount++;
                    else if (result.Verdict == EvalVerdictEnum.Partial) run.PartialCount++;
                    else run.FailCount++;
                }
                run.Status = EvalRunStatusEnum.Completed;
            }
            catch (OperationCanceledException)
            {
                run.Status = EvalRunStatusEnum.Cancelled;
            }
            catch (Exception exception)
            {
                _Logging.Warn("[EvalService] run " + run.Id + " failed: " + exception.Message);
                run.Status = EvalRunStatusEnum.Failed;
                run.Error = exception.Message;
            }

            run.FinishedUtc = DateTime.UtcNow;
            await _Db.EvalRuns.UpdateAsync(run, token).ConfigureAwait(false);
            return run;
        }

        #endregion

        #region Private-Methods

        private static void ApplyJudge(EvalResult result, string? raw)
        {
            if (String.IsNullOrWhiteSpace(raw))
            {
                result.Verdict = EvalVerdictEnum.Unknown;
                result.Reason = "The judge model returned no response.";
                return;
            }

            result.Reason = raw.Length > 500 ? raw.Substring(0, 500) : raw;

            int start = raw.IndexOf('{');
            int end = raw.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                string json = raw.Substring(start, end - start + 1);
                EvalJudgeVerdict? verdict = null;
                try { verdict = Json.Deserialize<EvalJudgeVerdict>(json); }
                catch (Exception) { verdict = null; }
                if (verdict != null)
                {
                    result.Score = Math.Clamp(verdict.Score, 0, 10);
                    if (!String.IsNullOrWhiteSpace(verdict.Reason)) result.Reason = verdict.Reason;
                    result.FailureMode = verdict.FailureMode;
                    result.Verdict = ParseVerdict(verdict.Verdict);
                    return;
                }
            }
            result.Verdict = EvalVerdictEnum.Unknown;
        }

        private static EvalVerdictEnum ParseVerdict(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return EvalVerdictEnum.Unknown;
            string normalized = value.Trim().ToLowerInvariant();
            if (normalized.StartsWith("pass", StringComparison.Ordinal)) return EvalVerdictEnum.Pass;
            if (normalized.StartsWith("partial", StringComparison.Ordinal)) return EvalVerdictEnum.Partial;
            if (normalized.StartsWith("fail", StringComparison.Ordinal)) return EvalVerdictEnum.Fail;
            return EvalVerdictEnum.Unknown;
        }

        #endregion
    }
}
