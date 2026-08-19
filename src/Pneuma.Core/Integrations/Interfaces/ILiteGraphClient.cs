namespace Pneuma.Core.Integrations.Interfaces
{
    using Pneuma.Core.Integrations.Abstractions;

    /// <summary>
    /// LiteGraph-backed knowledge-graph client. The full contract lives on the provider-neutral
    /// <see cref="IGraphRepository"/>; this interface is the concrete-backend marker used at composition.
    /// </summary>
    public interface ILiteGraphClient : IGraphRepository
    {
    }
}
