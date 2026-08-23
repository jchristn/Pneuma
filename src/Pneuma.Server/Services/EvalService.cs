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
        private readonly ModelRunnerGate? _Gate;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the evaluation service.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="query">Grounded query service (answers facts and runs the judge completion).</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="gate">Optional model-runner admission gate; when supplied, each fact's answer + judge is
        /// admitted through it (waiting when saturated) so background eval yields to interactive traffic.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public EvalService(DatabaseDriverBase db, GroundedQueryService query, LoggingModule logging, ModelRunnerGate? gate = null)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Query = query ?? throw new ArgumentNullException(nameof(query));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Gate = gate;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a queued (<c>Pending</c>) evaluation run for a subject and return it immediately; the
        /// background eval worker claims and processes it. Fast — no model calls.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject to evaluate.</param>
        /// <param name="category">Optional category filter (null = all facts).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created pending run.</returns>
        public async Task<EvalRun> CreateRunAsync(string tenantId, string subjectId, string? category, CancellationToken token = default)
        {
            Subject? subject = await _Db.Subjects.ReadAsync(tenantId, subjectId, token).ConfigureAwait(false);
            List<EvalFact> facts = await _Db.EvalFacts.EnumerateBySubjectAsync(tenantId, subjectId, token).ConfigureAwait(false);
            int total = String.IsNullOrEmpty(category)
                ? facts.Count
                : facts.FindAll(f => String.Equals(f.Category, category, StringComparison.OrdinalIgnoreCase)).Count;

            EvalRun run = new EvalRun
            {
                TenantId = tenantId,
                SubjectId = subjectId,
                Category = category,
                Status = EvalRunStatusEnum.Pending,
                TotalFacts = total,
                JudgeModel = subject?.InferenceModel
            };
            await _Db.EvalRuns.CreateAsync(run, token).ConfigureAwait(false);
            return run;
        }

        /// <summary>
        /// Process a claimed run to completion: answer each fact through the grounded pipeline, judge it, and
        /// persist a per-fact result plus running tallies. Honors an operator cancel (the run's status is set to
        /// <c>Cancelled</c> out-of-band and observed between facts) and a shutdown token, and never clobbers a
        /// cancel with a later terminal status.
        /// </summary>
        /// <param name="run">The claimed run (status <c>Running</c> after <c>ClaimNextQueuedAsync</c>).</param>
        /// <param name="token">Cancellation token (server shutdown).</param>
        /// <returns>The run in its terminal state.</returns>
        public async Task<EvalRun> ProcessAsync(EvalRun run, CancellationToken token = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));

            run.Status = EvalRunStatusEnum.Running;
            await _Db.EvalRuns.UpdateAsync(run, token).ConfigureAwait(false);

            Subject? subject = await _Db.Subjects.ReadAsync(run.TenantId, run.SubjectId, token).ConfigureAwait(false);
            List<EvalFact> facts = await _Db.EvalFacts.EnumerateBySubjectAsync(run.TenantId, run.SubjectId, token).ConfigureAwait(false);
            if (!String.IsNullOrEmpty(run.Category))
            {
                facts = facts.FindAll(f => String.Equals(f.Category, run.Category, StringComparison.OrdinalIgnoreCase));
            }
            run.TotalFacts = facts.Count;

            Prompt? judge = await _Db.Prompts.ReadByKeyAsync(run.TenantId, "eval.judge", token).ConfigureAwait(false);
            string judgePrompt = String.IsNullOrWhiteSpace(judge?.Content) ? DefaultJudgePrompt : judge!.Content!;

            bool cancelledByOperator = false;
            try
            {
                foreach (EvalFact fact in facts)
                {
                    // An operator cancel sets the run's status out-of-band; observe it between facts and stop
                    // without overwriting the Cancelled status.
                    EvalRun? current = await _Db.EvalRuns.ReadAsync(run.TenantId, run.Id, token).ConfigureAwait(false);
                    if (current == null || current.Status != EvalRunStatusEnum.Running) { cancelledByOperator = true; break; }
                    token.ThrowIfCancellationRequested();

                    string produced = await AnswerAndJudgeAsync(run, subject, fact, judgePrompt, token).ConfigureAwait(false);
                    // AnswerAndJudge persisted the result and updated in-memory tallies on run; persist progress.
                    await _Db.EvalRuns.UpdateProgressAsync(run, token).ConfigureAwait(false);
                    _ = produced;
                }
                run.Status = cancelledByOperator ? EvalRunStatusEnum.Cancelled : EvalRunStatusEnum.Completed;
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
            // Persist the terminal state with a fresh token so a shutdown-cancelled run still records its outcome.
            await _Db.EvalRuns.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
            return run;
        }

        /// <summary>
        /// Create and synchronously process a run to its terminal state, returning it. Convenience for callers
        /// (and tests) that want a completed run inline rather than via the background worker.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject to evaluate.</param>
        /// <param name="category">Optional category filter (null = all facts).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The completed (or failed/cancelled) run.</returns>
        public async Task<EvalRun> RunAsync(string tenantId, string subjectId, string? category, CancellationToken token = default)
        {
            EvalRun run = await CreateRunAsync(tenantId, subjectId, category, token).ConfigureAwait(false);
            return await ProcessAsync(run, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        /// <summary>Answer one fact, judge it, persist the result, and increment the run's in-memory tallies.</summary>
        /// <returns>The produced answer text.</returns>
        private async Task<string> AnswerAndJudgeAsync(EvalRun run, Subject? subject, EvalFact fact, string judgePrompt, CancellationToken token)
        {
            IDisposable? lease = await AcquireGateAsync(token).ConfigureAwait(false);
            try
            {
                string produced;
                try
                {
                    GroundedAnswer answer = await _Query.AnswerAsync(run.TenantId, fact.Question, 8, run.SubjectId, null, token: token).ConfigureAwait(false);
                    produced = answer?.Answer ?? String.Empty;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    produced = "(error producing answer: " + exception.Message + ")";
                }

                EvalResult result = new EvalResult
                {
                    TenantId = run.TenantId,
                    RunId = run.Id,
                    FactId = fact.Id,
                    Question = fact.Question,
                    ExpectedAnswer = fact.ExpectedAnswer,
                    ProducedAnswer = produced,
                    Category = fact.Category
                };

                string judgeInput = "Question:\n" + fact.Question + "\n\nExpected answer:\n" + fact.ExpectedAnswer +
                    "\n\nProduced answer:\n" + produced;
                string? judgeRaw = await _Query.CompleteWithSubjectAsync(run.TenantId, subject, judgePrompt, judgeInput, 256, token).ConfigureAwait(false);
                ApplyJudge(result, judgeRaw);

                await _Db.EvalResults.CreateAsync(result, token).ConfigureAwait(false);
                if (result.Verdict == EvalVerdictEnum.Pass) run.PassCount++;
                else if (result.Verdict == EvalVerdictEnum.Partial) run.PartialCount++;
                else run.FailCount++;
                return produced;
            }
            finally
            {
                lease?.Dispose();
            }
        }

        /// <summary>Acquire a model-runner slot, waiting (not failing) when the system is saturated; null when no gate is configured.</summary>
        private async Task<IDisposable?> AcquireGateAsync(CancellationToken token)
        {
            if (_Gate == null) return null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    return await _Gate.AcquireAsync(token).ConfigureAwait(false);
                }
                catch (ModelRunnerBusyException)
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
            }
        }

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
