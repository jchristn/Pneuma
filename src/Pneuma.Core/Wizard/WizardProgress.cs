namespace Pneuma.Core.Wizard
{
    using System;

    /// <summary>
    /// Progress of one wizard drafting call, reported while it runs: what it is doing (reading reference pages, waiting
    /// for the model, receiving its reply, asking again after an unusable reply, checking the result), the attempt, how
    /// much text the model has written, and the time so far.
    /// </summary>
    public class WizardProgress
    {
        #region Public-Members

        /// <summary>reading, waiting, writing, retrying, or checking.</summary>
        public string Phase { get; set; } = "waiting";

        /// <summary>Model call attempt, starting at 1 (2 after an unusable reply).</summary>
        public int Attempt { get; set; } = 1;

        /// <summary>Characters of the model's reply received so far in this attempt.</summary>
        public int Characters { get; set; } = 0;

        /// <summary>Milliseconds since the step started.</summary>
        public long ElapsedMs { get; set; } = 0;

        /// <summary>Detail for the phase, such as the page being read or why the reply is being asked for again.</summary>
        public string? Message { get; set; } = null;

        #endregion
    }
}
