namespace Pneuma.Server.Services
{
    /// <summary>
    /// The output of answer synthesis: the answer text plus honest generation metadata (the model that
    /// produced it and how long generation took). Token counts are intentionally absent — the underlying
    /// completion client does not report usage, so no token figures are surfaced rather than fabricated.
    /// </summary>
    public class GeneratedAnswer
    {
        #region Public-Members

        /// <summary>The generated answer text.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>The model that produced the answer, or null when no model ran.</summary>
        public string? Model { get; set; }

        /// <summary>The wall-clock generation time in milliseconds, or null when no model ran.</summary>
        public long? DurationMs { get; set; }

        #endregion
    }
}
