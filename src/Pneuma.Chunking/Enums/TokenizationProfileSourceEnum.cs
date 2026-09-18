namespace Pneuma.Chunking.Enums
{
    /// <summary>
    /// Identifies where the active tokenization profile was resolved from.
    /// </summary>
    public enum TokenizationProfileSourceEnum
    {
        /// <summary>The profile came from an explicit endpoint override.</summary>
        EndpointOverride,
        /// <summary>The profile came from dynamic provider capability resolution.</summary>
        ProviderProbe,
        /// <summary>The profile came from a provider default registry entry.</summary>
        ProviderDefault,
        /// <summary>The profile came from the server-wide fallback default.</summary>
        GlobalFallback
    }
}
