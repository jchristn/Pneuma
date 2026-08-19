namespace Pneuma.Core.Integrations.Abstractions
{
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Implementations;

    /// <summary>
    /// A downstream integration that can report its own connectivity. Implemented by every integration
    /// client so startup diagnostics can probe each dependency uniformly and classify it as
    /// reachable-and-healthy, reachable-but-erroring, or unreachable.
    /// </summary>
    public interface IServiceProbe
    {
        /// <summary>Logical service name being probed.</summary>
        string ServiceName { get; }

        /// <summary>Probe the service for basic connectivity.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The health result.</returns>
        Task<IntegrationHealthResult> ProbeAsync(CancellationToken token = default);
    }
}
