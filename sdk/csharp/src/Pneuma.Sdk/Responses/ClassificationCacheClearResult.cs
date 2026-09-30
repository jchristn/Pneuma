namespace Pneuma.Sdk.Responses
{
    using System;

    /// <summary>The result of clearing classification cache entries.</summary>
    public class ClassificationCacheClearResult
    {
        /// <summary>Entries removed.</summary>
        public int Removed { get; set; } = 0;
    }
}
