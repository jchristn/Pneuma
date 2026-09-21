namespace Pneuma.Core.Responses
{
    using System;

    /// <summary>
    /// Result of warming a subject's embedding model — a lightweight embed issued ahead of a real search so the
    /// model provider (e.g. an Ollama server that unloads idle models) loads the model into memory before the
    /// user's first query rather than paying that cold-load cost during the search itself. Returned by the
    /// subject-search warm-up endpoint so the dashboard can surface progress.
    /// </summary>
    public class EmbeddingWarmupResult
    {
        /// <summary>Whether the warm-up ran (the subject exists and an embedding model is configured).</summary>
        public bool Success { get; set; } = false;

        /// <summary>The subject's embedding model runner id, or null when the subject has none configured.</summary>
        public string? ModelId { get; set; } = null;

        /// <summary>The embedding model runner's display name (falls back to the id), or null when none is configured.</summary>
        public string? ModelName { get; set; } = null;

        /// <summary>Whether the warm-up embed returned a usable vector (the model is loaded and responding).</summary>
        public bool Ready { get; set; } = false;

        /// <summary>Wall-clock milliseconds the warm-up embed took (high on a cold model load, low once resident).</summary>
        public long ElapsedMs { get; set; } = 0;
    }
}
