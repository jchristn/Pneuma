namespace Pneuma.Core.Integrations.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// Partio-backed semantic-processor client. The provider-neutral chunk/embed/summarize contract
    /// lives on <see cref="ISemanticProcessor"/>; this interface adds Partio's endpoint-administration
    /// surface, which is vendor-specific.
    /// </summary>
    public interface IPartioClient : ISemanticProcessor
    {
        /// <summary>
        /// Enumerate the available embedding endpoints configured in Partio.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The available embedding endpoints.</returns>
        Task<List<PartioEndpoint>> ListEmbeddingEndpointsAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate the available completion endpoints configured in Partio.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The available completion endpoints.</returns>
        Task<List<PartioEndpoint>> ListCompletionEndpointsAsync(CancellationToken token = default);

        /// <summary>Read a Partio endpoint by type ("embedding"|"completion") and id; null when not found.</summary>
        /// <param name="type">Endpoint type ("embedding" or "completion").</param>
        /// <param name="id">Endpoint id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The endpoint, or null.</returns>
        Task<PartioEndpoint?> ReadEndpointAsync(string type, string id, CancellationToken token = default);

        /// <summary>Create a Partio endpoint of the given type.</summary>
        /// <param name="type">Endpoint type ("embedding" or "completion").</param>
        /// <param name="endpoint">Endpoint to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created endpoint (including its id).</returns>
        Task<PartioEndpoint> CreateEndpointAsync(string type, PartioEndpoint endpoint, CancellationToken token = default);

        /// <summary>Update a Partio endpoint of the given type.</summary>
        /// <param name="type">Endpoint type ("embedding" or "completion").</param>
        /// <param name="id">Endpoint id.</param>
        /// <param name="endpoint">Updated endpoint.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated endpoint.</returns>
        Task<PartioEndpoint> UpdateEndpointAsync(string type, string id, PartioEndpoint endpoint, CancellationToken token = default);

        /// <summary>Delete a Partio endpoint of the given type.</summary>
        /// <param name="type">Endpoint type ("embedding" or "completion").</param>
        /// <param name="id">Endpoint id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        Task DeleteEndpointAsync(string type, string id, CancellationToken token = default);
    }
}
