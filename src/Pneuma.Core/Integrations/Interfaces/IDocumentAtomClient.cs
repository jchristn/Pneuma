namespace Pneuma.Core.Integrations.Interfaces
{
    using Pneuma.Core.Integrations.Abstractions;

    /// <summary>
    /// DocumentAtom-backed atomizer client. The full contract lives on the provider-neutral
    /// <see cref="IAtomizer"/>; this interface is the concrete-backend marker used at composition.
    /// </summary>
    public interface IDocumentAtomClient : IAtomizer
    {
    }
}
