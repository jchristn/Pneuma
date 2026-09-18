namespace Pneuma.Chunking.Enums
{
    /// <summary>
    /// Describes how an upstream embedding endpoint applies token limits to batched inputs.
    /// </summary>
    public enum BatchLimitModeEnum
    {
        /// <summary>The token budget applies independently to each input in the batch.</summary>
        PerInput,
        /// <summary>The token budget applies to the whole request body across all batched inputs.</summary>
        WholeRequest,
        /// <summary>The endpoint limit mode is unknown.</summary>
        Unknown
    }
}
