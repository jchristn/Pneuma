namespace Pneuma.Core.Integrations.Interfaces
{
    using Pneuma.Core.Integrations.Abstractions;

    /// <summary>
    /// Verbex-backed inverted-index client. The full contract lives on the provider-neutral
    /// <see cref="IInvertedIndex"/>; this interface is the concrete-backend marker used at composition.
    /// </summary>
    public interface IVerbexClient : IInvertedIndex
    {
    }
}
