namespace Pneuma.Chunking.Enums
{
    /// <summary>
    /// Strategy for handling chunk overlap.
    /// </summary>
    public enum OverlapStrategyEnum
    {
        /// <summary>Mechanical overlap by token count.</summary>
        SlidingWindow,
        /// <summary>Adjust overlap boundaries to nearest sentence boundary.</summary>
        SentenceBoundaryAware,
        /// <summary>Adjust overlap boundaries to nearest paragraph or heading boundary.</summary>
        SemanticBoundaryAware
    }
}
