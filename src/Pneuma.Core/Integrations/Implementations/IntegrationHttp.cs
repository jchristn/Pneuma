namespace Pneuma.Core.Integrations.Implementations
{
    using System.Net.Http;

    /// <summary>
    /// Holds the single shared <see cref="HttpClient"/> instance used by all integration clients.
    /// A single client is reused for the lifetime of the process to avoid socket exhaustion.
    /// </summary>
    internal static class IntegrationHttp
    {
        /// <summary>Shared HTTP client for all integration clients.</summary>
        internal static readonly HttpClient Client = new HttpClient();
    }
}
