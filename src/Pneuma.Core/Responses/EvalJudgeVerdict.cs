namespace Pneuma.Core.Responses
{
    /// <summary>
    /// The parsed JSON the judge model returns for one evaluated fact. Tolerant of a model that wraps the JSON
    /// in extra prose; the harness extracts the object before deserializing.
    /// </summary>
    public class EvalJudgeVerdict
    {
        #region Public-Members

        /// <summary>The verdict word: "Pass", "Partial", or "Fail".</summary>
        public string? Verdict { get; set; } = null;

        /// <summary>A 0–10 score.</summary>
        public double Score { get; set; } = 0;

        /// <summary>Short reasoning.</summary>
        public string? Reason { get; set; } = null;

        /// <summary>Optional failure-mode label.</summary>
        public string? FailureMode { get; set; } = null;

        #endregion
    }
}
